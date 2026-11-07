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
    private static RunHistoryUploadQueue Queue(string root, HttpClient client, Func<bool>? enabled = null) =>
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
        Check(RunHistoryUploadQueue.ShouldUploadCharacters(new[] { "custom_host", "KAREN" }), "Karen in second seat must upload");
        Check(RunHistoryUploadQueue.ShouldUploadCharacters(new[] { "REGENT" }), "Regent must upload");
        Check(RunHistoryUploadQueue.ShouldUploadCharacters(new[] { "NECROBINDER" }), "Necrobinder must upload");
        Check(RunHistoryUploadQueue.ShouldUploadCharacters(new[] { "custom_character" }), "Discovered custom characters must upload");
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
            Check(offline.Calls == RunHistoryUploadQueue.MaxAttempts, "Attempts must be bounded");
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
            var oldQueue = new RunHistoryUploadQueue(legacyRoot, Endpoint, client, () => true, _ => { }, () => Now, Advance);
            Check(oldQueue.Enqueue(legacyPayload), "Legacy queued payload fixture");
            await Queue(legacyRoot, client).FlushAsync();
            Check(legacyHandler.Calls == RunHistoryUploadQueue.MaxAttempts && !legacyHandler.LastBody!.Contains("shine_current") && legacyHandler.LastBody.Contains("other"), "Recovered queue must sanitize before sending");
            string path = Directory.GetFiles(legacyRoot, "*.json", SearchOption.AllDirectories).Single();
            Check(!File.ReadAllText(path).Contains("shine_current"), "Retried legacy snapshot must be saved without counters");
        }
        Console.WriteLine("PASS recovered queue counters removed before retry and persistence");

        string boundRoot = Folder();
        using (var client = new HttpClient(new Handler((_, _) => new HttpResponseMessage(HttpStatusCode.Created))))
        {
            var queue = Queue(boundRoot, client);
            for (int index = 0; index < RunHistoryUploadQueue.MaxItems; index++) Check(queue.Enqueue("{\"id\":" + index + "}"), "Queue must accept within limit");
            Check(!queue.Enqueue("{\"id\":999}"), "Full queue must reject without deleting earlier data");
            Check(Directory.GetFiles(boundRoot, "*.json", SearchOption.AllDirectories).Length == RunHistoryUploadQueue.MaxItems, "Existing queue must stay intact");
        }
        Console.WriteLine("PASS capacity bound");
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
