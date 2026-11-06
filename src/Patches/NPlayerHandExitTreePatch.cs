using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace ShoujoKageki.Core.Runtime.Patches;

[HarmonyPatch(typeof(NPlayerHand), nameof(NPlayerHand._ExitTree))]
public static class NPlayerHandExitTreePatch
{
    private static readonly ConditionalWeakTable<NPlayerHand, object> ExitingHands = new();

    public static bool IsExiting(NPlayerHand? hand)
    {
        return hand != null && ExitingHands.TryGetValue(hand, out _);
    }

    private static void Prefix(NPlayerHand __instance)
    {
        ExitingHands.Remove(__instance);
        ExitingHands.Add(__instance, new object());
    }
}

[HarmonyPatch(typeof(NPlayerHand), "AfterCardsSelected")]
public static class NPlayerHandAfterCardsSelectedExitPatch
{
    private static bool Prefix(NPlayerHand __instance)
    {
        if (!NPlayerHandExitTreePatch.IsExiting(__instance))
        {
            return true;
        }

        CoreLog.Logger.Info("[NPlayerHandExitTreePatch] Skipped select-mode UI cleanup while hand node exited tree.");
        return false;
    }
}

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.EnableControllerNavigation))]
public static class NCombatRoomEnableControllerNavigationExitPatch
{
    private static bool Prefix(NCombatRoom __instance)
    {
        if (!GodotObject.IsInstanceValid(__instance) || !__instance.IsInsideTree())
        {
            return false;
        }

        NPlayerHand? hand = __instance.Ui?.Hand;
        if (!GodotObject.IsInstanceValid(hand) || !hand.IsInsideTree() || NPlayerHandExitTreePatch.IsExiting(hand))
        {
            CoreLog.Logger.Info("[NPlayerHandExitTreePatch] Skipped controller navigation refresh while hand node exited tree.");
            return false;
        }

        return true;
    }
}
