using System.Reflection;
using System.Text.Json;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using CoreRoleIntegration;
using ShoujoKageki.Core.V1;
using ShoujoKagekiJunna.Combat;
using ShoujoKagekiJunna.Models;
using ShoujoKagekiAijoKaren.src.Core.Commands;
using ShoujoKagekiAijoKaren.src.Core.Models.Cards;
using ShoujoKagekiAijoKaren.src.Core.Models.Cards.shine;
using ShoujoKagekiAijoKaren.src.Core.Models.Powers;
using ShoujoKagekiAijoKaren.src.Core.PromisePileSystem;

namespace CoreMatrixIntegration;

/// <summary>Only the copy, power, relation, selection and pile lifecycles changed by this refactor.</summary>
public sealed class LifecycleFixture : AbstractConsoleCmd
{
    public override string CmdName => "lifecycle_fixture";
    public override string Args => "run";
    public override string Description => "Targeted shared lifecycle regression.";
    public override bool IsNetworked => true;
    public override CmdResult Process(Player? player, string[] args) => player == null
        ? new(false, "Player required") : new(Run(player), true, "running");
    private sealed class Selector(bool empty = false) : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
            => Task.FromResult(empty ? Enumerable.Empty<CardModel>() : options.Take(maxSelect));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
            => throw new NotSupportedException();
    }
    private static async Task Run(Player player)
    {
        var rows = new List<object>();
        void Check(string name, bool pass) { rows.Add(new { name, pass }); if (!pass) throw new InvalidOperationException(name); }
        MatrixFixture.Report = JsonSerializer.Serialize(new { status = "running", actor = player.NetId });
        var context = new ThrowingPlayerChoiceContext();
        try
        {
            var state = player.Creature.CombatState!;
            var recipient = state.Players.FirstOrDefault(other => other != player) ?? player;
            if (player.Character.Id.Entry != "KAREN")
            {
                await PromisePileManager.UpdatePowerAsync(player);
                Check("refresh does not activate an unrelated character", !player.Creature.HasPower<KarenPromisePilePower>());
                var automatic = state.CreateCard(ModelDb.Card<ResolutionProbeCard>(), player);
                await CardPileCmd.Add(automatic, PileType.Draw, CardPilePosition.Top);
                await CardPileCmd.AutoPlayFromDrawPile(context, player, 1, CardPilePosition.Top, forceExhaust: false);
                Check("native draw-pile autoplay does not activate a promise lifecycle", !player.Creature.HasPower<KarenPromisePilePower>());
            }
            var permanent = player.RunState.CreateCard(ModelDb.Card<JunnaFullDraw>(), player);
            Shine.SetState(permanent, 2, 6);
            await CardPileCmd.Add(permanent, PileType.Deck);
            var actual = CardCopies.ForCombat(permanent, player, CardCopyPurpose.Transfer, permanent);
            await CardPileCmd.Add(actual, PileType.Hand);
            ArrowConversion.Convert(actual);
            DamageModifiers.Add(actual, 5, 2);
            actual.EnergyCost.SetThisCombat(0);
            actual.DynamicVars.Damage.BaseValue = 39;
            var snapshot = CardCopies.Snapshot(actual);
            rows.Add(new { name = "copy source evidence", pass = true, source = Shine.GetState(permanent), actual = Shine.GetState(actual), captured = Shine.GetState(snapshot),
                inDeck = player.Deck.Cards.Contains(permanent), linked = actual.DeckVersion == permanent,
                nativeType = snapshot.GetType().FullName, converted = !CardCatalog.IsArrow(snapshot), damage = snapshot.DynamicVars.Damage.BaseValue });
            Check("snapshot retains native type, conversion and generated numbers", snapshot is JunnaFullDraw && !CardCatalog.IsArrow(snapshot) && snapshot.DynamicVars.Damage.BaseValue == 39);
            Check("snapshot retains its permanent origin", Shine.GetState(snapshot) is { Temporary: false });
            var copy = CardCopies.ForCombat(actual, recipient, CardCopyPurpose.TemporaryCopy);
            Check("combat copy preserves recipient and native clone identity", copy.Owner == recipient && actual.Owner == player && copy.CloneOf == actual && state.ContainsCard(copy));
            Check("combat copy preserves temporary cost and conversion", !CardCatalog.IsArrow(copy) && copy.EnergyCost.GetWithModifiers(CostModifiers.Local) == 0);
            var invested = DamageModifiers.CaptureDamageModifierSnapshot(copy);
            Check("combat copy preserves damage and hit investments", invested.DamageBonus == 5 && invested.ExtraHits == 2 && copy.DynamicVars.Damage.BaseValue == 39);
            Check("temporary Shine lifecycle is independent", Shine.GetState(copy) is { Remaining: 2, Maximum: 6, Temporary: true } && copy.DeckVersion == null);
            copy.RemoveFromState();
            var transferred = permanent.CreateTransferCopy(recipient);
            Check("a permanent transfer keeps its lifecycle before entering the deck", Shine.GetState(transferred) is { Remaining: 2, Maximum: 6, Temporary: false });
            var received = await CardPileCmd.Add(transferred, PileType.Deck);
            Check("Karen transfer helper assigns the new permanent owner", received.success && received.cardAdded.Owner == recipient && permanent.Owner == player);
            await CardPileCmd.RemoveFromDeck(received.cardAdded, showPreview: false);
            Shine.SetState(actual, 0, 6);
            Check("an exhausted internal snapshot retains its original type", CardCopies.Snapshot(actual) is JunnaFullDraw);
            Check("an exhausted acquired copy is an empty shell", CardCopies.Preview(actual, CardCopyPurpose.Transfer).Id != actual.Id);
            await CardPileCmd.RemoveFromCombat(actual);
            using (CardSelectCmd.UseSelector(new Selector()))
                Check("shared selection keeps the native offered card", await CardSelection.Choose(context, [snapshot], player) == snapshot);
            using (CardSelectCmd.UseSelector(new Selector(true)))
            {
                Check("optional choice permits skipping", await CardSelection.Choose(context, [snapshot], player, canSkip: true) == null);
                bool rejected = false;
                try { await CardSelection.Choose(context, [snapshot], player); }
                catch (InvalidOperationException) { rejected = true; }
                Check("required choice rejects an empty automated selection", rejected);
            }
            using var deterministicChoices = CardSelectCmd.UseSelector(new Selector());
            var north = player.RunState.CreateCard(CardCatalog.Model("JH59"), player);
            Shine.SetState(north, 1, 9);
            await CardPileCmd.Add(north, PileType.Deck);
            var last = CardCopies.ForCombat(north, player, CardCopyPurpose.Transfer, north);
            await CardPileCmd.Add(last, PileType.Hand);
            int archived = Shine.GetDepletedCards(player).Count;
            await CardCmd.AutoPlay(context, last, player.Creature);
            var power = player.Creature.Powers.OfType<CatalogPower>().Single(item => item.DesignId == "JH59");
            rows.Add(new { name = "last-use power evidence", pass = true, amount = power.Amount,
                archivedBefore = archived, archivedAfter = Shine.GetDepletedCards(player).Count,
                inDeck = player.Deck.Cards.Contains(north), state = Shine.GetState(last), permanentState = Shine.GetState(north),
                linked = last.DeckVersion == north, duplicate = last.IsDupe });
            Check("last-use power installs and archives its permanent card", power.Amount > 0 && Shine.GetDepletedCards(player).Count == archived + 1 && !player.Deck.Cards.Contains(north));
            int energy = player.PlayerCombatState!.Energy;
            await power.AfterPlayerTurnStart(context, player);
            Check("installed effect survives its source depletion", player.PlayerCombatState.Energy > energy);
            if (recipient != player)
            {
                var relay = player.RunState.CreateCard(ModelDb.Card<KarenFightRelay>(), player);
                Shine.SetState(relay, 2, 3);
                await CardPileCmd.Add(relay, PileType.Deck);
                var played = CardCopies.ForCombat(relay, player, CardCopyPurpose.Transfer, relay);
                await CardPileCmd.Add(played, PileType.Hand);
                int before = recipient.Deck.Cards.Count(card => card.Id == relay.Id);
                await CardCmd.AutoPlay(context, played, player.Creature);
                Check("native Karen relay transfers the remaining lifecycle", !player.Deck.Cards.Contains(relay) && recipient.Deck.Cards.Count(card => card.Id == relay.Id) == before + 1
                    && Shine.GetState(recipient.Deck.Cards.Last(card => card.Id == relay.Id)) is { Remaining: 1, Maximum: 3 });
            }
            var paper = player.RunState.CreateCard(CardCatalog.Model("JK184"), player);
            await CardPileCmd.Add(paper, PileType.Deck);
            var research = typeof(CardCatalog).Assembly.GetType("ShoujoKagekiJunna.Combat.KnowledgeRelations")!;
            var links = (SavedSpireField<CardModel, string>)AccessTools.Field(research, "Links").GetValue(null)!;
            var researchGroup = AccessTools.Method(typeof(KnowledgeProgress), "ResearchGroup");
            string groupId = (string)researchGroup.Invoke(null, [paper])!;
            var members = player.Deck.Cards.Where(card => (string?)researchGroup.Invoke(null, [card]) == groupId).ToArray();
            Check("research acquisition grants its full physical group", !string.IsNullOrEmpty(groupId) && members.Length == 7);
            foreach (var member in members) links.Set(member, null!);
            links.Set(paper, player.NetId + "/9000");
            _ = DynamicKnowledge.Members(paper);
            _ = paper.HoverTips.ToArray();
            Check("queries and hover tips do not migrate saved links", members.Where(card => card != paper).All(card => string.IsNullOrEmpty(links.Get(card))));
            var save = RunManager.Instance.ToSave(null);
            var writer = new PacketWriter(); writer.Write(RunManager.CanonicalizeSave(save, player.NetId));
            var reader = new PacketReader(); reader.Reset(writer.Buffer);
            var loaded = RunState.FromSerializable(reader.Read<SerializableRun>());
            var restored = loaded.Players.Single(item => item.NetId == player.NetId);
            var restoredPaper = restored.Deck.Cards.Last(card => card.Id == paper.Id);
            rows.Add(new { name = "research save evidence", pass = true, expected = members.Length,
                original = members.Select(card => new { id = card.Id.Entry, group = researchGroup.Invoke(null, [card]), link = links.Get(card) }).ToArray(),
                restored = restored.Deck.Cards.Where(card => CardCatalog.DesignId(card)?.StartsWith("JK184", StringComparison.Ordinal) == true)
                    .Select(card => new { id = card.Id.Entry, group = researchGroup.Invoke(null, [card]), link = links.Get(card) }).ToArray(),
                patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(RunState), nameof(RunState.FromSerializable)))?.Postfixes.Select(patch => patch.PatchMethod.DeclaringType?.FullName).ToArray(),
                actual = DynamicKnowledge.Members(restoredPaper).Count });
            Check("native load migrates old research links before queries", DynamicKnowledge.Members(restoredPaper).Count == members.Length);
            var sequence = (SavedSpireField<Player, int>)AccessTools.Field(research, "NextGroup").GetValue(null)!;
            Check("migration advances the saved group sequence", sequence.Get(restored) >= 9000);
            restored.SyncWithSerializedPlayer(player.ToSerializable());
            restoredPaper = restored.Deck.Cards.Last(card => card.Id == paper.Id);
            Check("native synchronization normalizes the same relations", DynamicKnowledge.Members(restoredPaper).Count == members.Length);
            var move = state.CreateCard(ModelDb.Card<ResolutionProbeCard>(), player);
            await PromisePileCmd.Add(player, move);
            Check("explicit promise use activates the participating player", player.Creature.HasPower<KarenPromisePilePower>() && PromisePileManager.EffectivePile(player).Cards.Contains(move));
            await CardPileCmd.Add(move, PileType.Discard);
            Check("native movement refreshes the promise counter", PromisePileManager.GetCount(player) == PromisePileManager.EffectivePile(player).Cards.Count);
            await PromisePileManager.EnterMode(player, PromisePileMode.Void);
            await PromisePileManager.EnterMode(player, PromisePileMode.InfiniteReinforcement);
            Check("void and infinite modes share one replenishment path", PromisePileManager.EffectivePile(player).Type == PileType.Draw && PromisePileManager.GetCount(player) == CardPile.MaxCardsInHand);
            await CardPileCmd.RemoveFromCombat(player.PlayerCombatState.Hand.Cards.ToArray());
            var drawn = await PromisePileCmd.Draw(context, player);
            Check("each native draw completes before a single refill", drawn != null && PromisePileManager.GetCount(player) == CardPile.MaxCardsInHand);
            MatrixFixture.Report = JsonSerializer.Serialize(new { status = "done", actor = player.NetId, passed = true, rows });
        }
        catch (Exception error)
        {
            MatrixFixture.Report = JsonSerializer.Serialize(new { status = "done", actor = player.NetId, passed = false, rows, error = error.ToString() });
        }
    }
}
