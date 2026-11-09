using System.Text.Json;
using BaseLib.Utils;
using BaseLib.Utils.Attributes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.TestSupport;
using ShoujoKageki.Core.V1;
using ShoujoKageki.Core.V1.Presentation;
using ShoujoKagekiJunna.Combat;
using ShoujoKagekiJunna.Models;
using ShoujoKagekiJunna.UI;

namespace CoreMatrixIntegration;

[Pool(typeof(JunnaCardPool))]
[CustomID("CORE_UPGRADE_COMPLETION_PROBE")]
public sealed class UpgradeCompletionCard() : BaseLib.Abstracts.CustomCardModel(0, CardType.Skill, CardRarity.Token, TargetType.Self, showInCardLibrary: false)
{
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var study = Owner.PlayerCombatState!.AllCards.OfType<CatalogCard>().First(card => card.DesignId == "JK82");
        CardCmd.Upgrade(study, CardPreviewStyle.None);
        CardCmd.Upgrade(study, CardPreviewStyle.None);
        await Task.CompletedTask;
    }
}
public sealed class ConsolidationFixture : AbstractConsoleCmd
{
    public override string CmdName => "consolidation_fixture";
    public override string Args => "run";
    public override string Description => "Directed shared lifecycle regression.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? player, string[] args) => player?.PlayerCombatState == null
        ? new(false, "Combat required") : new(Run(player), true, "running");
    private sealed class Selector : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
            => Task.FromResult(options.Take(maxSelect));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
            => throw new NotSupportedException();
    }
    private static async Task Run(Player player)
    {
        var rows = new List<object>();
        void Check(string name, bool passed)
        {
            rows.Add(new { name, passed });
            if (!passed) throw new InvalidOperationException(name);
        }
        MatrixFixture.Report = JsonSerializer.Serialize(new { status = "running", actor = player.NetId });
        var settings = CoreApi.Configuration.Current;
        var room = NCombatRoom.Instance!;
        var lifetime = new Node();
        room.AddChild(lifetime);
        try
        {
            using var single = KnowledgeCombat.SingleCard();
            var context = new ThrowingPlayerChoiceContext();
            var study = CardCatalog.Create("JK82", player);
            await CardPileCmd.Add(study, PileType.Hand);
            int knowledgeBefore = CardActions.All(player).Count(card => CardCatalog.DesignId(card) == "JK82B");
            var effect = player.Creature.CombatState!.CreateCard<UpgradeCompletionCard>(player);
            int observedUpgrades = 0;
            using (CoreApi.Get<ICardUpgrades>().Register(card => card == study, async (_, _, _) =>
            {
                await Task.Yield();
                observedUpgrades++;
            })) await CardExecution.Play(context, effect, new CardExecutionRequest());
            Check("native completion waits for asynchronous upgrade observers", observedUpgrades == 2);
            Check("native effect completion includes both upgrade rewards", study.CurrentUpgradeLevel == 2 &&
                CardActions.All(player).Count(card => CardCatalog.DesignId(card) == "JK82B") == knowledgeBefore + 2);
            await CardUpgrades.Upgrade(study, CardPreviewStyle.None);
            Check("explicit asynchronous upgrade completes its reward", study.CurrentUpgradeLevel == 3 &&
                CardActions.All(player).Count(card => CardCatalog.DesignId(card) == "JK82B") == knowledgeBefore + 3);
            using (CardSelectCmd.UseSelector(new Selector()))
            {
                var selected = await CardPicking.Pick(context, player, new[] { study, study }, new CardPickRequest
                { Prompt = new LocString("gameplay_ui", "CORE_SELECT_OPTION"), Minimum = 4, Maximum = 4 });
                Check("picking deduplicates candidates and clips counts", selected.Count == 1 && selected[0] == study);
            }
            var source = CardCatalog.Create(CardCatalog.Groups.First().MainCardId, player);
            Shine.SetState(source, 0, 3, true);
            var snapshots = KnowledgePreviewCards.Create(source);
            var preview = new KnowledgeGroupPreview();
            var visual = (MegaCrit.Sts2.Core.Nodes.Cards.NCard)AccessTools.Method(typeof(KnowledgeGroupPreview), "CreatePreview").Invoke(preview, [snapshots[0], room])!;
            Check("knowledge preview keeps an exhausted card model", visual.Model!.GetType() == snapshots[0].GetType());
            visual.QueueFree(); preview.Free(); source.RemoveFromState();
            var first = player.RunState.CreateCard(CardCatalog.Model("JK148"), player);
            var companion = player.RunState.CreateCard(CardCatalog.Model("JK148B"), player);
            await CardPileCmd.Add(new[] { first, companion }, PileType.Deck);
            AccessTools.Method(typeof(KnowledgeProgress), "BindResearch").Invoke(null, [new[] { first, companion }]);
            var links = (SavedSpireField<CardModel, string>)AccessTools.Field(typeof(DynamicKnowledge), "Links").GetValue(null)!;
            links.Set(first, "legacy-test/1");
            _ = DynamicKnowledge.Members(first);
            Check("querying legacy membership never mutates companions", string.IsNullOrEmpty(links.Get(companion)));
            var restored = Player.FromSerializable(player.ToSerializable());
            var restoredFirst = restored.Deck.Cards.First(card => card.Id == first.Id);
            rows.Add(new { name = "research restoration evidence", original = links.Get(first), research = AccessTools.Method(typeof(KnowledgeProgress), "ResearchGroup").Invoke(null, [first]),
                restored = restored.Deck.Cards.Where(card => card.Id == first.Id || card.Id == companion.Id).Select(card => new { id = card.Id.ToString(), link = links.Get(card), research = AccessTools.Method(typeof(KnowledgeProgress), "ResearchGroup").Invoke(null, [card]) }).ToArray() });
            Check("player restoration normalizes legacy research groups", restored.Deck.Cards.Where(card => card.Id == first.Id || card.Id == companion.Id).Count(card => links.Get(card) == "legacy-test/1") == 2);
            var attack = CardCatalog.Create("JB01", player);
            await CardPileCmd.Add(attack, PileType.Hand);
            DamageModifiers.AddMany(new[] { study, attack, attack }, 2);
            Check("batch investment deduplicates and respects damage capability", DamageModifiers.CaptureDamageModifierSnapshot(attack).DamageBonus == 2 && DamageModifiers.CaptureDamageModifierSnapshot(study).DamageBonus == 0);
            var result = new CardCreationResult(study);
            var relic = player.Relics.First();
            result.ModifyCard(first, relic); result.ModifyCard(companion, relic);
            Check("repeated relic registration keeps the latest replacement", result.Card == companion && result.ModifyingRelics.Count() == 1);
            var layer = new Node2D(); lifetime.AddChild(layer);
            CoreEffects.Register(layer, () => CoreApi.Configuration.Current.EnableShineEffects);
            CoreApi.Configuration.Update(settings with { VfxLevel = CoreVfxLevel.Full, EnableShineEffects = false });
            Check("explicit effect policy suppresses its layer", !layer.Visible);
            CoreApi.Configuration.Update(settings with { VfxLevel = CoreVfxLevel.Full, EnableShineEffects = true });
            Check("explicit effect policy restores visibility", layer.Visible);
            CoreApi.Configuration.Update(settings with { EnableMusicPlayer = false });
            var music = CoreApi.Get<IMusicOverrides>();
            using (var track = music.AcquireTrack("probe", new MusicOverrideRequest
            { ResourcePath = "res://ShoujoKagekiAijoKaren/audio/Karen_form.MP3", Volume = .2f, FadeOutSeconds = 0, Lifetime = lifetime }))
            {
                var audio = lifetime.GetChildren().OfType<AudioStreamPlayer>().Single();
                Check("temporary music plays with the interface disabled", audio.Playing && !audio.StreamPaused && Math.Abs(audio.VolumeLinear - .2f) < .001);
                using (music.AcquireSilence("cutscene", lifetime)) Check("silence pauses the displaced track", audio.StreamPaused);
                Check("releasing silence resumes the same player", audio.Playing && !audio.StreamPaused);
                using (CoreApi.Get<IMusicPlayback>().AcquirePriority("legacy-priority"))
                    Check("legacy priority suspends temporary music", audio.StreamPaused);
                Check("legacy priority disposal restores temporary music", !audio.StreamPaused);
                CoreApi.Get<IMusicPlayback>().SetFormPriority(true);
                Check("legacy form priority suspends temporary music", audio.StreamPaused);
                CoreApi.Get<IMusicPlayback>().SetFormPriority(false);
                Check("legacy form priority release restores temporary music", !audio.StreamPaused);
            }
            await CoreEffects.DrawFrame(room);
            Check("released temporary audio is freed", lifetime.GetChildren().OfType<AudioStreamPlayer>().Count() == 0);
            MatrixFixture.Report = JsonSerializer.Serialize(new { status = "done", actor = player.NetId, passed = true, rows });
        }
        catch (Exception error) { MatrixFixture.Report = JsonSerializer.Serialize(new { status = "done", actor = player.NetId, passed = false, rows, error = error.ToString() }); }
        finally { lifetime.QueueFree(); CoreApi.Configuration.Update(settings); }
    }
}
