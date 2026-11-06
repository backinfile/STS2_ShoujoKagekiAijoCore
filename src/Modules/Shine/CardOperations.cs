using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

public sealed class CardOperations : ICoreModule, ICardReplayService, ICardMovementEvents
{
    private static CardOperations _instance = null!;
    private readonly SpireField<CardModel, int> _nextReplay = new(() => 0);
    public string Id => "card-operations";
    public int Order => -400;
    public event Action<CardMovement>? Moved;
    public void Initialize(CoreContext context)
    {
        _instance = this;
        context.Services.Add<ICardReplayService>(this);
        context.Services.Add<ICardMovementEvents>(this);
    }
    public int GetNext(CardModel card) => _nextReplay.Get(card);
    public void SetNext(CardModel card, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _nextReplay.Set(card, count);
    }
    [HarmonyPatch(typeof(Hook), nameof(Hook.ModifyCardPlayCount))]
    private static class ReplayPreviewPatch
    {
        private static void Postfix(CardModel card, ref int __result) => __result += _instance.GetNext(card);
    }
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterModifyingCardPlayCount))]
    private static class ReplayConsumePatch
    {
        private static void Prefix(CardModel card) => _instance.SetNext(card, 0);
    }
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardChangedPiles))]
    private static class MovementPatch
    {
        private static void Postfix(CardModel card, PileType oldPile, ref Task __result)
        {
            var next = card.Pile?.Type ?? PileType.None;
            __result = Notify(__result, new CardMovement(card, oldPile, next));
        }
        private static async Task Notify(Task task, CardMovement movement)
        {
            await task;
            _instance.Moved?.Invoke(movement);
        }
    }
}
