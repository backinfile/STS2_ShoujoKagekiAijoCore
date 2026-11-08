using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
namespace ShoujoKageki.Core.V1;
public sealed record CardMovement(CardModel Card, PileType OldPile, PileType NewPile)
{
    public AbstractModel? Source { get; init; }
}
public interface ICardMovementEvents
{
    event Action<CardMovement>? Moved;
}

/// <summary>Awaited rule handlers run in registration order after each native movement hook.</summary>
public interface ICardMovementPipeline
{
    IDisposable Register(Func<CardMovement, Task> handler);
}
