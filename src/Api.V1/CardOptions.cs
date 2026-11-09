using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.V1;

public interface ICardOptions
{
    CardModel Create(CardModel canonical, Player owner, bool upgrade = false);
    Task<CardModel?> Choose(PlayerChoiceContext context, IReadOnlyList<CardModel> options,
        Player player, bool canSkip, LocString? title);
}
public static class CardOptions
{
    public static CardModel Create(CardModel canonical, Player owner, bool upgrade = false)
        => CoreApi.Get<ICardOptions>().Create(canonical, owner, upgrade);
    public static Task<CardModel?> Choose(PlayerChoiceContext context, IReadOnlyList<CardModel> options,
        Player player, bool canSkip = false, LocString? title = null)
        => CoreApi.Get<ICardOptions>().Choose(context, options, player, canSkip, title);
}
