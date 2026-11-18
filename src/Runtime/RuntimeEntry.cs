using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using ShoujoKageki.Core.V1;
using ShoujoKageki.Core.Runtime.Modules.Configuration;
using ShoujoKageki.Core.Runtime.Modules.Execution;
using ShoujoKageki.Core.Runtime.Modules.Shine.Saves;

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
        if (CoreApi.IsReady) throw new InvalidOperationException("Core API was bound before runtime initialization.");
        var stage = "module discovery";
        var harmony = new Harmony(CoreApi.ModId + ".Runtime");
        var configuration = new CoreConfiguration();
        try
        {
            var modules = typeof(RuntimeEntry).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ICoreModule).IsAssignableFrom(t))
                .Select(t => (ICoreModule)Activator.CreateInstance(t)!).OrderBy(m => m.Order).ThenBy(m => m.Id, StringComparer.Ordinal).ToArray();
            if (modules.GroupBy(x => x.Id).Any(x => x.Count() > 1)) throw new InvalidOperationException("Duplicate Core module IDs.");

            stage = "configuration and services";
            var services = new CoreServices();
            services.Add<ICoreDiagnostics>(new CoreDiagnostics());
            services.Add<ICoreDiscovery>(new CoreDiscovery());
            services.Add<ICoreConfiguration>(configuration);
            Context = new CoreContext(services, releaseVersion, gameVersion, Path.Combine(OS.GetUserDataDir(), CoreApi.ModId));
            Fingerprint = fingerprint;
            Directory.CreateDirectory(Context.DataDirectory);
            configuration.Initialize();
            Context.Discovery.Refresh();

            foreach (var module in modules)
            {
                stage = $"module {module.Id}";
                // Include the module currently initializing so partial subscriptions can be detached.
                Modules.Add(module);
                module.Initialize(Context);
                Context.Diagnostics.Report(DiagnosticSeverity.Info, module.Id, "module.initialized", $"Initialized {module.Id}.");
            }
            stage = "runtime patches";
            harmony.PatchAll(typeof(RuntimeEntry).Assembly);

            // BaseLib's save registration is process-wide and has no public removal API.
            stage = "player save registration";
            CorePlayerSave.RegisterSave();
            CoreApi.Bind(services);
        }
        catch (Exception error)
        {
            var context = Context;
            var actions = ReleaseActions(context).Prepend(() => harmony.UnpatchAll(harmony.Id))
                .Append(ForwardedCardCall.RemovePatches).Append(configuration.ClearObservers);
            var failure = Cleanup.Capture(actions, error);
            Context = null!;
            Fingerprint = "";
            throw new InvalidOperationException($"Core runtime initialization failed at {stage}.", failure);
        }

        try { configuration.RegisterView(); }
        catch (Exception error)
        {
            Context.Diagnostics.Report(DiagnosticSeverity.Warning, "configuration", "view.unavailable",
                "Core settings UI registration failed.", error);
        }
    }

    internal static void ModelsReady()
    {
        try
        {
            Context.Discovery.Refresh();
            Context.NotifyModelsReady();
        }
        catch (Exception error)
        {
            Context.Diagnostics.Report(DiagnosticSeverity.Error, "runtime", "models.failed",
                $"Core {Context.ReleaseVersion}, game {Context.GameVersion}, fingerprint {Fingerprint}. Required model hooks failed.", error);
            throw;
        }
    }

    private static Action[] ReleaseActions(CoreContext? context)
    {
        var actions = Modules.AsEnumerable().Reverse().OfType<IDisposable>()
            .Select<IDisposable, Action>(module => module.Dispose);
        if (context != null) actions = actions.Prepend(context.NotifyShutdown);
        var result = actions.ToArray();
        Modules.Clear();
        return result;
    }

    internal static void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        if (Cleanup.Capture(ReleaseActions(Context)) is { } error)
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
