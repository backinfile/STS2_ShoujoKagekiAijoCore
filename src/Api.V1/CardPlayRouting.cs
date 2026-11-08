using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
namespace ShoujoKageki.Core.V1;
public delegate Task CardPlayMiddleware(CardModel card, PlayerChoiceContext context, CardPlay play, Func<CardPlay, Task> next);
public interface ICardPlayRouting
{
    IDisposable Register(CardPlayMiddleware middleware, Func<CardModel, PileType?> destination);
}
