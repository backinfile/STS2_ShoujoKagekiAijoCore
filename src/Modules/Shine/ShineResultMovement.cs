using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add))]
#if STS2_BETA
[HarmonyPatch([typeof(IEnumerable<CardModel>), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool), typeof(bool)])]
#else
[HarmonyPatch([typeof(IEnumerable<CardModel>), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)])]
#endif
internal static class ShineResultMovement
{
    private static void Prefix(ref IEnumerable<CardModel> cards, CardPile newPile, out List<CardPileAddResult> __state)
    {
        __state = [];
        if (!newPile.IsCombatPile || newPile.Type == PileType.Play) return;
        var materialized = cards.ToArray();
        var deferred = materialized.Where(ShineService.ShouldDeferResultMove).ToHashSet();
        if (deferred.Count == 0) return;
        __state = deferred.Select(c => new CardPileAddResult { cardAdded = c, oldPile = c.Pile, success = false, modifyingModels = [] }).ToList();
        cards = materialized.Where(c => !deferred.Contains(c)).ToArray();
    }
    private static void Postfix(ref Task<IReadOnlyList<CardPileAddResult>> __result, List<CardPileAddResult> __state)
    {
        if (__state.Count > 0) __result = Merge(__result, __state);
    }
    private static async Task<IReadOnlyList<CardPileAddResult>> Merge(Task<IReadOnlyList<CardPileAddResult>> original, List<CardPileAddResult> deferred)
        => (await original).Concat(deferred).ToArray();
}
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Exhaust), [typeof(PlayerChoiceContext), typeof(CardModel), typeof(bool), typeof(bool)])]
internal static class ShineResultExhaust
{
    private static bool Prefix(CardModel card, ref Task __result)
    {
        if (!ShineService.ShouldDeferResultMove(card)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}
