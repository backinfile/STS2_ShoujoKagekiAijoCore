using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Modding;
using ShoujoKageki.Core.V1;
using System.IO;

namespace ShoujoKageki.Core.Telemetry;

/// <summary>The callback only captures bounded strings; a single background timer persists them.</summary>
internal static class CoreErrorReporter
{
    private static readonly System.Net.Http.HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 4096 };
    private static ClientErrorBuffer? _buffer;
    private static readonly TelemetryChannel Channel = new(endpoint => new TelemetryUploadQueue(
        Path.Combine(TelemetryModule.Context.DataDirectory, "telemetry", "errors"), endpoint, Client,
        () => TelemetryModule.Active && TelemetryModule.ErrorUploadEnabled && RunHistoryUploadPatch.ErrorEndpoint == endpoint,
        _ => { }, maxItems: 32, maxBytes: 2 * 1024 * 1024, maxPayloadBytes: 64 * 1024,
        requestInterval: TimeSpan.FromMinutes(1), conflictIsSuccess: false));
    private static CoreErrorLogger? _logger;
    private static System.Threading.Timer? _timer;
    private static int _ticking;
    internal static string LastFailure { get; private set; } = "";
    [ThreadStatic] private static bool _capturing;

    internal static void Initialize()
    {
        if (_timer != null) return;
        try
        {
            var endpoint = RunHistoryUploadPatch.ErrorEndpoint;
            if (endpoint == null) return;
            string version = ModManager.Mods.FirstOrDefault(mod => mod.manifest?.id == "ShoujoKagekiAijoKaren")?.manifest?.version ?? "unknown";
            string os = OS.GetName();
            string platform = os == "Android" ? "android" : "pc";
            var release = ReleaseInfoManager.Instance.ReleaseInfo;
            // Discovery and disabled-mod filtering precede ModInitializer callbacks. Include mods
            // scheduled after Karen and failed initializers, not just those already fully loaded.
            var mods = ModManager.Mods.Where(mod => mod.manifest?.id != null && mod.manifest.id != CoreApi.ModId &&
                    mod.state is ModLoadState.None or ModLoadState.Loaded or ModLoadState.Failed)
                .Select(mod => new ClientErrorBuffer.ModInfo(mod.manifest!.id!, mod.manifest.name ?? mod.manifest.id!, mod.manifest.version ?? "unknown"))
                .OrderBy(mod => mod.id, StringComparer.Ordinal).Take(129).ToArray();
            _buffer = new ClientErrorBuffer(release?.Version ?? "unknown", version, platform,
                os: os, gameCommit: release?.Commit ?? "unknown", mods: mods, coreContext: () => TelemetryModule.Snapshot());
            Channel.Select(endpoint);
            _logger = new CoreErrorLogger();
            OS.AddLogger(_logger);
            // Do not mark unobserved exceptions as handled or alter normal crash behavior.
            TaskScheduler.UnobservedTaskException += OnUnobserved;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));
        }
        catch (Exception error)
        {
            LastFailure = error.GetType().Name;
        }
    }

    internal static void ConfigurationChanged()
    {
        if (_timer == null) Initialize();
        var queue = Channel.Select(RunHistoryUploadPatch.ErrorEndpoint);
        if (TelemetryModule.ErrorUploadEnabled) _ = queue?.FlushAsync();
        else
        {
            _buffer?.Clear();
            Channel.Pause();
        }
    }

    private static void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args) => CaptureException(args.Exception);
    private static void OnUnhandled(object sender, UnhandledExceptionEventArgs args) => CaptureException(args.ExceptionObject);
    internal static void Shutdown()
    {
        Channel.Pause();
        _timer?.Dispose();
        _timer = null;
        TaskScheduler.UnobservedTaskException -= OnUnobserved;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        if (_logger != null) { OS.RemoveLogger(_logger); _logger.Dispose(); _logger = null; }
        _buffer?.Clear();
    }

    internal static void CaptureException(object? error)
    {
        if (!TelemetryModule.ErrorUploadEnabled || _buffer == null || _capturing) return;
        try
        {
            string text = error?.ToString() ?? "Unhandled exception";
            if (text.Length > 16384) text = text[..16384];
            int newline = text.IndexOf('\n');
            Capture("managed", newline < 0 ? text : text[..newline], newline < 0 ? "" : text[(newline + 1)..]);
        }
        catch (Exception failure) { LastFailure = failure.GetType().Name; }
    }

    internal static void Capture(string source, string message, string stack, Func<string>? stackFactory = null)
    {
        if (_capturing || !TelemetryModule.ErrorUploadEnabled || _buffer == null) return;
        try { _capturing = true; _buffer.Capture(source, message, stack, stackFactory); }
        catch (Exception failure) { LastFailure = failure.GetType().Name; }
        finally { _capturing = false; }
    }

    private static void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) != 0) return;
        try
        {
            var queue = Channel.Select(RunHistoryUploadPatch.ErrorEndpoint);
            if (!TelemetryModule.Active || !TelemetryModule.ErrorUploadEnabled || _buffer == null
                || queue == null || RunHistoryUploadPatch.ErrorEndpoint != queue.Endpoint) return;
            _buffer.Persist(payload => TelemetryModule.ErrorUploadEnabled
                && RunHistoryUploadPatch.ErrorEndpoint == queue.Endpoint && queue.Enqueue(payload));
            _ = queue.FlushAsync();
        }
        catch (Exception failure) { LastFailure = failure.GetType().Name; }
        finally { Volatile.Write(ref _ticking, 0); }
    }
}
