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
    private static readonly Dictionary<string, TelemetryUploadQueue> Queues = new(StringComparer.Ordinal);
    private static TelemetryUploadQueue? _activeQueue;
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
    private static TelemetryUploadQueue? Queue()
    {
        var endpoint = Endpoint;
        lock (Queues)
        {
            if (endpoint == null)
            {
                _activeQueue?.Pause();
                return _activeQueue = null;
            }
            if (!Queues.TryGetValue(endpoint.AbsoluteUri, out var queue))
            {
                queue = new TelemetryUploadQueue(Path.Combine(TelemetryModule.Context.DataDirectory, "telemetry", "pending"),
                    endpoint, HttpClient, () => TelemetryModule.Active && TelemetryModule.DataUploadEnabled && Endpoint == endpoint, TelemetryModule.Warn,
                    preparePayload: RunHistorySanitizer.Sanitize);
                Queues.Add(endpoint.AbsoluteUri, queue);
            }
            if (!ReferenceEquals(_activeQueue, queue)) _activeQueue?.Pause();
            return _activeQueue = queue;
        }
    }
    internal static void Shutdown()
    {
        lock (Queues)
            foreach (var queue in Queues.Values) queue.Pause();
    }
    internal static void ResumePendingUploads()
    {
        try
        {
            if (Queue() is not { } queue) return;
            if (TelemetryModule.DataUploadEnabled) _ = queue.FlushAsync();
            else queue.Pause();
        }
        catch (Exception error) { TelemetryModule.Warn($"暂无法恢复对局队列 {error.GetType().Name}"); }
    }
    private static void Postfix()
    {
        if (!TelemetryModule.DataUploadEnabled || RunManager.Instance.History is not { } history) return;
        try
        {
            if (Queue() is not { } queue) return;
            string json = ShineHistory.InjectIntoHistoryJson(JsonSerializationUtility.ToJson(history));
            var payload = JsonNode.Parse(json)!;
            TelemetryModule.EnrichHistory(payload);
            if (queue.Enqueue(payload.ToJsonString())) _ = queue.FlushAsync();
        }
        catch (Exception error) { TelemetryModule.Warn($"对局快照暂无法上传 {error.GetType().Name}"); }
    }
}
