using System.Reflection;
using System.Runtime.Loader;
using Godot;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace ShoujoKageki.Core.Bootstrap;

[ModInitializer(nameof(Initialize))]
public static class Bootstrap
{
    public const int BootstrapProtocol = 1;
    public const string ModId = "ShoujoKagekiAijoCore";
    internal const string CoordinationKey = "ShoujoKagekiAijoCore.BootstrapProtocol1";
    private static readonly MegaCrit.Sts2.Core.Logging.Logger Log = new(ModId, LogType.Generic);
    internal static Assembly? RuntimeAssembly;
    private static Mod? _logicalMod;

    public static void Initialize()
    {
        lock (AppDomain.CurrentDomain)
        {
            var prior = AppDomain.CurrentDomain.GetData(CoordinationKey) as string;
            if (prior == "ready") return;
            if (prior != null) throw new InvalidOperationException($"Core bootstrap is {prior}. Restart after correcting the reported error.");
            AppDomain.CurrentDomain.SetData(CoordinationKey, "initializing");
            new Harmony(ModId + ".Bootstrap").PatchAll(typeof(Bootstrap).Assembly);
            try
            {
                InitializeOnce();
                AppDomain.CurrentDomain.SetData(CoordinationKey, "ready");
            }
            catch (Exception error)
            {
                AppDomain.CurrentDomain.SetData(CoordinationKey, "failed");
                Log.Error($"[Core bootstrap] {error}");
                foreach (var mod in ModManager.Mods.Where(x => x.manifest?.id == ModId || DependsOnCore(x)))
                    if (mod.state is ModLoadState.None or ModLoadState.Loaded) mod.state = ModLoadState.Failed;
                // BaseLib owns the pending startup-error dialog.
                var config = Type.GetType("BaseLib.Config.ModConfig+ModConfigLogger, BaseLib");
                (config?.GetProperty("PendingUserMessages")?.GetValue(null) as IList<string>)?.Add($"Core 加载失败。{error.GetBaseException().Message}");
                throw;
            }
        }
    }

    private static bool DependsOnCore(Mod mod) => mod.manifest?.dependencies?.Any(d => d.id == ModId) == true;
    private static bool Eligible(Mod mod) => mod.state is ModLoadState.None or ModLoadState.DisabledDuplicate;

    private static void InitializeOnce()
    {
        var entries = ModManager.Mods.Where(x => x.manifest?.id == ModId && Eligible(x)).ToArray();
        var location = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location);
        _logicalMod = entries.FirstOrDefault(x => !string.IsNullOrEmpty(location)
            && Path.TrimEndingDirectorySeparator(Path.GetFullPath(x.path)).Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)), StringComparison.OrdinalIgnoreCase))
            // Android may report a canonical assembly path instead of the mod directory alias.
            ?? (entries.Length == 1 ? entries[0] : throw new InvalidOperationException("Cannot identify the active Core mod entry."));
        var baseLib = ModManager.Mods.FirstOrDefault(x => x.manifest?.id == "BaseLib" && x.state == ModLoadState.Loaded)
            ?? throw new InvalidOperationException("缺少已加载的 BaseLib，请按 BaseLib → Core → 角色 Mod 排列，并安装 BaseLib 3.4.7。");
        if (AccessTools.Field(typeof(ReflectionHelper), "_modTypes").GetValue(null) != null)
            throw new InvalidOperationException("Core 在模型反射缓存初始化后加载，请按 BaseLib → Core → 角色 Mod 排列。");
        var requirements = new List<CoreRequirements>();
        var minimumRelease = "1.0.0";
        foreach (var role in ModManager.Mods.Where(x => x.state == ModLoadState.None && DependsOnCore(x)))
        {
            var dependency = role.manifest!.dependencies!.First(x => x.id == ModId);
            if (dependency.minVersion != null && SemVersion.Parse(dependency.minVersion).CompareTo(SemVersion.Parse(minimumRelease)) > 0) minimumRelease = dependency.minVersion;
            var path = Path.Combine(role.path, "core.requirements");
            if (!File.Exists(path)) throw new InvalidDataException($"{role.manifest.id} 缺少 core.requirements，请重新安装完整角色包。");
            var requirement = BundleSelection.Read<CoreRequirements>(path);
            if (requirement.ModId != role.manifest.id) throw new InvalidDataException($"{role.manifest.id} 的 Core 需求身份不一致。");
            requirements.Add(requirement);
        }
        var candidates = new List<BundleCandidate>();
        foreach (var mod in ModManager.Mods.Where(x => x.manifest?.id == ModId && Eligible(x)))
        {
            try { candidates.Add(BundleSelection.Validate(mod.path, mod.modSource + ":" + Path.GetFullPath(mod.path))); }
            catch (Exception error) { Log.Warn($"[Core candidate rejected] {mod.modSource} {error.Message}"); }
        }
        var gameVersion = ReleaseInfoManager.Instance!.SemVer!.ToString();
        var selected = BundleSelection.Select(candidates, requirements, gameVersion, baseLib.manifest!.version ?? "0.0.0", minimumRelease);
        var target = selected.Bundle.Targets.Single(x => SemVersion.Parse(gameVersion).CompareTo(SemVersion.Parse(x.Value.MinimumGameVersion)) >= 0
            && SemVersion.Parse(gameVersion).CompareTo(SemVersion.Parse(x.Value.MaximumGameVersion)) <= 0);
        _logicalMod.manifest!.version = selected.Bundle.ReleaseVersion;
        _logicalMod.version = SemanticVersion.TryFromString(selected.Bundle.ReleaseVersion, out var version) ? version : throw new InvalidDataException("Invalid release version.");
        foreach (var duplicate in ModManager.Mods.Where(x => x != _logicalMod && x.manifest?.id == ModId && Eligible(x)))
            duplicate.state = ModLoadState.DisabledDuplicate;
        // Replace the lookup list. The game's active foreach keeps its original list iterator.
        var reordered = new List<Mod> { _logicalMod };
        reordered.AddRange(ModManager.Mods.Where(x => x != _logicalMod));
        AccessTools.Field(typeof(ModManager), "_mods").SetValue(null, reordered);
        var context = AssemblyLoadContext.GetLoadContext(typeof(Bootstrap).Assembly) ?? AssemblyLoadContext.Default;
        var api = LoadExact(context, BundleSelection.ResolvePath(selected.Root, selected.Bundle.ApiAssembly));
        RuntimeAssembly = LoadExact(context, BundleSelection.ResolvePath(selected.Root, target.Value.Assembly));
        var associate = typeof(ModManager).GetMethod("AssociateAssemblyWithMod", [typeof(string), typeof(Assembly)]);
        associate?.Invoke(null, [ModId, api]);
        associate?.Invoke(null, [ModId, RuntimeAssembly]);
        if (!ProjectSettings.LoadResourcePack(BundleSelection.ResolvePath(selected.Root, selected.Bundle.ResourcePack), true))
            throw new InvalidDataException("Core 公共资源包挂载失败。");
        ScriptManagerBridge.LookupScriptsInAssembly(RuntimeAssembly);
        var entry = RuntimeAssembly.GetType("ShoujoKageki.Core.Runtime.RuntimeEntry", true)!;
        entry.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)!.Invoke(null,
            [selected.Bundle.ReleaseVersion, gameVersion, selected.Fingerprint]);
        Log.Info($"[Core bootstrap] Selected {selected.Bundle.ReleaseVersion}/{target.Key}, {candidates.Count} complete candidates, fingerprint {selected.Fingerprint}.");
    }

    private static Assembly LoadExact(AssemblyLoadContext context, string path)
    {
        var identity = AssemblyName.GetAssemblyName(path);
        var existing = context.Assemblies.FirstOrDefault(x => x.GetName().Name == identity.Name);
        if (existing != null)
        {
            if (!Path.GetFullPath(existing.Location).Equals(path, StringComparison.OrdinalIgnoreCase))
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
        if (Bootstrap.RuntimeAssembly != null) __result = __result.Concat(Bootstrap.RuntimeAssembly.GetTypes()).Distinct().ToArray();
    }
}
[HarmonyPatch(typeof(ReflectionHelper), nameof(ReflectionHelper.GetSubtypesFromAssembly))]
internal static class SubtypeBridge
{
    private static void Postfix(Assembly assembly, Type parentType, ref IEnumerable<Type> __result)
    {
        if (assembly == typeof(Bootstrap).Assembly && Bootstrap.RuntimeAssembly != null)
            __result = __result.Concat(Bootstrap.RuntimeAssembly.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface && parentType.IsAssignableFrom(t))).Distinct();
    }
}
[HarmonyPatch(typeof(ModManager), "TryLoadMod")]
internal static class BootstrapFailureGuard
{
    private static void Postfix(Mod mod)
    {
        if (mod.manifest?.id == Bootstrap.ModId && AppDomain.CurrentDomain.GetData(Bootstrap.CoordinationKey) as string == "failed") mod.state = ModLoadState.Failed;
    }
}
