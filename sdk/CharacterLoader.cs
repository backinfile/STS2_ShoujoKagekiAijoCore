using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace ShoujoKageki.Core.Loader;

// Compiled into each character loader; no runtime dependency on another loader.
internal static class CharacterLoader
{
    private static Assembly? _implementation;
    private static Type[] _types = [];
    private static bool _stable;

    internal static void Initialize(string modId, string entryType,
        Func<SemanticVersion, string> selectVariant, Logger log)
    {
        if (_implementation != null) return;
        var version = ReleaseInfoManager.Instance.SemVer
            ?? throw new InvalidOperationException("Game version is unavailable.");
        string variant = selectVariant(version);
        _stable = variant == "Stable";
        var loader = typeof(CharacterLoader).Assembly;
        string path = Path.Combine(Path.GetDirectoryName(loader.Location)!, "lib", $"{modId}.{variant}.dll");
        var context = AssemblyLoadContext.GetLoadContext(loader) ?? AssemblyLoadContext.Default;
        _implementation = context.LoadFromAssemblyPath(Path.GetFullPath(path));
        _types = _implementation.GetTypes();
        // The stable game exposes only the root assembly. Discovery bridges also
        // provide a fallback if the beta association hook fails.
        try
        {
            typeof(ModManager).GetMethod("AssociateAssemblyWithMod", BindingFlags.Public | BindingFlags.Static,
                null, [typeof(string), typeof(Assembly)], null)?.Invoke(null, [modId, _implementation]);
        }
        catch (TargetInvocationException error)
        {
            log.Warn($"[{modId}Loader] Assembly association failed; using discovery bridges: {error.InnerException?.Message ?? error.Message}");
        }
        new Harmony(modId + ".Loader").PatchAll(loader);
        _implementation.GetType(entryType, throwOnError: true)!
            .GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        log.Info($"[{modId}Loader] Loaded {variant} implementation for game {version}.");
    }

    [HarmonyPatch(typeof(ReflectionHelper), nameof(ReflectionHelper.ModTypes), MethodType.Getter)]
    private static class ModelTypes
    {
        private static void Postfix(ref Type[] __result)
        {
            var seen = new HashSet<Type>(__result);
            __result = [.. __result, .. _types.Where(seen.Add)];
        }
    }

    [HarmonyPatch(typeof(ReflectionHelper), nameof(ReflectionHelper.GetSubtypesFromAssembly))]
    private static class AssemblySubtypes
    {
        private static void Postfix(Assembly assembly, Type parentType, ref IEnumerable<Type> __result)
        {
            if (!_stable || assembly != typeof(CharacterLoader).Assembly) return;
            __result = __result.Concat(_types
                .Where(type => !type.IsAbstract && !type.IsInterface && parentType.IsAssignableFrom(type)));
        }
    }

    [HarmonyPatch(typeof(ModelIdSerializationCache), nameof(ModelIdSerializationCache.Init))]
    private static class StableModelTypes
    {
        private static bool Prepare() => _stable;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var scan = AccessTools.Method(typeof(ReflectionHelper), nameof(ReflectionHelper.GetSubtypesFromAssembly));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(scan))
                    instruction.operand = AccessTools.Method(typeof(StableModelTypes), nameof(GetSubtypes));
                yield return instruction;
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IEnumerable<Type> GetSubtypes(Assembly assembly, Type parentType) =>
            ReflectionHelper.GetSubtypesFromAssembly(assembly, parentType);
    }
}
