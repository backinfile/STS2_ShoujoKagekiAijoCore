using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using ShoujoKageki.Core.V1;
using ShoujoKageki.Core.Runtime.Modules.Configuration;

namespace ShoujoKageki.Core.Runtime;

public static class RuntimeEntry
{
    private static readonly List<ICoreModule> Modules = [];
    private static bool _shutdown;
    public static CoreContext Context { get; private set; } = null!;
    public static string Fingerprint { get; private set; } = "";
    public static void Initialize(string releaseVersion, string gameVersion, string fingerprint)
    {
        if (Context != null) return;
        Fingerprint = fingerprint;
        var services = new CoreServices();
        services.Add<ICoreDiagnostics>(new CoreDiagnostics());
        services.Add<ICoreDiscovery>(new CoreDiscovery());
        services.Add<ICoreConfiguration>(new CoreConfiguration());
        Context = new CoreContext(services, releaseVersion, gameVersion, Path.Combine(OS.GetUserDataDir(), CoreApi.ModId));
        Directory.CreateDirectory(Context.DataDirectory);
        CoreApi.Bind(services);
        CoreConfiguration.Instance.Initialize();
        Context.Discovery.Refresh();
        var modules = typeof(RuntimeEntry).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ICoreModule).IsAssignableFrom(t))
            .Select(t => (ICoreModule)Activator.CreateInstance(t)!).OrderBy(m => m.Order).ThenBy(m => m.Id, StringComparer.Ordinal).ToArray();
        if (modules.GroupBy(x => x.Id).Any(x => x.Count() > 1)) throw new InvalidOperationException("Duplicate Core module IDs.");
        foreach (var module in modules)
        {
            module.Initialize(Context);
            Modules.Add(module);
            Context.Diagnostics.Report(DiagnosticSeverity.Info, module.Id, "module.initialized", $"Initialized {module.Id}.");
        }
        new Harmony(CoreApi.ModId + ".Runtime").PatchAll(typeof(RuntimeEntry).Assembly);
    }
    internal static void ModelsReady()
    {
        Context.Discovery.Refresh();
        Context.NotifyModelsReady();
    }
    internal static void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        var actions = Modules.AsEnumerable().Reverse().OfType<IDisposable>()
            .Select<IDisposable, Action>(module => module.Dispose).Prepend(Context.NotifyShutdown).ToArray();
        Modules.Clear();
        if (Cleanup.Capture(actions) is { } error)
            Context.Diagnostics.Report(DiagnosticSeverity.Error, "runtime", "shutdown.failed", "Core cleanup failed.", error);
    }
}
[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.InitIds))]
internal static class ModelLifecyclePatch
{
    private static void Postfix() => RuntimeEntry.ModelsReady();
}
[HarmonyPatch(typeof(NGame), nameof(NGame.Quit))]
internal static class ShutdownLifecyclePatch
{
    private static void Prefix() => RuntimeEntry.Shutdown();
}
