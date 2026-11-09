using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.V1;

public sealed record CardPickRequest
{
    public required LocString Prompt { get; init; }
    public int Minimum { get; init; } = 1;
    public int Maximum { get; init; } = 1;
    public AbstractModel? Source { get; init; }
    public bool PreferHand { get; init; } = true;
}
public interface ICardPicking
{
    Task<IReadOnlyList<CardModel>> Pick(PlayerChoiceContext context, Player player,
        IEnumerable<CardModel> candidates, CardPickRequest request);
}
public static class CardPicking
{
    public static Task<IReadOnlyList<CardModel>> Pick(PlayerChoiceContext context, Player player,
        IEnumerable<CardModel> candidates, CardPickRequest request)
        => CoreApi.Get<ICardPicking>().Pick(context, player, candidates, request);
}
