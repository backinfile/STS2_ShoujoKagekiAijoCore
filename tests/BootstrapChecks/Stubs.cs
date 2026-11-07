using System.Reflection;

namespace Godot {
    public static class ProjectSettings { public static bool LoadResourcePack(string path, bool replace) => true; }
}
namespace Godot.Bridge {
    public static class ScriptManagerBridge { public static void LookupScriptsInAssembly(Assembly assembly) { } }
}
namespace HarmonyLib {
    public enum MethodType { Getter }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute {
        public HarmonyPatch(Type type, string method) { }
        public HarmonyPatch(Type type, string method, MethodType kind) { }
    }
    public sealed class Harmony(string id) { public void PatchAll(Assembly assembly) { } }
    public static class AccessTools {
        public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
    }
}
namespace MegaCrit.Sts2.Core.Logging {
    public enum LogType { Generic }
    public sealed class Logger(string id, LogType kind) {
        public void Error(string message) { }
        public void Warn(string message) { }
        public void Info(string message) { }
    }
}
namespace MegaCrit.Sts2.Core.Debug {
    public sealed class ReleaseInfoManager {
        public static ReleaseInfoManager Instance { get; } = new();
        public string SemVer { get; } = "0.107.1";
    }
}
namespace MegaCrit.Sts2.Core.Helpers {
    public static class ReflectionHelper {
        public static Type[]? _modTypes;
        public static Type[] ModTypes => _modTypes ?? [];
        public static IEnumerable<Type> GetSubtypesFromAssembly(Assembly assembly, Type parent) => [];
    }
    public sealed class SemanticVersion {
        public static bool TryFromString(string text, out SemanticVersion version) { version = new(); return true; }
    }
}
namespace MegaCrit.Sts2.Core.Modding {
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ModInitializerAttribute(string method) : Attribute { }
    public enum ModLoadState { None, DisabledDuplicate, Loaded, Failed, Disabled }
    public sealed class ModDependency { public string id = ""; public string? minVersion; }
    public sealed class ModManifest { public string id = ""; public string? version; public ModDependency[]? dependencies; }
    public sealed class Mod {
        public string path = "";
        public ModManifest? manifest;
        public ModLoadState state;
        public string modSource = "Local";
        public MegaCrit.Sts2.Core.Helpers.SemanticVersion? version;
    }
    public static class ModManager {
        public static List<Mod> _mods = [];
        public static IReadOnlyList<Mod> Mods => _mods;
    }
}
