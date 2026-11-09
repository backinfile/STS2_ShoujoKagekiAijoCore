using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
namespace ShoujoKageki.Core.V1;
public delegate Task CardPlayMiddleware(CardModel card, PlayerChoiceContext context, CardPlay play, Func<CardPlay, Task> next);
public interface ICardPlayRouting
{
    IDisposable Register(CardPlayMiddleware middleware, Func<CardModel, PileType?> destination);
}

/// <summary>A continuation may be retained by a scheduler and resumed with its current choice context.</summary>
public delegate Task CardEffectContinuation(PlayerChoiceContext context, CardPlay play);
public delegate Task CardEffectMiddleware(CardModel card, PlayerChoiceContext context, CardPlay play,
    CardEffectContinuation next);
public interface ICardEffectRouting
{
    IDisposable Register(CardEffectMiddleware middleware, Func<CardModel, PileType?> destination);
}
