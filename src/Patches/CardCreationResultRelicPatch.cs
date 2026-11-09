using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.Runtime.Patches;

[HarmonyPatch(typeof(CardCreationResult), nameof(CardCreationResult.ModifyCard), [typeof(CardModel), typeof(RelicModel)])]
internal static class CardCreationResultRelicPatch
{
    private static bool Prefix(CardCreationResult __instance, CardModel card, RelicModel modifyingRelic)
    {
        if (!__instance.ModifyingRelics.Any(relic => ReferenceEquals(relic, modifyingRelic))) return true;
        __instance.ModifyCard(card);
        return false;
    }
}
