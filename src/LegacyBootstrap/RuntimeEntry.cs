using System.Reflection;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Modding;

namespace ShoujoKageki.Core.Runtime;

// The 1.x bootstrap resolves this type by name before it runs any gameplay code.
public static class RuntimeEntry
{
    public static void Initialize(string releaseVersion, string gameVersion, string fingerprint)
    {
        var current = ModManager.Mods.FirstOrDefault(mod =>
            mod.manifest?.id == "ShoujoKagekiCore" && mod.state == ModLoadState.None)
            ?? throw new InvalidOperationException("旧版 Core 的兼容入口需要启用新版 Core。");
        var context = AssemblyLoadContext.GetLoadContext(typeof(RuntimeEntry).Assembly) ?? AssemblyLoadContext.Default;
        var path = Path.GetFullPath(Path.Combine(current.path, "ShoujoKagekiCore.dll"));
        var assembly = context.LoadFromAssemblyPath(path);
        var type = assembly.GetType("ShoujoKageki.Core.Bootstrap.Bootstrap", throwOnError: true)!;
        var start = type.GetMethod("InitializeFromLegacy", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("请使用支持 1.x 共存的新版 Core。");
        start.Invoke(null, null);
    }
}
