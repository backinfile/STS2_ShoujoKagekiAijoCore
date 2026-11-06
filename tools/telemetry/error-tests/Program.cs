using System.Net;
using System.Text.Json;
using ShoujoKageki.Core.Telemetry;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var now = DateTimeOffset.UtcNow;
ClientErrorBuffer Buffer() => new("0.107.1", "v0.1.6", "pc", () => now, os:"Windows", gameCommit:"test-build",
    mods:new[] {new ClientErrorBuffer.ModInfo("BaseLib", "BaseLib", "v3.2.1")});
var buffer = Buffer();
for (int i = 0; i < 100; i++) buffer.Capture("game", $"Failed card {i}", $"at Karen.Play() line {i}");
Check(buffer.GroupCount == 1, "Dynamic values must coalesce");
var bodies = new List<string>();
buffer.Persist(payload => { bodies.Add(payload); return true; });
using var first = JsonDocument.Parse(bodies.Single());
Check(first.RootElement.GetProperty("count").GetInt32() == 100, "Coalesced count");
buffer.Capture("game", "Failed card 123", "at Karen.Play() line 12");
buffer.Persist(payload => { bodies.Add(payload); return true; });
using var second = JsonDocument.Parse(bodies.Last());
Check(first.RootElement.GetProperty("report_id").GetString() == second.RootElement.GetProperty("report_id").GetString(), "Report ID must survive snapshots");
Check(second.RootElement.GetProperty("count").GetInt32() == 101, "Snapshots use cumulative count");
Console.WriteLine("PASS aggregation and cumulative snapshots");

buffer = Buffer(); int expensive = 0;
for (int i = 0; i < 100000; i++) buffer.Capture("engine", "failure", "", () => { expensive++; return "trace"; });
Check(expensive == 120 && buffer.Suppressed == 99880, "Flood must be rejected before stack formatting");
now += TimeSpan.FromMinutes(1);
buffer.Capture("engine", "failure", "", () => { expensive++; return "trace"; });
Check(expensive == 121, "Capture budget must refill");
buffer = Buffer();
for (int i = 0; i < 120; i++) buffer.Capture("game", "failure" + (char)('A' + i), "trace");
Check(buffer.GroupCount == 64, "Distinct-error memory bound");
Console.WriteLine("PASS pre-format rate gate and group capacity");

var windowsTestPath = string.Join("\\", new[] { "C:", "Users", "PrivateName", "file.cs" });
var unixTestPath = string.Join("/", new[] { "", "home", "private", "file" });
var text = $"Authorization: Bearer PRIVATE_TOKEN\npassword=PRIVATE_PASSWORD\nhttps://host.invalid/secret?q=x\n{windowsTestPath}\n{unixTestPath}\ntest@example.invalid 76561199000000001 192.168.3.9";
text += "\nAuthorization: Basic PRIVATE_BASIC\n\"password\": \"PRIVATE SPACE PASSWORD\"";
var clean = ClientErrorBuffer.Clean(text, 2048);
foreach (string secret in new[] { "PRIVATE_TOKEN", "PRIVATE_PASSWORD", "PRIVATE_BASIC", "SPACE", "PrivateName", "host.invalid", "example.invalid", "76561199000000001", "192.168.3.9" })
    Check(!clean.Contains(secret), "Sensitive value escaped cleaning: " + secret);
Check(ClientErrorBuffer.Clean(new string('x', 1000000), 2048).Length == 2048, "Text bound");
Console.WriteLine("PASS sanitization and text bounds");

buffer = Buffer();
var resourceUris = new[] { "res://images/example.tres", "res://audio/example.ogg", "event:/sfx/example" };
foreach (var uri in resourceUris)
    buffer.Capture("engine", "Failed loading resource: " + uri, "", () => $"at Load('{uri}')");
var resourceBodies = new List<string>();
buffer.Persist(payload => { resourceBodies.Add(payload); return true; });
Check(resourceBodies.Count == resourceUris.Length, "Different resource paths must remain distinct");
foreach (var payload in resourceBodies)
{
    using var report = JsonDocument.Parse(payload);
    string message = report.RootElement.GetProperty("message").GetString()!;
    Check(resourceUris.Any(uri => message == "Failed loading resource: " + uri &&
        report.RootElement.GetProperty("stack").GetString() == $"at Load('{uri}')"), "Resource URI must survive capture and persistence");
}
var diskPaths = new[] { windowsTestPath, windowsTestPath.Replace('\\', '/').Replace("C:", "d:"),
    "\\\\" + string.Join("\\", new[] { "server", "private", "file.cs" }), unixTestPath };
foreach (var path in diskPaths)
    foreach (var prefix in new[] { "", "path=", "路径：", "路径" })
    {
        string mixed = $"res://images/example.tres\n{prefix}{path}\nevent:/sfx/example";
        string expected = $"res://images/example.tres\n{prefix}<path>\nevent:/sfx/example";
        Check(ClientErrorBuffer.Clean(mixed, 2048) == expected, "Disk paths must stay private beside resource URIs");
        Check(ClientErrorBuffer.Clean(expected, 2048) == expected, "Repeated sanitization must preserve resource URIs");
    }
Console.WriteLine("PASS resource URIs, distinct reports and disk-path privacy");

Check(first.RootElement.GetProperty("platform").GetString()=="pc" && first.RootElement.GetProperty("os").GetString()=="Windows", "PC and OS are distinct");
Check(first.RootElement.GetProperty("game_commit").GetString()=="test-build" && first.RootElement.GetProperty("mods")[0].GetProperty("version").GetString()=="v3.2.1", "Build and mod versions preserved");
var android = new ClientErrorBuffer("0.111.0", "v0.1.6", "android", os:"Android", mods:Enumerable.Range(0,200).Select(i=>new ClientErrorBuffer.ModInfo("Mod"+i,new string('名',80),new string('v',40))));
android.Capture("game", new string('错',2048), new string('栈',8192));
string androidBody="";android.Persist(payload=>{androidBody=payload;return true;});
using var androidJson=JsonDocument.Parse(androidBody);
Check(androidJson.RootElement.GetProperty("mods_truncated").GetBoolean() && androidJson.RootElement.GetProperty("mods").GetArrayLength()<=128,"Mod inventory bounded and truncation explicit");
Check(System.Text.Encoding.UTF8.GetByteCount(androidBody)<=65536,"Worst-case Unicode report plus inventory fits request budget");
var incomplete = new ClientErrorBuffer("0.107.1", "v0.1.6", "pc", mods:new[] {new ClientErrorBuffer.ModInfo("Unversioned", "", "")});
incomplete.Capture("game","failure","");string incompleteBody="";incomplete.Persist(payload=>{incompleteBody=payload;return true;});
using var incompleteJson=JsonDocument.Parse(incompleteBody);
Check(incompleteJson.RootElement.GetProperty("mods")[0].GetProperty("name").GetString()=="Unversioned" && incompleteJson.RootElement.GetProperty("mods")[0].GetProperty("version").GetString()=="unknown","Incomplete manifests must not reject the report");
Console.WriteLine("PASS PC/Android, game build and bounded mod inventory");

buffer = Buffer();buffer.Capture("game", "failure", "trace");
Check(buffer.Persist(_ => false) == 0 && buffer.Persist(_ => true) == 1, "Full queue must preserve unsaved count");
buffer.Capture("game", "failure", "trace");buffer.Clear();
Check(buffer.Persist(_ => true) == 0, "Disable must clear memory");
buffer = Buffer();
for (int i=0;i<300;i++) { now += TimeSpan.FromMinutes(1); buffer.Capture("game", "failure", "trace"); buffer.Persist(_ => true); }
Check(buffer.Persist(_ => true) == 0, "Lifetime snapshot bound");
Console.WriteLine("PASS queue-full, clear and lifetime bounds");

string root = Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
var requests = new List<DateTimeOffset>();int calls=0;
bool enabled = true;
using var client = new HttpClient(new TestHandler(_ => {
    requests.Add(now);calls++;
    var response = new HttpResponseMessage(calls == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.Created);
    if (calls == 1) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
    return response;
}));
RunHistoryUploadQueue Queue(string dir) => new(dir, new Uri("https://example.invalid/errors"), client, () => enabled, _ => {},
    () => now, span => { now += span; return Task.CompletedTask; }, maxItems:2, maxBytes:65536, maxPayloadBytes:1024, requestInterval:TimeSpan.FromMinutes(1));
var queue = Queue(Path.Combine(root, Guid.NewGuid().ToString("N")));
Check(queue.Enqueue("{\"id\":1}") && queue.Enqueue("{\"id\":2}"), "Error queue accepts bounded entries");
Check(!queue.Enqueue("{\"id\":3}") && !queue.Enqueue(new string('x',1025)), "Error-specific queue budgets");
enabled=false;await queue.FlushAsync();Check(calls==0,"Disabled queue must stay offline");
enabled=true;await queue.FlushAsync();
Check(calls==3 && requests[1]-requests[0]>=TimeSpan.FromSeconds(90) && requests[2]-requests[1]>=TimeSpan.FromMinutes(1), "Requests honor Retry-After and minimum spacing");
Console.WriteLine("PASS persistence, setting, request spacing and Retry-After");

using (var conflictClient = new HttpClient(new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict))))
{
    string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
    var conflictQueue = new RunHistoryUploadQueue(directory, new Uri("https://example.invalid/errors"), conflictClient, () => true, _ => { }, conflictIsSuccess: false);
    conflictQueue.Enqueue("{\"report_id\":\"conflict\"}");
    await conflictQueue.FlushAsync();
    Check(Directory.GetFiles(directory, "*.rejected", SearchOption.AllDirectories).Length == 1, "Error identity conflicts must remain available for diagnosis");
}
Console.WriteLine("PASS error identity conflict retention");

sealed class TestHandler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
}
