using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

[HarmonyPatch(typeof(NCard), "GetTitleLabelOutlineColor")]
internal static class ReproductionTitlePatch
{
    private static void Postfix(NCard __instance, ref Color __result)
    {
        if (__instance.Model is CoreShineReproduce) __result = StsColors.cardTitleOutlineRare;
    }
}
[HarmonyPatch(typeof(NCard), nameof(NCard.ActivateRewardScreenGlow))]
internal static class ReproductionGlowPatch
{
    private static void Postfix(NCard __instance)
    {
        if (__instance.Model is CoreShineReproduce) __instance.CardHighlight.Modulate = NCardHighlight.gold;
    }
}
