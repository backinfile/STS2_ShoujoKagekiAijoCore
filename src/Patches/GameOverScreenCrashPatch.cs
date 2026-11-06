using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Runs;

namespace ShoujoKageki.Core.Runtime.Patches;

/// <summary>
/// 修复 STS2 本体在放弃游戏（Abandon）时，NGameOverScreen 因 _runState.CurrentRoom 为 null
/// 导致 MoveCreaturesToDifferentLayerAndDisableUi 抛出 NullReferenceException 的崩溃。
/// 该方法仅负责视觉表现（移动角色节点、禁用 UI），失败不影响游戏状态。
/// TODO 不一定需要,之后看看是否移除
/// </summary>
[HarmonyPatch(typeof(NGameOverScreen), "MoveCreaturesToDifferentLayerAndDisableUi")]
public class GameOverScreenCrashPatch
{
    private static bool Prefix(NGameOverScreen __instance)
    {
        RunState runState = Traverse.Create(__instance).Field<RunState>("_runState").Value;
        if (runState?.CurrentRoom == null)
        {
            CoreLog.Logger.Info("[GameOverScreenCrashPatch] _runState.CurrentRoom 为 null，跳过 MoveCreaturesToDifferentLayerAndDisableUi 以避免 NRE。");
            return false;
        }
        return true;
    }
}
