using System.Text.Json;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.TestSupport;
using ShoujoKageki.Core.V1;
using ShoujoKageki.Core.V1.Presentation;
using ShoujoKagekiJunna.Combat;
using ShoujoKagekiJunna.Models;
using ShoujoKagekiAijoKaren.src.Core.DisableRelicSystem;

namespace CoreMatrixIntegration;

public sealed class RefactorFixture : AbstractConsoleCmd
{
    public override string CmdName => "refactor_fixture";
    public override string Args => "run";
    public override string Description => "Targeted option, execution, snapshot and display regression.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? player, string[] args) => player?.PlayerCombatState == null
        ? new(false, "Combat required") : new(Run(player), true, "running");

    private sealed class Selector(bool fail = false) : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
            => fail ? Task.FromException<IEnumerable<CardModel>>(new InvalidOperationException("selection-fixture"))
                : Task.FromResult(options.Take(maxSelect));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<CardRewardAlternative> alternatives) => throw new NotSupportedException();
    }
    private static async Task Run(Player player)
    {
        var rows = new List<object>();
        void Check(string name, bool passed)
        {
            rows.Add(new { name, passed });
            if (!passed) throw new InvalidOperationException(name);
        }
        MatrixFixture.Report = JsonSerializer.Serialize(new { status = "running" });
        var settings = CoreApi.Configuration.Current;
        var context = new ThrowingPlayerChoiceContext();
        Node2D? layer = null;
        try
        {
            var combat = player.Creature.CombatState!;
            foreach (var card in player.PlayerCombatState!.AllCards.ToArray())
            {
                ArrowCombat.Cancel(card);
                await CardPileCmd.RemoveFromCombat(card);
            }
            var enemy = combat.HittableEnemies.First(target => target.IsAlive);
            await CreatureCmd.SetMaxHp(enemy, 10000);
            await CreatureCmd.SetCurrentHp(enemy, 10000);
            await PlayerCmd.GainEnergy(9 - player.PlayerCombatState.Energy, player);

            var options = new[] { "JB01", "JB02", "JH59", "JA38" }
                .Select(id => CardOptions.Create(CardCatalog.Model(id), player)).ToArray();
            Check("option models stay outside combat registration", options.All(card => card.Owner == player && card.Pile == null && !combat.ContainsCard(card)));
            using (CardSelectCmd.UseSelector(new Selector()))
            {
                Check("three-choice options use the public lifecycle", await CardOptions.Choose(context, options.Take(3).ToArray(), player) == options[0]);
                Check("four-choice options use the public lifecycle", await CardOptions.Choose(context, options, player) == options[0]);
            }
            using (CardSelectCmd.UseSelector(new Selector(fail: true)))
            {
                bool failed = false;
                try { await CardOptions.Choose(context, options, player); }
                catch (InvalidOperationException error) when (error.Message == "selection-fixture") { failed = true; }
                Check("selection failures preserve detached option ownership", failed && options.All(card => card.Owner == player && !combat.ContainsCard(card)));
            }

            using var single = KnowledgeCombat.SingleCard();
            var powerCard = CardCatalog.Create("JH59", player);
            Shine.SetState(powerCard, 1, 9, temporary: true);
            await CardPileCmd.Add(powerCard, PileType.Hand);
            Check("last-use Shine power executes through native play", await CardExecution.Play(context, powerCard, new CardExecutionRequest()));
            Check("last-use Shine power retains its installed effect", player.Creature.Powers.OfType<CatalogPower>().Any(power => power.DesignId == "JH59"));
            var snapshot = CoreApi.Get<ICardSnapshots>().Capture(powerCard);
            Check("depleted snapshots preserve the original model", snapshot.GetType() == powerCard.GetType() && Shine.GetState(snapshot)?.Remaining == 0);
            Check("depleted actual copies still produce the common shell", CardCopies.Preview(powerCard, CardCopyPurpose.TemporaryCopy).GetType() != powerCard.GetType());

            int deferredEffects = 0, oldMiddlewareEffects = 0;
            PlayerChoiceContext? observedContext = null;
            ResourceInfo observedResources = default;
            using var observer = CoreApi.Get<ICardEffectRouting>().Register(async (card, choice, play, next) =>
            {
                deferredEffects++;
                observedContext = choice;
                observedResources = play.Resources;
                await next(choice, play);
            }, _ => null);
            using var oldObserver = CoreApi.Get<ICardPlayRouting>().Register(async (card, choice, play, next) =>
            {
                oldMiddlewareEffects++;
                await next(play);
            }, _ => null);
            var arrow = CardCatalog.Create("JB03", player);
            await CardPileCmd.Add(arrow, PileType.Hand);
            Shine.SetState(arrow, 2, 2, temporary: true);
            int hp = enemy.CurrentHp;
            Check("native arrow deployment starts", await CardExecution.Play(context, arrow, new CardExecutionRequest { Target = enemy }));
            Check("deployment defers the complete downstream route", deferredEffects == 0 && oldMiddlewareEffects == 0 && enemy.CurrentHp == hp && Shine.GetState(arrow)?.Remaining == 2);
            var resumedContext = new ThrowingPlayerChoiceContext();
            await ArrowCombat.Accelerate(resumedContext, ArrowCombat.Find(arrow)!, 99);
            Check("deferred execution retains both public routing generations", deferredEffects == 1 && oldMiddlewareEffects == 1);
            Check("deferred execution uses the current choice context", ReferenceEquals(observedContext, resumedContext));
            Check("deferred effect damages and debits once", enemy.CurrentHp < hp && Shine.GetState(arrow)?.Remaining == 1 && arrow.Pile?.Type == PileType.Discard);

            var paid = CardCatalog.Create("JB01", player);
            await CardPileCmd.Add(paid, PileType.Hand);
            int beforePayment = player.PlayerCombatState.Energy;
            Check("paid play enters the native pipeline", await CardExecution.Play(context, paid,
                new CardExecutionRequest { Payment = CardPayment.Paid, Target = enemy }));
            Check("paid play spends resources once", player.PlayerCombatState.Energy == beforePayment - 1);
            Check("paid resources reach the actual effect", observedResources.EnergySpent == 1 && observedResources.EnergyValue == 1);
            var counter = NCombatRoom.Instance!.GetTree().Root.FindChildren("*", "", true, false).OfType<NEnergyCounter>().Single();
            Check("energy display follows the native state tracker", counter.GetNode<MegaLabel>("Label").Text == $"{player.PlayerCombatState.Energy}/{player.PlayerCombatState.MaxEnergy}");

            var paidX = combat.CreateCard(ModelDb.Card<Whirlwind>(), player);
            await CardPileCmd.Add(paidX, PileType.Hand);
            int availableX = player.PlayerCombatState.Energy;
            Check("paid X ignores the free-only capture setting", await CardExecution.Play(context, paidX,
                new CardExecutionRequest { Payment = CardPayment.Paid, CaptureAvailableX = false }));
            Check("paid X carries the actual spent value", player.PlayerCombatState.Energy == 0 &&
                observedResources.EnergySpent == availableX && observedResources.EnergyValue == availableX && paidX.EnergyCost.CapturedXValue == availableX);
            await PlayerCmd.GainEnergy(2, player);

            var blocked = CardCatalog.Create("JB01", player);
            blocked.AddKeyword(CardKeyword.Unplayable);
            await CardPileCmd.Add(blocked, PileType.Hand);
            int energy = player.PlayerCombatState.Energy;
            Check("blocked paid play preserves resources", !await CardExecution.Play(context, blocked, new CardExecutionRequest { Payment = CardPayment.Paid, Target = enemy }) && player.PlayerCombatState.Energy == energy);
            await HandArrow.Play(context, blocked);
            Check("blocked hand-arrow play restores its original form", ArrowCombat.Find(blocked) == null && blocked.Type == CardType.Attack);

            var relics = player.Relics.ToArray();
            var relic = relics.First(DisableRelicManager.IsRelicLockable);
            int position = Array.IndexOf(relics, relic);
            Check("relic disable keeps native order", DisableRelicManager.DisableRelicAtPosition(player, position) && player.Relics[position] is KarenLockRelic);
            DisableRelicManager.RestoreAllRelics(player);
            Check("relic restoration preserves exact model identity", player.Relics.SequenceEqual(relics) && !relic.HasBeenRemovedFromState);
            Check("relic display projects the restored model", DisableRelicNodeManager.FindRelicHolder(relic, player)?.Relic.Model == relic);
            DisableRelicManager.RestoreAllRelics(player);
            Check("repeated relic restoration is idempotent", player.Relics.SequenceEqual(relics));

            CoreApi.Configuration.Update(settings with { VfxLevel = CoreVfxLevel.Full });
            layer = new Node2D();
            NCombatRoom.Instance!.AddChild(layer);
            CoreEffects.Register(layer);
            CoreApi.Configuration.Update(settings with { VfxLevel = CoreVfxLevel.Off });
            Check("registered effects follow Off", !layer.Visible);
            layer.Show();
            Check("effect-local visibility changes retain suppression", !layer.Visible);
            CoreApi.Configuration.Update(settings with { VfxLevel = CoreVfxLevel.Full });
            Check("registered effects restore visibility", layer.Visible);
            MatrixFixture.Report = JsonSerializer.Serialize(new { status = "done", passed = true, rows });
        }
        catch (Exception error)
        {
            MatrixFixture.Report = JsonSerializer.Serialize(new { status = "done", passed = false, rows, error = error.ToString() });
        }
        finally
        {
            CoreApi.Configuration.Update(settings);
            layer?.QueueFree();
        }
    }
}
