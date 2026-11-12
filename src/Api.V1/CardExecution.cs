using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.V1;

public enum CardPayment { Free, Paid }
public sealed record CardExecutionRequest
{
    public Creature? Target { get; init; }
    public CardPayment Payment { get; init; }
    /// <summary>Capture available energy for a free X-cost play. Paid plays use their actual payment.</summary>
    public bool CaptureAvailableX { get; init; } = true;
    public bool PreserveEnergyValue { get; init; } = true;
}
public interface ICardExecution
{
    Task<bool> Play(PlayerChoiceContext context, CardModel card, CardExecutionRequest request);
}
/// <summary>Stages actual cards in Play, executes them in order and restores unplayed cards.</summary>
public interface ICardStaging
{
    Task Play(IEnumerable<CardModel> cards, Func<CardModel, Task> play,
        Func<IReadOnlyList<CardModel>, Task> restore);
}
public static class CardExecution
{
    public static Task<bool> Play(PlayerChoiceContext context, CardModel card, CardExecutionRequest request)
        => CoreApi.Get<ICardExecution>().Play(context, card, request);
    public static Task PlayStaged(IEnumerable<CardModel> cards, Func<CardModel, Task> play,
        Func<IReadOnlyList<CardModel>, Task> restore)
        => CoreApi.Get<ICardStaging>().Play(cards, play, restore);
}
