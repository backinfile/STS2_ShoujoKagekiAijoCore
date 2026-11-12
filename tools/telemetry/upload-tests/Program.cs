using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ShoujoKageki.Core.Telemetry;

internal static class Program
{
    private static readonly Uri Endpoint = new("https://example.invalid/collector");
    private static string Root = "";
    private static DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static string Folder() { var path = Path.Combine(Root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static Task Advance(TimeSpan value) { Now += value; return Task.CompletedTask; }
    private static TelemetryUploadQueue Queue(string root, HttpClient client, Func<bool>? enabled = null) =>
        new(root, Endpoint, client, enabled ?? (() => true), _ => { }, () => Now, Advance, preparePayload: RunHistorySanitizer.Sanitize);

    private static async Task Main(string[] args)
    {
        string[] counters = { "karen_shine_current", "karen_shine_max", "core_shine_current", "core_shine_max" };
        var properties = new System.Text.Json.Nodes.JsonArray();
        var card = new System.Text.Json.Nodes.JsonObject { ["core_shine_exhaust_count"] = 2 };
        foreach (string prefix in new[] { "", "CardModel_" })
            foreach (string counter in counters)
            {
                card[prefix + counter] = 7;
                properties.Add(new System.Text.Json.Nodes.JsonObject { ["name"] = prefix + counter, ["value"] = 7 });
            }
        properties.Add(new System.Text.Json.Nodes.JsonObject { ["name"] = "CardModel_other", ["value"] = 9 });
        card["props"] = new System.Text.Json.Nodes.JsonObject { ["ints"] = properties };
        var history = new System.Text.Json.Nodes.JsonObject { ["nested"] = new System.Text.Json.Nodes.JsonArray(card) };
        RunHistorySanitizer.RemoveShineValues(history);
        Check(properties.Count == 1 && properties[0]!["name"]!.GetValue<string>() == "CardModel_other", "Actual SavedSpireField names must be removed");
        Check(card.Count == 2 && card["core_shine_exhaust_count"]!.GetValue<int>() == 2, "Remove counters while retaining exhaustion history");
        Console.WriteLine("PASS actual SavedSpireField counters filtered before upload");

        Root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Root);
        Check(TelemetryUploadQueue.ShouldUploadCharacters(new[] { "custom_host", "KAREN" }), "Karen in second seat must upload");
        Check(TelemetryUploadQueue.ShouldUploadCharacters(new[] { "REGENT" }), "Regent must upload");
        Check(TelemetryUploadQueue.ShouldUploadCharacters(new[] { "NECROBINDER" }), "Necrobinder must upload");
        Check(TelemetryUploadQueue.ShouldUploadCharacters(new[] { "custom_character" }), "Discovered custom characters must upload");
        Console.WriteLine("PASS party selection");

        const string payload = "{\"card\":\"华恋\",\"id\":76561199000000001}";
        string retryRoot = Folder();
        var retry = new Handler((call, request) =>
        {
            var response = new HttpResponseMessage(call < 3 ? (call == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.TooManyRequests) : HttpStatusCode.Created);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });
        using (var client = new HttpClient(retry))
        {
            var queue = Queue(retryRoot, client);
            Check(queue.Enqueue(payload), "Enqueue must persist payload");
            Check(Directory.GetFiles(retryRoot, "*.json", SearchOption.AllDirectories).Length == 1, "Payload must exist before any request");
            await queue.FlushAsync();
            Check(retry.Calls == 3 && retry.LastBody == payload, "503/429 must retry original bytes");
            Check(Directory.GetFiles(retryRoot, "*.json", SearchOption.AllDirectories).Length == 0, "Acknowledged payload must leave queue");
        }
        Console.WriteLine("PASS transient errors and Retry-After");

        string restartRoot = Folder();
        var offline = new Handler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using (var client = new HttpClient(offline))
        {
            var queue = Queue(restartRoot, client);
            queue.Enqueue(payload);
            await queue.FlushAsync();
            Check(offline.Calls == TelemetryUploadQueue.MaxAttempts, "Attempts must be bounded");
            Check(Directory.GetFiles(restartRoot, "*.json", SearchOption.AllDirectories).Length == 1, "Exhausted payload must survive");
        }
        Now += TimeSpan.FromHours(1);
        var online = new Handler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using (var client = new HttpClient(online))
        {
            await Queue(restartRoot, client).FlushAsync();
            Check(online.Calls == 1 && online.LastBody == payload, "Next launch must recover the durable payload");
        }
        Console.WriteLine("PASS bounded retries and restart recovery");

        string disabledRoot = Folder();
        bool enabled = false;
        var gated = new Handler((_, _) => new HttpResponseMessage(HttpStatusCode.Created));
        using (var client = new HttpClient(gated))
        {
            var queue = Queue(disabledRoot, client, () => enabled);
            queue.Enqueue(payload);
            await queue.FlushAsync();
            Check(gated.Calls == 0, "Disabled upload must not send queued data");
            enabled = true;
            await queue.FlushAsync();
            Check(gated.Calls == 1, "Re-enabling must flush preserved data");
        }
        Console.WriteLine("PASS upload setting");

        string rejectedRoot = Folder();
        var rejected = new Handler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using (var client = new HttpClient(rejected))
        {
            var queue = Queue(rejectedRoot, client);
            queue.Enqueue(payload);
            await queue.FlushAsync();
            await queue.FlushAsync();
            Check(rejected.Calls == 1, "Permanent rejection must not retry");
            Check(Directory.GetFiles(rejectedRoot, "*.rejected", SearchOption.AllDirectories).Length == 1, "Rejected payload must remain locally inspectable");
        }
        Console.WriteLine("PASS permanent rejection");

        string legacyRoot = Folder();
        const string legacyPayload = "{\"props\":{\"ints\":[{\"name\":\"CardModel_core_shine_current\",\"value\":3},{\"name\":\"other\",\"value\":9}]}}";
        var legacyHandler = new Handler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using (var client = new HttpClient(legacyHandler))
        {
            var oldQueue = new TelemetryUploadQueue(legacyRoot, Endpoint, client, () => true, _ => { }, () => Now, Advance);
            Check(oldQueue.Enqueue(legacyPayload), "Legacy queued payload fixture");
            await Queue(legacyRoot, client).FlushAsync();
            Check(legacyHandler.Calls == TelemetryUploadQueue.MaxAttempts && !legacyHandler.LastBody!.Contains("shine_current") && legacyHandler.LastBody.Contains("other"), "Recovered queue must sanitize before sending");
            string path = Directory.GetFiles(legacyRoot, "*.json", SearchOption.AllDirectories).Single();
            Check(!File.ReadAllText(path).Contains("shine_current"), "Retried legacy snapshot must be saved without counters");
        }
        Console.WriteLine("PASS recovered queue counters removed before retry and persistence");

        string boundRoot = Folder();
        using (var client = new HttpClient(new Handler((_, _) => new HttpResponseMessage(HttpStatusCode.Created))))
        {
            var queue = Queue(boundRoot, client);
            for (int index = 0; index < TelemetryUploadQueue.MaxItems; index++) Check(queue.Enqueue("{\"id\":" + index + "}"), "Queue must accept within limit");
            Check(!queue.Enqueue("{\"id\":999}"), "Full queue must reject without deleting earlier data");
            Check(Directory.GetFiles(boundRoot, "*.json", SearchOption.AllDirectories).Length == TelemetryUploadQueue.MaxItems, "Existing queue must stay intact");
        }
        Console.WriteLine("PASS capacity bound");
        await WorkerLifecycle();
    }

    private static async Task WorkerLifecycle()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0, active = 0, maximum = 0;
        using (var client = new HttpClient(new AsyncHandler(async (_, token) =>
        {
            int running = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, running);
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
            }
            Interlocked.Decrement(ref active);
            return new HttpResponseMessage(HttpStatusCode.Created);
        })))
        {
            var queue = Queue(Folder(), client);
            queue.Enqueue("{\"id\":1}");
            var first = queue.FlushAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var wakes = Enumerable.Range(0, 20).Select(_ => queue.FlushAsync()).ToArray();
            Check(wakes.All(task => ReferenceEquals(first, task)), "Repeated wakeups must share the running task");
            queue.Enqueue("{\"id\":2}");
            release.SetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            Check(calls == 2 && maximum == 1, "New payloads must drain through one worker");
        }
        Console.WriteLine("PASS coalesced wakeups and enqueue during delivery");

        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockedDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool recovered = false;
        string pausedRoot = Folder();
        var handler = new Handler((_, _) => new HttpResponseMessage(recovered ? HttpStatusCode.Created : HttpStatusCode.ServiceUnavailable));
        using (var client = new HttpClient(handler))
        {
            var queue = new TelemetryUploadQueue(pausedRoot, Endpoint, client, () => true, _ => {}, () => Now,
                _ => { waiting.TrySetResult(); return blockedDelay.Task; });
            queue.Enqueue("{\"id\":3}");
            var worker = queue.FlushAsync();
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queue.Pause();
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
            Check(handler.Calls == 1 && Directory.GetFiles(pausedRoot, "*.json", SearchOption.AllDirectories).Length == 1,
                "Pause during backoff must stop promptly and retain the payload");
            recovered = true;
            Now += TimeSpan.FromHours(1);
            await queue.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(handler.Calls == 2 && Directory.GetFiles(pausedRoot, "*.json", SearchOption.AllDirectories).Length == 0,
                "The same queue must resume preserved data after a pause");
        }
        Console.WriteLine("PASS pause and resume during retry backoff");

        entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string interruptedRoot = Folder();
        using (var client = new HttpClient(new AsyncHandler(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.Created);
        })))
        {
            var queue = Queue(interruptedRoot, client);
            queue.Enqueue("{\"id\":4}");
            var worker = queue.FlushAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queue.Pause();
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
            Check(Directory.GetFiles(interruptedRoot, "*.json", SearchOption.AllDirectories).Length == 1,
                "Stopping an in-flight request must preserve its unacknowledged payload");
        }
        Console.WriteLine("PASS request cancellation retains unacknowledged data");
        using (var client = new HttpClient(new Handler((_, _) => new HttpResponseMessage(HttpStatusCode.Created))))
        {
            int created = 0;
            string channelRoot = Folder();
            var channel = new TelemetryChannel(endpoint =>
            {
                created++;
                return new TelemetryUploadQueue(channelRoot, endpoint, client, () => true, _ => { });
            });
            var first = channel.Select(Endpoint);
            var other = channel.Select(new Uri("http://127.0.0.1:9/other"));
            Check(ReferenceEquals(first, channel.Select(Endpoint)) && created == 2,
                "Switching back must reuse the same endpoint queue");
            Check(channel.Select(null) == null && ReferenceEquals(other, channel.Select(new Uri("http://127.0.0.1:9/other"))),
                "Disabling and restoring an endpoint retains its queue");
            channel.Pause();
        }
        Console.WriteLine("PASS channel endpoint ownership");
        entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int channelActive = 0, channelMaximum = 0;
        using (var client = new HttpClient(new AsyncHandler(async (_, _) =>
        {
            int running = Interlocked.Increment(ref channelActive);
            channelMaximum = Math.Max(channelMaximum, running);
            try
            {
                entered.TrySetResult();
                await release.Task;
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            finally { Interlocked.Decrement(ref channelActive); }
        })))
        {
            string channelRoot = Folder();
            var channel = new TelemetryChannel(endpoint => new TelemetryUploadQueue(channelRoot, endpoint, client, () => true, _ => { }));
            var firstQueue = channel.Select(Endpoint)!;
            firstQueue.Enqueue("{\"id\":5}");
            var inFlight = firstQueue.FlushAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            channel.Select(new Uri("http://127.0.0.1:9/other"));
            var resumed = channel.Select(Endpoint)!;
            var resumedTask = resumed.FlushAsync();
            Check(ReferenceEquals(inFlight, resumedTask), "Switching back during cancellation must retain the in-flight worker");
            release.TrySetResult();
            await resumedTask.WaitAsync(TimeSpan.FromSeconds(5));
            Check(channelMaximum == 1, "A delayed cancellation must not run concurrent endpoint requests");
            channel.Pause();
        }
        Console.WriteLine("PASS endpoint switch during an in-flight request");


    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => response(request, cancellationToken);
    }

    private sealed class Handler(Func<int, HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return response(Calls, request);
        }
    }
}
