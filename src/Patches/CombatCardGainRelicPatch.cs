using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using ShoujoKageki.Core.Runtime.Modules.Shine;

namespace ShoujoKageki.Core.Runtime.Patches;

[HarmonyPatch]
internal static class CombatCardGainRelicPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(BookOfFiveRings), nameof(BookOfFiveRings.AfterCardChangedPiles));
        yield return AccessTools.Method(typeof(LuckyFysh), nameof(LuckyFysh.AfterCardChangedPiles));
    }
    private static bool Prefix(RelicModel __instance, ref Task __result)
    {
        var player = __instance.Owner;
        if (player == null || CombatManager.Instance?.IsInProgress != true) return true;
        var mode = ShineArchive.GetRules(player).RelicMode;
        var restrict = mode == CombatCardGainRelicMode.Enabled
            || mode == CombatCardGainRelicMode.ShoujoOnly && CoreDiscovery.IsShoujoCharacter(player.Character);
        if (!restrict) return true;
        __result = Task.CompletedTask;
        return false;
    }
}
[HarmonyPatch(typeof(NRun), nameof(NRun._Ready))]
internal static class RunRuleSnapshotPatch
{
    private static void Prefix()
    {
        if (RunManager.Instance.DebugOnlyGetState() is not { } state) return;
        foreach (var player in state.Players) _ = ShineArchive.GetRules(player);
    }
}
