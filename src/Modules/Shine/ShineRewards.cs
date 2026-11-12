using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Unlocks;
using ShoujoKageki.Core.V1;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

public sealed class ShineRewardService : IShineRewards, ICoreModule
{
    public string Id => "shine-rewards";
    public int Order => -490;
    public CardModel Reproduction => ModelDb.Card<CoreShineReproduce>();
    public void Initialize(CoreContext context) => context.Services.Add<IShineRewards>(this);

    public IReadOnlyList<CardModel> GetCandidates(Player recipient, CardModel? except = null)
    {
        CardModel[] FromPool(CardPoolModel? pool) => pool == null ? [] : pool
            .GetUnlockedCards(UnlockState.all, recipient.RunState.CardMultiplayerConstraint)
            .Where(card => V1.Shine.IsShineCard(card) && card.Rarity != CardRarity.Basic)
            .Where(card => card.Id != except?.Id && card.Id != Reproduction.Id)
            .Where(card => card is not IShineRewardEligibility policy || policy.CanDropAsShineReward(recipient))
            .DistinctBy(card => card.Id).ToArray();

        var cards = CoreDiscovery.IsShoujoCharacter(recipient.Character) ? FromPool(recipient.Character.CardPool) : [];
        if (cards.Length == 0)
        {
            var fallback = RuntimeEntry.Context.Discovery.Models.FirstOrDefault(model =>
                model.ModId == "ShoujoKagekiAijoKaren" && model.Kind == "character");
            cards = FromPool(fallback == null ? null : ModelDb.GetById<CharacterModel>(ModelDb.GetId(fallback.ModelType)).CardPool);
        }
        return Reproduction.Id == except?.Id ? cards : [..cards, Reproduction];
    }

    public CardModel Roll(Player recipient, Rng rng, CardModel? except = null)
    {
        var candidates = GetCandidates(recipient, except);
        if (candidates.Count == 0) throw new InvalidOperationException("No eligible Shine card reward.");
        var roll = rng.NextInt(100);
        var bucket = candidates.Where(card => roll < 42 ? card.Rarity == CardRarity.Common :
            roll < 84 ? card.Rarity == CardRarity.Uncommon :
            roll < 99 ? card.Rarity == CardRarity.Rare : card.Id == Reproduction.Id).ToArray();
        return bucket.Length == 0 ? candidates[rng.NextInt(candidates.Count)] : bucket[rng.NextInt(bucket.Length)];
    }

    public MegaCrit.Sts2.Core.Rewards.Reward Create(CardModel card, Player recipient) => new CoreShineCardReward(card, recipient);

    public async Task Add(Player recipient, CardModel? except = null)
    {
        var card = recipient.RunState.CreateCard(Roll(recipient, recipient.PlayerRng.Rewards, except), recipient);
        var reward = Create(card, recipient);
        if (recipient.RunState.CurrentRoom is CombatRoom room)
        {
            room.AddExtraReward(recipient, reward);
            await PowerCmd.Apply<CoreShineCardRewardPower>(new ThrowingPlayerChoiceContext(), recipient.Creature, 1m, recipient.Creature, null);
        }
        else await RewardsCmd.OfferCustom(recipient, [reward]);
    }
}
