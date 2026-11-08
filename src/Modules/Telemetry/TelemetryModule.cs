using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Models;
using ShoujoKageki.Core.Runtime;
using ShoujoKageki.Core.V1;

namespace ShoujoKageki.Core.Telemetry;

public sealed class TelemetryModule : ICoreModule, IDisposable
{
    internal static CoreContext Context { get; private set; } = null!;
    private CoreContext? _context;
    internal static bool Active { get; private set; }
    internal static bool DataUploadEnabled => Context.Configuration.Current is { EnableInformationCollection: true, EnableDataUpload: true };
    internal static bool ErrorUploadEnabled => Context.Configuration.Current is { EnableInformationCollection: true, EnableErrorUpload: true };
    public string Id => "telemetry";
    public int Order => 100;
    public void Initialize(CoreContext context)
    {
        Context = _context = context;
        Active = true;
        context.Configuration.Changed += ConfigurationChanged;
        context.ShuttingDown += Dispose;
        try { LegacyQueueMigration.Import(); }
        catch (Exception error) { Warn($"旧遥测队列迁移暂停，原记录保留 {error.GetType().Name}"); }
        CoreErrorReporter.Initialize();
        RunHistoryUploadPatch.ResumePendingUploads();
    }
    private static void ConfigurationChanged(CoreSettings oldSettings, CoreSettings newSettings)
    {
        CoreErrorReporter.ConfigurationChanged();
        if (newSettings.EnableInformationCollection && newSettings.EnableDataUpload) RunHistoryUploadPatch.ResumePendingUploads();
    }
    internal static void Warn(string message) => Context.Diagnostics.Report(DiagnosticSeverity.Warning, "telemetry", "delivery", message);
    private static string Version(string modId, string version) => string.IsNullOrWhiteSpace(version) ? (modId == "sts2" ? Context.GameVersion : "unknown") : version;
    internal static object Snapshot(IReadOnlyList<CoreModelInfo>? models = null)
    {
        var mods = Context.Discovery.Mods.OrderBy(mod => mod.Id, StringComparer.Ordinal).Take(128)
            .Select(mod => new { id = mod.Id, name = mod.Id, version = Version(mod.Id, mod.Version) });
        if (models != null)
            mods = models.GroupBy(model => model.ModId).Select(group => new { id = group.Key, name = group.Key, version = Version(group.Key, group.First().ModVersion) });
        return new
        {
            schema_version = 2,
            core_version = Context.ReleaseVersion,
            api_version = CoreApi.ApiMajor.ToString(),
#if STS2_BETA
            game_target = "beta",
#else
            game_target = "stable",
#endif
            game_version = Context.GameVersion,
            source_mods = mods.OrderBy(mod => mod.id, StringComparer.Ordinal).Take(128).ToArray(),
            model_sources = (models ?? Array.Empty<CoreModelInfo>()).Take(8192).OrderBy(model => model.Id, StringComparer.Ordinal)
                .Select(model => new { model_id = model.Id, source_mod = model.ModId, source_version = Version(model.ModId, model.ModVersion) }).ToArray()
        };
    }
    internal static void EnrichHistory(JsonNode payload)
    {
        var models = Context.Discovery.Models.GroupBy(model => model.Id).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        AddModelIdentity(payload, models, referenced);
        payload["core_telemetry"] = JsonSerializer.SerializeToNode(Snapshot(referenced.Select(id => models[id]).ToArray()));
    }
    private static void AddModelIdentity(JsonNode? node, IReadOnlyDictionary<string, CoreModelInfo> models, ISet<string> referenced)
    {
        if (node is JsonValue scalar && scalar.TryGetValue<string>(out var text) && models.ContainsKey(text)) referenced.Add(text);
        if (node is JsonObject obj)
        {
            foreach (var child in obj.ToArray()) AddModelIdentity(child.Value, models, referenced);
            if (obj["id"] is JsonValue value && value.TryGetValue<string>(out var id) && models.TryGetValue(id, out var model) && typeof(CardModel).IsAssignableFrom(model.ModelType))
                obj["model_full_id"] = model.Id;
        }
        else if (node is JsonArray array)
            foreach (var child in array) AddModelIdentity(child, models, referenced);
    }
    public void Dispose()
    {
        if (_context == null) return;
        _context.Configuration.Changed -= ConfigurationChanged;
        _context.ShuttingDown -= Dispose;
        Active = false;
        CoreErrorReporter.Shutdown();
        _context = null;
    }
}
