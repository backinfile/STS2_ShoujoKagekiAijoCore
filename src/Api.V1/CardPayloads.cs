using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.V1;

/// <summary>An immutable downstream route snapshot that can be bound to an actual card copy.</summary>
public interface ICardEffectPayload
{
    CardEffectContinuation Bind(CardModel card);
}

public delegate Task CardPayloadMiddleware(CardModel card, PlayerChoiceContext context, CardPlay play,
    ICardEffectPayload next);

/// <summary>Extends effect routing without changing either published middleware contract.</summary>
public interface ICardPayloadRouting
{
    IDisposable Register(CardPayloadMiddleware middleware, Func<CardModel, PileType?> destination);
    /// <summary>The native OnPlay payload for effects deployed directly, outside the routing pipeline.</summary>
    ICardEffectPayload Native { get; }
}
