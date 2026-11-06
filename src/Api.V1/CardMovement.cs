using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
namespace ShoujoKageki.Core.V1;
public sealed record CardMovement(CardModel Card, PileType OldPile, PileType NewPile);
public interface ICardMovementEvents
{
    event Action<CardMovement>? Moved;
}
