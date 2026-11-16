using System.Text.Json;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using ShoujoKageki.Core.V1;

namespace CoreTheftProbe;

[ModInitializer(nameof(Initialize))]
public static class Startup
{
    internal static readonly SavedSpireField<CardModel, int> Growth = new(() => 0, "core_theft_growth");
    public static void Initialize()
    {
        _ = Growth;
        ((Godot.SceneTree)Godot.Engine.GetMainLoop()).ProcessFrame += () => Godot.RenderingServer.ForceDraw(false);
    }
}

public sealed class TheftCommand : AbstractConsoleCmd
{
    private static string _report = "idle";
    public override string CmdName => "core_theft";
    public override string Args => "run | status";
    public override string Description => "Verify native stolen-card return budgets.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? player, string[] args)
    {
        if (args.FirstOrDefault() == "status") return new(true, _report);
        if (player?.PlayerCombatState == null) return new(false, "Combat required.");
        _report = "running";
        return new(Run(player), true, "Theft checks started.");
    }
    private static async Task Run(Player player)
    {
        var rows = new List<object>();
        void Check(bool pass, string name, object? data = null)
        {
            rows.Add(new { name, pass, data });
            if (!pass) throw new InvalidOperationException(name);
        }
        try
        {
            var combat = player.Creature.CombatState!;
            var room = (CombatRoom)player.RunState.CurrentRoom!;
            var target = combat.Creatures.First(c => c.IsEnemy && !c.IsDead);
            foreach (var scenario in new[] { "ordinary", "spent", "upgraded", "restored", "maximum", "combat-upgraded", "temporary" })
            {
                var upgraded = scenario == "upgraded";
                var definition = ModelDb.GetById<CardModel>(player.Deck.Cards.First().Id);
                var permanent = player.RunState.CreateCard(definition, player);
                if (upgraded) { permanent.UpgradeInternal(); permanent.FinalizeUpgradeInternal(); }
                await CardPileCmd.Add(permanent, PileType.Deck);
                if (scenario != "ordinary") Shine.SetState(permanent, scenario == "spent" ? 3 : 2, 7);
                Startup.Growth.Set(permanent, 23);
                var actual = CardCopies.ForCombat(permanent, player, CardCopyPurpose.Transfer, permanent);
                await CardPileCmd.Add(actual, PileType.Draw);
                if (scenario == "spent") await Shine.ResolveAsync(actual, () => Task.CompletedTask);
                if (scenario == "combat-upgraded") { actual.UpgradeInternal(); actual.FinalizeUpgradeInternal(); }
                if (scenario == "restored") Shine.RestoreToMaximum(actual);
                if (scenario == "maximum") Shine.ChangeMaximum(actual, 2);
                if (scenario == "temporary") Shine.GrantTemporary(actual, 4);
                var expected = Shine.GetState(scenario == "temporary" ? permanent : actual);
                await CardPileCmd.RemoveFromCombat(actual);
                var swipe = (SwipePower)ModelDb.Power<SwipePower>().ToMutable();
                await swipe.Steal(actual);
                await PowerCmd.Apply(new ThrowingPlayerChoiceContext(), swipe, target, 1m, target, null);
                Check(!player.Deck.Cards.Contains(permanent), "Steal removes permanent card", new { scenario });
                rows.Add(new { name = "Stolen and permanent states", pass = true, data = new { scenario, expected, permanent = Shine.GetState(permanent), stolen = Shine.GetState(actual) } });
                await swipe.BeforeDeath(target);
                var reward = room.ExtraRewards[player].OfType<SpecialCardReward>().Last();
                var saved = reward.ToSerializable();
                var returned = CardModel.FromSerializable(saved.SpecialCard!);
                Check(Shine.GetState(returned) == expected, "Return reward preserves Shine", new { scenario, expected, actual = Shine.GetState(returned) });
                var restored = Reward.FromSerializable(saved, player);
                Check(restored.GetType() == typeof(SpecialCardReward), "Returned reward retains native identity");
                var beforeClaim = player.Deck.Cards.ToHashSet();
                var selected = scenario == "upgraded" ? restored : reward;
                Check(await selected.SelectUnsynchronized(), "Return reward can be claimed");
                var claimed = player.Deck.Cards.Except(beforeClaim).Single();
                Check(Shine.GetState(claimed) == expected,
                    "Claim preserves original remaining and maximum", new { scenario, expected, actual = Shine.GetState(claimed) });
                Check(claimed.CurrentUpgradeLevel == (upgraded ? 1 : 0) && Startup.Growth.Get(claimed) == 23,
                    "Return preserves upgrade and growth");
            }
            _report = JsonSerializer.Serialize(new { passed = true, rows });
        }
        catch (Exception error) { _report = JsonSerializer.Serialize(new { passed = false, rows, error = error.ToString() }); }
    }
}
