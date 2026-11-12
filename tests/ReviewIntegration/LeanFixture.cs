using System.Collections;
using System.Reflection;
using System.Text.Json;
using BaseLib.Abstracts;
using BaseLib.Utils;
using BaseLib.Utils.Attributes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using ShoujoKageki.Core.V1;
using ShoujoKagekiJunna.Combat;
using ShoujoKagekiJunna.Models;
using ShoujoKagekiAijoKaren.src.Core.Models.Potions;
using ShoujoKagekiAijoKaren.src.Core.Models.Cards.ancient;
using ShoujoKagekiAijoKaren.src.Models.Cards;
using ShoujoKagekiAijoKaren.src.Models.Relics;

namespace CoreReviewIntegration;

public sealed class LeanFixture : AbstractConsoleCmd
{
    private static string Report = "{}";
    private static CardModel? FailingCard;
    private static readonly Exception EffectFailure = new InvalidOperationException("lean-effect-failure");
    private static bool FailEffect(CardModel __instance, ref Task __result)
    {
        if (!ReferenceEquals(__instance, FailingCard)) return true;
        __result = Task.FromException(EffectFailure);
        return false;
    }
    public override string CmdName => "lean_fixture";
    public override string Args => "run|report";
    public override string Description => "Directed refactor regression.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? player, string[] args)
        => args.FirstOrDefault() == "report" ? new(true, Report)
            : player?.PlayerCombatState == null ? new(false, "Combat required") : new(Run(player), true, "running");
    private sealed class Scope(Action release) : IDisposable { public void Dispose() => release(); }
    private static async Task Run(Player player)
    {
        var rows = new List<object>();
        Report = JsonSerializer.Serialize(new { status = "running" });
        void Check(string name, bool passed)
        {
            rows.Add(new { name, passed });
            if (!passed) throw new InvalidOperationException(name);
        }
        var context = new BlockingPlayerChoiceContext();
        var combat = player.Creature.CombatState!;
        var patcher = new Harmony("CoreReviewIntegration.LeanFailure");
        try
        {
            Check("hairpin keeps model identity", ModelDb.Relic<KarenHairpinRelic>().Id.Entry == "KAREN_HAIRPIN_RELIC");
            Check("BaseLib supplies the starter upgrade", ((CustomRelicModel)ModelDb.Relic<KarenHairpinRelic>()).GetUpgradeReplacement()!.Id == ModelDb.Relic<KarenHairpin2Relic>().Id);
            foreach (var potion in new PotionModel[] { ModelDb.Potion<KarenShinePotion>(), ModelDb.Potion<KarenTeaPotion>(), ModelDb.Potion<KarenAwakePotion>() })
            {
                string expected = potion switch { KarenShinePotion => "KAREN_SHINE_POTION", KarenTeaPotion => "KAREN_TEA_POTION", _ => "KAREN_AWAKE_POTION" };
                Check(expected + " keeps model and image identity", potion.Id.Entry == expected &&
                    (string)AccessTools.Property(typeof(PotionModel), "PackedImagePath").GetValue(potion)! == "res://images/potions/" + expected.ToLowerInvariant() + ".png");
            }
            using var single = KnowledgeCombat.SingleCard();
            var fall = player.RunState.CreateCard(ModelDb.Card<KarenFall>(), player);
            await CardPileCmd.Add(fall, PileType.Deck, CardPilePosition.Top);
            CardCmd.Upgrade(fall);
            var enchantment = (EnchantmentModel)CoreApi.Get<ICoreDiscovery>().Models.Where(model => typeof(EnchantmentModel).IsAssignableFrom(model.ModelType))
                .Select(model => ModelDb.GetById<EnchantmentModel>(ModelDb.GetId(model.ModelType))).First(enchantment => enchantment.CanEnchant(fall)).MutableClone();
            CardCmd.Enchant(enchantment, fall, 1);
            var tooth = ModelDb.Relic<ArchaicTooth>();
            var starter = AccessTools.Method(typeof(ArchaicTooth), "GetTranscendenceStarterCard");
            var transform = AccessTools.Method(typeof(ArchaicTooth), "GetTranscendenceTransformedCard");
            Check("Karen retains fallback starter eligibility", starter.Invoke(tooth, [player]) is KarenFall);
            var ancient = (CardModel)transform.Invoke(tooth, [fall])!;
            Check("native transformation retains upgrade and enchantment", ancient is KarenWhy && ancient.IsUpgraded &&
                ancient.Enchantment?.Id == fall.Enchantment!.Id && ancient.Enchantment.Amount == fall.Enchantment.Amount);
            Check("BaseLib excludes ancient replacement from normal transformation pools",
                ArchaicTooth.TranscendenceCards.Count(card => card is KarenWhy) == 1);
            var bash = player.RunState.CreateCard(ModelDb.Card<Bash>(), player);
            await CardPileCmd.Add(bash, PileType.Deck);
            Check("other native starter keeps priority over Karen fallback", ReferenceEquals(starter.Invoke(tooth, [player]), bash));
            player.Deck.RemoveInternal(bash);
            player.Deck.RemoveInternal(fall);

            var relations = (SavedSpireField<Player, string>)AccessTools.Field(typeof(DynamicKnowledge), "Relations").GetValue(null)!;
            string? saved = relations.Get(player);
            try
            {
                relations.Set(player, null!);
                var source = combat.CreateCard(CardCatalog.Model("JK32"), player);
                Check("knowledge query reads defaults", DynamicKnowledge.IsLinked(source) && DynamicKnowledge.Members(source).Count > 0);
                _ = source.HoverTips.ToArray();
                Check("knowledge and hover queries do not write save fields", relations.Get(player) == null);
                AccessTools.Method(typeof(DynamicKnowledge), "Normalize").Invoke(null, [player]);
                string? normalized = relations.Get(player);
                Check("normalization explicitly saves defaults", !string.IsNullOrEmpty(normalized));
                _ = DynamicKnowledge.Members(source);
                Check("normalized query preserves serialized relations", relations.Get(player) == normalized);
                source.RemoveFromState();
            }
            finally { relations.Set(player, saved!); }

            var card = (CatalogCard)combat.CreateCard(CardCatalog.Model("JK10"), player);
            var installedBy = new CardPlay { Card = card, Target = player.Creature, ResultPile = PileType.Discard,
                Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
                IsAutoPlay = true, PlayIndex = 0, PlayCount = 1,
#if STS2_BETA
                Player = player,
#endif
            };
            await CatalogPower.Install(card, context, installedBy);
            var power = player.Creature.Powers.OfType<CatalogPower>().Single(power => power.DesignId == "JK10");
            var grants = AccessTools.Field(typeof(CatalogPower), "_grants");
            object original = ((IList)grants.GetValue(power)!)[0]!;
            var turn = AccessTools.Field(original.GetType(), "Turn");
            var used = AccessTools.Field(original.GetType(), "Used");
            var cycle = AccessTools.Field(original.GetType(), "CycleIndex");
            turn.SetValue(original, 4); used.SetValue(original, 1); cycle.SetValue(original, 2);
            var clonedPower = (CatalogPower)power.MutableClone();
            object cloned = ((IList)grants.GetValue(clonedPower)!)[0]!;
            Check("power clone retains current counters", (int)turn.GetValue(cloned)! == 4 && (int)used.GetValue(cloned)! == 1 && (int)cycle.GetValue(cloned)! == 2);
            turn.SetValue(cloned, 7); used.SetValue(cloned, 0); cycle.SetValue(cloned, 0);
            Check("power clone counters are independent", (int)turn.GetValue(original)! == 4 && (int)used.GetValue(original)! == 1 && (int)cycle.GetValue(original)! == 2);
            var sourceField = AccessTools.Field(original.GetType(), "Card");
            Check("power clone owns an independent source snapshot", !ReferenceEquals(sourceField.GetValue(original), sourceField.GetValue(cloned)));
            await PowerCmd.Remove(power);
            card.RemoveFromState();

            var generated = CardCatalog.Create("JA81B", player);
            var presentationType = typeof(CardActions).Assembly.GetType("ShoujoKagekiJunna.Combat.GeneratedArrowPresentation", true)!;
            var generate = AccessTools.Method(presentationType, "Play");
            await (Task)generate.Invoke(null, [generated, (Func<Task>)(() => Task.CompletedTask)])!;
            Check("unused generated arrow is removed from actual combat state", generated.HasBeenRemovedFromState &&
                !player.PlayerCombatState!.AllCards.Contains(generated));
            var beforeGeneration = player.PlayerCombatState!.AllCards.ToHashSet();
            var generationFailure = new InvalidOperationException("lean-generation-failure");
            bool generationFailed = false;
            using (CoreApi.Get<ICardMovementPipeline>().Register(move => move.NewPile == PileType.Play &&
                !beforeGeneration.Contains(move.Card) ? throw generationFailure : Task.CompletedTask))
            {
                try { await CardActions.Fly(context, player, 1); }
                catch (Exception error) when (ReferenceEquals(error, generationFailure)) { generationFailed = true; }
            }
            Check("failed generated-arrow preparation releases the actual card", generationFailed &&
                beforeGeneration.SetEquals(player.PlayerCombatState!.AllCards));

            var failingCard = FailingCard = combat.CreateCard<DefendIronclad>(player);
            patcher.Patch(AccessTools.Method(typeof(DefendIronclad), "OnPlay"),
                prefix: new HarmonyMethod(typeof(LeanFixture), nameof(FailEffect)) { priority = int.MinValue });
            await CardPileCmd.Add(failingCard, PileType.Hand);
            var cleanupFailure = new InvalidOperationException("lean-cleanup-failure");
            var released = new List<int>();
            using (CoreApi.Get<IModelExecutionScopes>().Register((model, _) => ReferenceEquals(model, failingCard)
                ? new Scope(() => { released.Add(1); throw cleanupFailure; }) : null))
            using (CoreApi.Get<IModelExecutionScopes>().Register((model, _) => ReferenceEquals(model, failingCard)
                ? new Scope(() => released.Add(2)) : null))
            {
                Exception? failure = null;
                try { await CardExecution.PlayStaged([failingCard], actual => CardExecution.Play(context, actual,
                    new CardExecutionRequest { Target = player.Creature }), unplayed => CardPileCmd.Add(unplayed, PileType.Hand)); }
                catch (Exception error) { failure = error; }
                Check("native effect and scope cleanup errors both survive", failure is AggregateException aggregate &&
                    aggregate.Flatten().InnerExceptions.Contains(EffectFailure) && aggregate.Flatten().InnerExceptions.Contains(cleanupFailure));
                Check("native scopes release once in reverse order", released.SequenceEqual(new[] { 2, 1 }));
                Check("staging restores actual card after dual failure", failingCard.Pile?.Type == PileType.Hand);
            }
            patcher.UnpatchAll(patcher.Id);
            FailingCard = null;
            await CardPileCmd.RemoveFromCombat(failingCard);
            var defend = combat.CreateCard<DefendIronclad>(player);
            await CardPileCmd.Add(defend, PileType.Hand);
            Check("subsequent native execution has a clean context", await CardExecution.Play(context, defend,
                new CardExecutionRequest { Target = player.Creature }));
            Report = JsonSerializer.Serialize(new { status = "done", passed = true, rows });
        }
        catch (Exception error) { Report = JsonSerializer.Serialize(new { status = "done", passed = false, rows, error = error.ToString() }); }
        finally { FailingCard = null; patcher.UnpatchAll(patcher.Id); }
    }
}
