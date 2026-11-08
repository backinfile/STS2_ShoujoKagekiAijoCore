using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.V1;

public interface ICardSelection
{
    Task<CardModel?> Choose(PlayerChoiceContext context, IReadOnlyList<CardModel> cards, Player player,
        bool canSkip, LocString? title);
}
public static class CardSelection
{
    public static Task<CardModel?> Choose(PlayerChoiceContext context, IReadOnlyList<CardModel> cards,
        Player player, bool canSkip = false, LocString? title = null)
        => CoreApi.Get<ICardSelection>().Choose(context, cards, player, canSkip, title);
}
