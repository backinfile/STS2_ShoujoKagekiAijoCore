using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Godot;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.Bootstrap;

[ModInitializer(nameof(Initialize))]
public static class Bootstrap
{
    public const int BootstrapProtocol = 1;
    public const string ModId = "ShoujoKagekiCore";
    internal const string LegacyModId = "ShoujoKagekiAijoCore";
    internal const string CoordinationKey = "ShoujoKagekiAijoCore.BootstrapProtocol1";
    private const string IdentityKey = CoordinationKey + ".ModId";
    private static readonly MegaCrit.Sts2.Core.Logging.Logger Log = new(ModId, LogType.Generic);
    private const string BridgeId = ModId + ".Discovery";
    internal static Type[] RuntimeTypes { get; private set; } = [];
    private static Mod? _logicalMod;

    private sealed class Startup
    {
        internal string Stage = "mod identity";
        internal string Game = "unknown";
        internal string Release = "unknown";
        internal string Target = "none";
        internal string Baseline = "none";
        internal string Fingerprint = "unknown";
        internal Assembly? Api;
        internal Assembly? Runtime;

        public override string ToString() =>
            $"stage={Stage}, game={Game}, core={Release}, target={Target}, baseline={Baseline}, fingerprint={Fingerprint}";
    }

    public static void Initialize() => InitializeCore(false);

    public static void InitializeFromLegacy() => InitializeCore(true);

    private static void InitializeCore(bool fromLegacy)
    {
        lock (AppDomain.CurrentDomain)
        {
            var prior = AppDomain.CurrentDomain.GetData(CoordinationKey) as string;
            if (prior == "ready" && AppDomain.CurrentDomain.GetData(IdentityKey) as string == ModId) return;
            var startup = new Startup();
            var bridges = new Harmony(BridgeId);
            try
            {
                if (prior != null && !(fromLegacy && prior == "initializing"))
                    throw new InvalidOperationException($"Core bootstrap is {prior}. Restart after correcting the reported error.");
                AppDomain.CurrentDomain.SetData(CoordinationKey, "initializing");
                InitializeOnce(startup, bridges);
                AppDomain.CurrentDomain.SetData(IdentityKey, ModId);
                AppDomain.CurrentDomain.SetData(CoordinationKey, "ready");
            }
            catch (Exception error)
            {
                if (prior != "ready") AppDomain.CurrentDomain.SetData(CoordinationKey, "failed");
                RuntimeTypes = [];
                // The native loader assigns Loaded after an initializer exception, then raises this event.
                ModManager.OnModDetected += MarkFailed;
                foreach (var mod in ModManager.Mods.Where(x => x.manifest?.id == ModId || DependsOnCore(x)))
                    if (mod.state is ModLoadState.None or ModLoadState.Loaded) mod.state = ModLoadState.Failed;

                Log.Error($"[Core startup] failed {startup}: {error}");
                TryFailureStep("assembly association", () =>
                {
                    // The native association method is available only on games with a multi-assembly Mod.
                    if (_logicalMod != null && AccessTools.Field(typeof(Mod), "assemblies")?.GetValue(_logicalMod) is IList assemblies)
                    {
                        if (startup.Runtime != null) assemblies.Remove(startup.Runtime);
                        if (startup.Api != null) assemblies.Remove(startup.Api);
                    }
                });
                TryFailureStep("discovery bridges", () => bridges.UnpatchAll(BridgeId));
                TryFailureStep("player message", () =>
                {
                    var config = Type.GetType("BaseLib.Config.ModConfig+ModConfigLogger, BaseLib");
                    (config?.GetProperty("PendingUserMessages")?.GetValue(null) as IList<string>)?.Add(
                        $"Core 加载失败（游戏 {startup.Game}，Core {startup.Release}，阶段 {startup.Stage}）。{error.GetBaseException().Message}");
                });
                throw;
            }
        }
    }

    private static void MarkFailed(Mod mod)
    {
        if (mod.manifest?.id != ModId) return;
        mod.state = ModLoadState.Failed;
        ModManager.OnModDetected -= MarkFailed;
    }

    private static void TryFailureStep(string step, Action action)
    {
        try { action(); }
        catch (Exception error) { Log.Error($"[Core startup] {step} cleanup failed: {error}"); }
    }

    private static bool DependsOnCore(Mod mod) => mod.manifest?.dependencies?.Any(d => d.id == ModId) == true;
    private static bool Eligible(Mod mod) => mod.state is ModLoadState.None or ModLoadState.DisabledDuplicate;

    private static void InitializeOnce(Startup startup, Harmony bridges)
    {
        startup.Stage = "mod identity";
        var entries = ModManager.Mods.Where(x => x.manifest?.id == ModId && Eligible(x)).ToArray();
        var location = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location);
        _logicalMod = entries.FirstOrDefault(x => !string.IsNullOrEmpty(location)
            && Path.TrimEndingDirectorySeparator(Path.GetFullPath(x.path)).Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)), StringComparison.OrdinalIgnoreCase))
            // Android may report a canonical assembly path instead of the mod directory alias.
            ?? (entries.Length == 1 ? entries[0] : throw new InvalidOperationException("Cannot identify the active Core mod entry."));
        startup.Release = _logicalMod.manifest?.version ?? "unknown";
        startup.Game = ReleaseInfoManager.Instance?.SemVer?.ToString() ?? "unknown";
        startup.Stage = "dependencies";
        var baseLib = ModManager.Mods.FirstOrDefault(x => x.manifest?.id == "BaseLib" && x.state == ModLoadState.Loaded)
            ?? throw new InvalidOperationException("缺少已加载的 BaseLib，请按 BaseLib → Core → 角色 Mod 排列，并安装包含 BaseLibCompatibilityHelper 的 BaseLib 3.4.8 完整包。");
        var modTypesCache = AccessTools.Field(typeof(ReflectionHelper), "_modTypes")
            ?? throw new MissingFieldException(typeof(ReflectionHelper).FullName, "_modTypes");
        if (modTypesCache.GetValue(null) != null)
            throw new InvalidOperationException("Core 在模型反射缓存初始化后加载，请按 BaseLib → Core → 角色 Mod 排列。");
        var requirements = new List<CoreRequirements>();
        var minimumRelease = "1.0.0";
        var legacy = ModManager.Mods.FirstOrDefault(x => x.manifest?.id == LegacyModId && x.state == ModLoadState.None);
        var needsLegacyApi = false;
        foreach (var role in ModManager.Mods.Where(x => x.state == ModLoadState.None))
        {
            var manifest = role.manifest;
            if (manifest?.dependencies == null || manifest.id is ModId or LegacyModId) continue;
            var dependencies = manifest.dependencies
                .Where(d => d.id == ModId || (legacy != null && d.id == LegacyModId)).ToArray();
            if (dependencies.Length == 0) continue;
            needsLegacyApi |= dependencies.Any(d => d.id == LegacyModId);
            foreach (var dependency in dependencies)
                if (dependency.minVersion != null && SemVersion.Parse(dependency.minVersion).CompareTo(SemVersion.Parse(minimumRelease)) > 0) minimumRelease = dependency.minVersion;
            var path = Path.Combine(role.path, "core.requirements");
            if (!File.Exists(path)) throw new InvalidDataException($"{manifest.id} 缺少 core.requirements，请重新安装完整角色包。");
            var requirement = BundleSelection.Read<CoreRequirements>(path);
            if (requirement.ModId != manifest.id) throw new InvalidDataException($"{manifest.id} 的 Core 需求身份不一致。");
            requirements.Add(requirement);
        }
        startup.Stage = "bundle selection";
        var candidates = new List<BundleCandidate>();
        foreach (var mod in ModManager.Mods.Where(x => x.manifest?.id == ModId && Eligible(x)))
        {
            try { candidates.Add(BundleSelection.Validate(mod.path, mod.modSource + ":" + Path.GetFullPath(mod.path))); }
            catch (Exception error) { Log.Warn($"[Core candidate rejected] {mod.modSource} {error.Message}"); }
        }
        startup.Stage = "game ABI";
        var compatibleTarget = DetectRuntimeTarget();
        startup.Stage = "bundle selection";
        startup.Target = compatibleTarget;
        if (!candidates.Any(candidate => BundleSelection.SelectTarget(candidate.Bundle, startup.Game, compatibleTarget) != null))
            throw new InvalidOperationException($"No Core bundle provides the {compatibleTarget} runtime for game {startup.Game}. " +
                $"Checked {string.Join(", ", candidates.Select(candidate => $"{candidate.Bundle.ReleaseVersion}/{candidate.Fingerprint}"))}. 请更新 Core 工坊订阅或 Karen 包。");
        var selected = BundleSelection.Select(candidates.Where(candidate => !needsLegacyApi || candidate.Bundle.LegacyApiAssembly != null),
            requirements, startup.Game, baseLib.manifest!.version ?? "0.0.0", minimumRelease, compatibleTarget);
        var target = BundleSelection.SelectTarget(selected.Bundle, startup.Game, compatibleTarget)!;
        startup.Release = selected.Bundle.ReleaseVersion;
        startup.Baseline = target.Target.MinimumGameVersion;
        startup.Fingerprint = selected.Fingerprint;
        if (SemVersion.Parse(startup.Game).CompareTo(SemVersion.Parse(target.Target.MaximumGameVersion)) > 0)
            Log.Warn($"[Core startup] Game {startup.Game} is newer than the reference range; trying ABI-compatible {target.Name} runtime built for {startup.Baseline}.");

        startup.Stage = "assembly preflight";
        var context = AssemblyLoadContext.GetLoadContext(typeof(Bootstrap).Assembly) ?? AssemblyLoadContext.Default;
        startup.Api = LoadExact(context, BundleSelection.ResolvePath(selected.Root, selected.Bundle.ApiAssembly));
        if (needsLegacyApi)
            LoadExact(context, BundleSelection.ResolvePath(selected.Root, selected.Bundle.LegacyApiAssembly!), allowIdenticalCopy: true);
        startup.Runtime = LoadExact(context, BundleSelection.ResolvePath(selected.Root, target.Target.Assembly));
        _ = startup.Api.GetTypes();
        var types = startup.Runtime.GetTypes();
        var entry = startup.Runtime.GetType("ShoujoKageki.Core.Runtime.RuntimeEntry", true)!;
        var initialize = entry.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static, null,
            [typeof(string), typeof(string), typeof(string)], null)
            ?? throw new MissingMethodException(entry.FullName, "Initialize");

        startup.Stage = "discovery bridges";
        bridges.CreateClassProcessor(typeof(ModTypesBridge)).Patch();
        bridges.CreateClassProcessor(typeof(SubtypeBridge)).Patch();

        startup.Stage = "resource registration";
        if (!ProjectSettings.LoadResourcePack(BundleSelection.ResolvePath(selected.Root, selected.Bundle.ResourcePack), true))
            throw new InvalidDataException("Core 公共资源包挂载失败。");
        ScriptManagerBridge.LookupScriptsInAssembly(startup.Runtime);

        startup.Stage = "native association";
        _logicalMod.version = SemanticVersion.TryFromString(startup.Release, out var version)
            ? version : throw new InvalidDataException("Invalid release version.");
        _logicalMod.manifest!.version = startup.Release;
        foreach (var duplicate in ModManager.Mods.Where(x => x != _logicalMod && x.manifest?.id == ModId && Eligible(x)))
            duplicate.state = ModLoadState.DisabledDuplicate;
        var associate = typeof(ModManager).GetMethod("AssociateAssemblyWithMod", [typeof(string), typeof(Assembly)]);
        associate?.Invoke(null, [ModId, startup.Api]);
        associate?.Invoke(null, [ModId, startup.Runtime]);

        startup.Stage = "runtime initialization";
        initialize.Invoke(null, [startup.Release, startup.Game, startup.Fingerprint]);
        RuntimeTypes = types;
        if (legacy != null)
        {
            legacy.version = _logicalMod.version;
            legacy.manifest!.version = startup.Release;
            legacy.manifest.affectsGameplay = false;
            legacy.state = ModLoadState.Loaded;
            foreach (var duplicate in ModManager.Mods.Where(x => x != legacy && x.manifest?.id == LegacyModId && Eligible(x)))
                duplicate.state = ModLoadState.DisabledDuplicate;
            Log.Info($"[Core startup] Using {LegacyModId} as an alias for Core {startup.Release}; legacy runtime skipped.");
        }
        Log.Info($"[Core startup] ready {startup}, candidates={candidates.Count}.");
    }

    internal static string DetectRuntimeTarget()
    {
        var choice = typeof(PlayerChoiceContext);
        var owner = choice.GetProperty("OwnerId");
        var action = choice.GetProperty("GameActionType");
        var parameters = owner == null ? new[] { typeof(PlayerChoiceOptions) } : [typeof(Player), typeof(PlayerChoiceOptions)];
        var begun = choice.GetMethod("SignalPlayerChoiceBegun", parameters);
        var ended = choice.GetMethod("SignalPlayerChoiceEnded", Type.EmptyTypes);
        if (begun is not { IsAbstract: true } || begun.ReturnType != typeof(Task)
            || ended is not { IsAbstract: true } || ended.ReturnType != typeof(Task)
            || (owner != null && (owner.PropertyType != typeof(ulong?) || owner.GetMethod is not { IsAbstract: true }
                || choice.GetProperty("ModelStack")?.PropertyType != typeof(IEnumerable<AbstractModel>)))
            || (action != null && (owner == null || Nullable.GetUnderlyingType(action.PropertyType)?.Name != "GameActionType"
                || action.GetMethod is not { IsAbstract: true })))
            throw new NotSupportedException("Unsupported PlayerChoiceContext ABI (OwnerId, GameActionType, ModelStack or choice signals).");

        var card = typeof(CardModel);
        var result = owner == null
            ? AccessTools.Method(card, "GetResultPileTypeForCardPlay", Type.EmptyTypes)
            : AccessTools.Method(card, "GetResultLocationForCardPlay", Type.EmptyTypes);
        if (result == null || (owner == null ? result.ReturnType != typeof(PileType) : result.ReturnType.Name != "CardLocation"))
            throw new NotSupportedException("Unsupported CardModel play result ABI.");
        return action != null ? "beta" : owner != null ? "main" : "stable";
    }

    private static Assembly LoadExact(AssemblyLoadContext context, string path, bool allowIdenticalCopy = false)
    {
        var identity = AssemblyName.GetAssemblyName(path);
        var existing = context.Assemblies.FirstOrDefault(x => x.GetName().Name == identity.Name);
        if (existing != null)
        {
            if (!Path.GetFullPath(existing.Location).Equals(path, StringComparison.OrdinalIgnoreCase)
                && !(allowIdenticalCopy && File.ReadAllBytes(existing.Location).AsSpan().SequenceEqual(File.ReadAllBytes(path))))
                throw new InvalidOperationException($"Core assembly {identity.Name} was loaded before bundle selection.");
            return existing;
        }
        return context.LoadFromAssemblyPath(path);
    }
}

[HarmonyPatch(typeof(ReflectionHelper), nameof(ReflectionHelper.ModTypes), MethodType.Getter)]
internal static class ModTypesBridge
{
    private static void Postfix(ref Type[] __result)
    {
        var types = Bootstrap.RuntimeTypes;
        if (types.Length != 0) __result = __result.Concat(types).Distinct().ToArray();
    }
}
[HarmonyPatch(typeof(ReflectionHelper), nameof(ReflectionHelper.GetSubtypesFromAssembly))]
internal static class SubtypeBridge
{
    private static void Postfix(Assembly assembly, Type parentType, ref IEnumerable<Type> __result)
    {
        if (assembly == typeof(Bootstrap).Assembly)
            __result = __result.Concat(Bootstrap.RuntimeTypes.Where(t => !t.IsAbstract && !t.IsInterface && parentType.IsAssignableFrom(t))).Distinct();
    }
}
