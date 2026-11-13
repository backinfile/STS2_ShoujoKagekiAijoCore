using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using ShoujoKageki.Core.V1;
using ShoujoKagekiJunna.Combat;
using ShoujoKagekiJunna.Models;
using ShoujoKagekiJunna.UI;
using ShoujoKagekiAijoKaren.src.Core.Models.Cards.neutral;
using ShoujoKagekiAijoKaren.src.Core.Models.Powers;
using ShoujoKagekiAijoKaren.src.Core.PromisePileSystem;
using ShoujoKagekiAijoKaren.src.Core.Commands;

namespace CoreReviewIntegration;

[ModInitializer(nameof(Initialize))]
public static class ReviewStartup { public static void Initialize() { } }

public sealed class ReviewFixture : AbstractConsoleCmd
{
    private static string Report = "{}";
    public override string CmdName => "review_fixture";
    public override string Args => "run|report";
    public override string Description => "Directed custom-choice, option and knowledge regression.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? player, string[] args)
        => args.FirstOrDefault() == "report" ? new(true, Report)
            : player?.PlayerCombatState == null ? new(false, "Combat required")
            : new(Run(player), true, "running");

    private sealed class TrackingContext : PlayerChoiceContext
    {
        public int Begun, Ended;
#if STS2_BETA
        public override ulong? OwnerId => null;
        public override Task SignalPlayerChoiceBegun(Player player, PlayerChoiceOptions options)
#else
        public override Task SignalPlayerChoiceBegun(PlayerChoiceOptions options)
#endif
        { Begun++; return Task.CompletedTask; }
        public override Task SignalPlayerChoiceEnded() { Ended++; return Task.CompletedTask; }
    }
    private sealed class Selector : ICardSelector
    {
        public readonly List<string[]> Offered = [];
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            var cards = options.ToArray();
            Offered.Add(cards.Select(card => card.GetType().Name).ToArray());
            return Task.FromResult<IEnumerable<CardModel>>(cards.Take(maxSelect));
        }
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> cards,
            IReadOnlyList<CardRewardAlternative> alternatives) => throw new NotSupportedException();
    }
    private static async Task Run(Player player)
    {
        var rows = new List<object>();
        Report = JsonSerializer.Serialize(new { status = "running" });
        void Check(string name, bool passed)
        {
            rows.Add(new { name, passed });
            if (!passed) throw new InvalidOperationException(name);
        }
        async Task Clear()
        {
            foreach (var card in player.PlayerCombatState!.AllCards.ToArray())
            {
                ArrowCombat.Cancel(card);
                await CardPileCmd.RemoveFromCombat(card);
            }
        }
        try
        {
            var context = new TrackingContext();
            int invoked = 0;
            Check("custom local index uses the public service", await PlayerChoices.ChooseIndex(context, player, 3,
                () => { invoked++; return Task.FromResult(2); }) == 2 && invoked == 1);
            Check("custom choice balances its context", context.Begun == 1 && context.Ended == 1);
            Check("custom cancellation roundtrip", await PlayerChoices.ChooseIndex(context, player, 2, () => Task.FromResult(-1)) == -1);
            var failure = new InvalidOperationException("review-choice-failure");
            bool failed = false;
            try { await PlayerChoices.ChooseIndex(context, player, 2, () => throw failure); }
            catch (Exception actual) when (ReferenceEquals(actual, failure)) { failed = true; }
            Check("callback failure preserves identity and closes context", failed && context.Begun == 3 && context.Ended == 3);
            int before = context.Begun;
            Check("empty choice does not open a context", await PlayerChoices.ChooseIndex(context, player, 0,
                () => throw new Exception("empty callback")) == -1 && context.Begun == before);
            Check("reward-style choice needs no choice context", await PlayerChoices.ChooseIndex(null, player, 2, () => Task.FromResult(1)) == 1);
            Check("nested choices retain their own result", await PlayerChoices.ChooseIndex(context, player, 2,
                async () => await PlayerChoices.ChooseIndex(context, player, 2, () => Task.FromResult(1))) == 1
                && context.Begun == context.Ended);

            var first = new RelicReward(ModelDb.Relic<Anchor>().ToMutable(), player);
            var second = new RelicReward(ModelDb.Relic<BagOfPreparation>().ToMutable(), player);
            var flowerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("ShoujoKageki.Core.Runtime.Modules.Relics.KarenFlowerRewardSet"))
                .First(type => type != null)!;
            var flower = Activator.CreateInstance(flowerType, [new List<Reward> { first, second }, player])!;
            Check("flower grants only the synchronized child",
                await (Task<bool>)flowerType.GetMethod("OnSelect", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(flower, null)!
                && player.Relics.Any(relic => relic.Id == first.Relic!.Id)
                && !player.Relics.Any(relic => relic.Id == second.Relic!.Id));

            using var single = KnowledgeCombat.SingleCard();
            await Clear();
            var group = CardCatalog.Groups.First(group => group.GrantedCardIds.Any(id => group.GrantedCardCounts.GetValueOrDefault(id, 1) > 0)
                && group.MainCardCount == 1);
            var source = CardCatalog.Create(group.MainCardId, player);
            var related = CardCatalog.Create(group.GrantedCardIds.First(id => group.GrantedCardCounts.GetValueOrDefault(id, 1) > 0), player);
            var unrelated = CardCatalog.Create("JB01", player);
            await CardPileCmd.Add(source, PileType.Hand);
            await CardPileCmd.Add(related, PileType.Draw, CardPilePosition.Bottom);
            await CardPileCmd.Add(unrelated, PileType.Draw, CardPilePosition.Bottom);
            int nativePreview = KnowledgePreviewCards.Create(CardCatalog.Model(group.MainCardId)).Count;
            Check("static preview preserves membership quantities", nativePreview == group.Members.Sum(member => Math.Max(0, member.Count)));
            await KnowledgeCombat.DrawPartners(context, source);
            Check("shared knowledge query draws only actual directional partners", related.Pile?.Type == PileType.Hand && unrelated.Pile?.Type == PileType.Draw);

            async Task StagingFailure(bool hand, bool singleChoice = false)
            {
                await Clear();
                var combat = player.Creature.CombatState!;
                var sourceCard = CardCatalog.Create("JB01", player);
                await CardPileCmd.Add(sourceCard, PileType.Hand);
                var materials = new[] { combat.CreateCard<DefendIronclad>(player), combat.CreateCard<DefendIronclad>(player) };
                foreach (var material in materials)
                    if (hand) await CardPileCmd.Add(material, PileType.Hand);
                    else await PromisePileCmd.Add(player, material);
                int moved = 0;
                var expectedFailure = new InvalidOperationException("staging-failure");
                bool caught = false;
                using var selector = CardSelectCmd.UseSelector(new Selector());
                using (CoreApi.Get<ICardMovementPipeline>().Register(movement =>
                {
                    if (materials.Contains(movement.Card) && movement.NewPile == PileType.Play && ++moved == (singleChoice ? 1 : 2))
                        throw expectedFailure;
                    return Task.CompletedTask;
                }))
                {
                    try
                    {
                        if (hand) await HandArrow.Select(context, sourceCard, 2);
                        else if (singleChoice) await PromisePileCmd.SelectAndAutoPlayFromPromisePile(context, player);
                        else await (Task)typeof(PromisePileCmd).GetMethod("AutoPlayFromPromisePile",
                            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [context, player, 2])!;
                    }
                    catch (Exception actual) when (ReferenceEquals(actual, expectedFailure)) { caught = true; }
                }
                Check("partial staging restores " + (hand ? "hand arrows" : singleChoice ? "selected promise" : "promise batch"),
                    caught && materials.All(card => card.Pile == (hand ? player.PlayerCombatState!.Hand : PromisePileManager.EffectivePile(player))));
            }
            await StagingFailure(true);
            await StagingFailure(false);
            await StagingFailure(false, singleChoice: true);

            async Task Options<T>(bool upgrade, params string[] expected) where T : CardModel
            {
                await Clear();
                var combat = player.Creature.CombatState!;
                for (int index = 0; index < 3; index++)
                    await CardPileCmd.Add(combat.CreateCard<DefendIronclad>(player), PileType.Draw, CardPilePosition.Bottom);
                await CardPileCmd.Add(combat.CreateCard<DefendIronclad>(player), PileType.Hand);
                var card = combat.CreateCard<T>(player);
                if (upgrade) CardCmd.Upgrade(card);
                await CardPileCmd.Add(card, PileType.Hand);
                var selector = new Selector();
                using var selection = CardSelectCmd.UseSelector(selector);
                Check(typeof(T).Name + " executes", await CardExecution.Play(context, card, new CardExecutionRequest()));
                Check(typeof(T).Name + " offered order " + expected.Length,
                    selector.Offered.First().SequenceEqual(expected));
            }
            await Options<KarenWakeUp>(true, "KarenWakeUpDrawPileOption", "KarenWakeUpPromisePileOption", "KarenWakeUpDiscardPileOption");
            await Options<KarenNoHesitate>(false, "KarenNoHesitateDrawPileOption", "KarenNoHesitateHandOption", "KarenNoHesitateDiscardPileOption");
            await PromisePileManager.EnterMode(player, PromisePileMode.Void);
            await Options<KarenWakeUp>(true, "KarenWakeUpDrawPileOption", "KarenWakeUpDiscardPileOption");
            await Options<KarenNoHesitate>(false, "KarenNoHesitateHandOption", "KarenNoHesitateDiscardPileOption");
            Report = JsonSerializer.Serialize(new { status = "done", passed = true, rows });
        }
        catch (Exception error)
        {
            Report = JsonSerializer.Serialize(new { status = "done", passed = false, rows, error = error.ToString() });
        }
    }
}
