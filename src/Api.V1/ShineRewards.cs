using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace ShoujoKageki.Core.V1;

/// <summary>Optional card-specific eligibility after the recipient's pool is selected.</summary>
public interface IShineRewardEligibility
{
    bool CanDropAsShineReward(Player recipient);
}
/// <summary>A character may supply the shared fallback reward pool.</summary>
public interface IShineRewardFallback
{
    CardPoolModel ShineRewardFallback { get; }
}
public interface IShineRewards
{
    IReadOnlyList<CardModel> GetCandidates(Player recipient, CardModel? except = null);
    CardModel Roll(Player recipient, Rng rng, CardModel? except = null);
    CardModel Reproduction { get; }
    MegaCrit.Sts2.Core.Rewards.Reward Create(CardModel card, Player recipient);
    Task Add(Player recipient, CardModel? except = null);
}
public static class ShineRewards
{
    private static IShineRewards Service => CoreApi.Get<IShineRewards>();
    public static IReadOnlyList<CardModel> GetCandidates(Player recipient, CardModel? except = null) => Service.GetCandidates(recipient, except);
    public static CardModel Roll(Player recipient, Rng rng, CardModel? except = null) => Service.Roll(recipient, rng, except);
    public static CardModel Reproduction => Service.Reproduction;
    public static MegaCrit.Sts2.Core.Rewards.Reward Create(CardModel card, Player recipient) => Service.Create(card, recipient);
    public static Task Add(Player recipient, CardModel? except = null) => Service.Add(recipient, except);
}
