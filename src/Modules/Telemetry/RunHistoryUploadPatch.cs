using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using ShoujoKageki.Core.Runtime.Modules.Shine;

namespace ShoujoKageki.Core.Telemetry;

[HarmonyPatch(typeof(RunHistoryUtilities), nameof(RunHistoryUtilities.CreateRunHistoryEntry))]
internal static class RunHistoryUploadPatch
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 4096 };
    private static readonly Dictionary<string, RunHistoryUploadQueue> Queues = new(StringComparer.Ordinal);
    private static readonly Lazy<string> DefaultUrl = new(LoadDefaultUrl);
    internal static Uri? Endpoint
    {
        get
        {
            string url = TelemetryModule.Context.Configuration.Current.TelemetryBaseUrl;
            if (string.IsNullOrWhiteSpace(url)) url = DefaultUrl.Value;
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) ? uri : null;
        }
    }
    internal static Uri? ErrorEndpoint => Endpoint is { } endpoint ? new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/errors") : null;
    private static string LoadDefaultUrl()
    {
        try
        {
            using var stream = typeof(RunHistoryUploadPatch).Assembly.GetManifestResourceStream("CoreTelemetryEndpoint.json");
            if (stream == null) return "";
            using var json = JsonDocument.Parse(stream);
            return json.RootElement.GetProperty("Url").GetString() ?? "";
        }
        catch (Exception) { TelemetryModule.Warn("数据上传配置无效"); return ""; }
    }
    private static RunHistoryUploadQueue? Queue()
    {
        if (Endpoint is not { } endpoint) return null;
        lock (Queues)
        {
            if (!Queues.TryGetValue(endpoint.AbsoluteUri, out var queue))
            {
                queue = new RunHistoryUploadQueue(Path.Combine(TelemetryModule.Context.DataDirectory, "telemetry", "pending"),
                    endpoint, HttpClient, () => TelemetryModule.Active && TelemetryModule.Context.Configuration.Current.EnableDataUpload && Endpoint == endpoint, TelemetryModule.Warn);
                Queues.Add(endpoint.AbsoluteUri, queue);
            }
            return queue;
        }
    }
    internal static void ResumePendingUploads()
    {
        if (!TelemetryModule.Context.Configuration.Current.EnableDataUpload) return;
        try { if (Queue() is { } queue) _ = queue.FlushAsync(); }
        catch (Exception error) { TelemetryModule.Warn($"暂无法恢复对局队列 {error.GetType().Name}"); }
    }
    private static void RemoveShineValues(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (string key in new[] { "karen_shine_current", "karen_shine_max", "core_shine_current", "core_shine_max" }) obj.Remove(key);
            foreach (var property in obj) RemoveShineValues(property.Value);
        }
        else if (node is JsonArray array)
            for (int i = array.Count - 1; i >= 0; i--)
                if (array[i] is JsonObject saved && saved["name"] is JsonValue name && name.TryGetValue<string>(out var text) &&
                    text is "karen_shine_current" or "karen_shine_max" or "core_shine_current" or "core_shine_max") array.RemoveAt(i);
                else RemoveShineValues(array[i]);
    }
    private static void Postfix()
    {
        if (!TelemetryModule.Context.Configuration.Current.EnableDataUpload || RunManager.Instance.History is not { } history) return;
        try
        {
            if (Queue() is not { } queue) return;
            string json = ShineHistory.InjectIntoHistoryJson(JsonSerializationUtility.ToJson(history));
            var payload = JsonNode.Parse(json)!;
            RemoveShineValues(payload);
            TelemetryModule.EnrichHistory(payload);
            if (queue.Enqueue(payload.ToJsonString())) _ = queue.FlushAsync();
        }
        catch (Exception error) { TelemetryModule.Warn($"对局快照暂无法上传 {error.GetType().Name}"); }
    }
}
