using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;
namespace ShoujoKageki.Core.Runtime.Modules.Shine.Saves;

/// <summary>Read-only adapter for files written before registered Core player saves.</summary>
[HarmonyPatch]
internal static class RunSaveManager_Patches
{
    [HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.ReadFile)), HarmonyPostfix]
    private static void ReadFile(string path, string? __result)
    {
        if (path.Contains(RunSaveManager.runSaveFileName) || path.Contains(RunSaveManager.multiplayerRunSaveFileName))
            KarenRunSaveReadScope.Record(__result);
    }
    [HarmonyPatch]
    private static class NativeRead
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(RunSaveManager), nameof(RunSaveManager.LoadRunSave));
            yield return AccessTools.Method(typeof(RunSaveManager), nameof(RunSaveManager.LoadMultiplayerRunSave));
        }
        private static void Prefix(out KarenRunSaveReadScope __state) => __state = new();
        private static void Postfix(ReadSaveResult<SerializableRun> __result, KarenRunSaveReadScope __state) => __state.Bind(__result);
        private static Exception? Finalizer(Exception? __exception, KarenRunSaveReadScope? __state)
        { __state?.Dispose(); return __exception; }
    }
}
