using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Logging;

namespace ShoujoKageki.Core.Runtime.Compatibility;

[HarmonyPatch]
internal static class CommonAssetPreload
{
    private static MethodBase TargetMethod() => AccessTools.Constructor(typeof(AssetLoadingSession),
        [typeof(string), typeof(IEnumerable<string>), typeof(ConcurrentDictionary<string, Resource>), typeof(AssetCache)]);

    private static void Prefix(string name, IEnumerable<string> paths, out List<Resource>? __state)
    {
        __state = null;
        if (name != "Common") return;
        var watch = Stopwatch.StartNew();
        var references = new Dictionary<string, int>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string path)
        {
            if (!visited.Add(path)) return;
            if (!path.EndsWith(".tscn", StringComparison.Ordinal) && !path.EndsWith(".tres", StringComparison.Ordinal)) return;
            // Atlas sprites can be virtual .tres paths; the text dependency reader requires a real file.
            if (!Godot.FileAccess.FileExists(path) && !Godot.FileAccess.FileExists(path + ".remap")) return;
            foreach (string dependency in ResourceLoader.GetDependencies(path))
            {
                string child = dependency.Split("::")[^1];
                if (!child.StartsWith("res://", StringComparison.Ordinal)) continue;
                references[child] = references.GetValueOrDefault(child) + 1;
                Visit(child);
            }
        }
        foreach (string path in paths) Visit(path);
        __state = [];
        // Avoid concurrent first loads of shared dependencies from the Common scene batch.
        foreach (string path in references.Where(pair => pair.Value > 1).Select(pair => pair.Key).Order(StringComparer.Ordinal))
            if (ResourceLoader.Load<Resource>(path) is { } resource) __state.Add(resource);
        Log.Info($"[Core Common preload] Prepared shared dependencies={__state.Count} in {watch.ElapsedMilliseconds}ms");
    }

    private static void Postfix(AssetLoadingSession __instance, List<Resource>? __state)
    {
        if (__state == null) return;
        // Keep roots until parents are cached. Drop our references without disposing shared game resources.
        _ = __instance.Task.ContinueWith(_ => __state.Clear(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static void Finalizer(Exception? __exception, List<Resource>? __state)
    {
        if (__exception != null) __state?.Clear();
    }
}
