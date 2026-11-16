using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using ShoujoKageki.Core.V1;

namespace CorePersistenceProbe;

[ModInitializer(nameof(Initialize))]
public static class Startup
{
    internal static readonly SavedSpireField<CardModel, int> Growth = new(() => 0, "core_sl_probe_growth");
    public static void Initialize()
    {
        _ = Growth;
        ((Godot.SceneTree)Godot.Engine.GetMainLoop()).ProcessFrame += () => Godot.RenderingServer.ForceDraw(false);
    }
}

public sealed class PersistenceCommand : AbstractConsoleCmd
{
    private static string _report = "idle";
    public override string CmdName => "core_sl";
    public override string Args => "seed | roundtrip | verify | view | close-view | status | exit";
    public override string Description => "Compare complete persisted player content across JSON and process restarts.";
    public override bool IsNetworked => false;
    private static Type Archive => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("ShoujoKageki.Core.Runtime.Modules.Shine.ShineArchive")).First(t => t != null)!;
    private static string ExpectedPath => Environment.GetEnvironmentVariable("CORE_SL_EXPECTED")
        ?? throw new InvalidOperationException("Set CORE_SL_EXPECTED inside the isolated test directory.");
    private static object Rules(Player player) => Archive.GetMethod("GetRules")!.Invoke(null, [player])!;
    private static string CardJson(CardModel card) => JsonSerializer.Serialize(card.ToSerializable(), JsonSerializationUtility.GetTypeInfo<SerializableCard>());
    private static JsonNode Capture(Player player) => JsonSerializer.SerializeToNode(new
    {
        deck = player.Deck.Cards.Select(c => new { card = JsonNode.Parse(CardJson(c)), shine = Shine.GetState(c), growth = Startup.Growth.Get(c) }),
        archive = Shine.GetDepletedModels(player).Select(c => new { card = JsonNode.Parse(CardJson(c)), shine = Shine.GetState(c), growth = Startup.Growth.Get(c) }),
        entries = Shine.GetDepletedCards(player), rules = Rules(player)
    })!;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Compare(JsonNode expected, Player player)
    {
        var actual = Capture(player);
        foreach (var key in new[] { "archive", "entries", "deck", "rules" })
            Check(JsonNode.DeepEquals(expected[key], actual[key]), $"SL changed {key}: expected {expected[key]}, actual {actual[key]}");
        var archived = Shine.GetDepletedModels(player).Last();
        Check(Shine.Copy(archived, CardCopyPurpose.TemporaryCopy).Id != archived.Id, "Archived ordinary copy must remain a shell after SL.");
        var reacquired = Shine.Reacquire(player, Shine.GetDepletedCards(player).Last());
        Check(reacquired.Id == archived.Id && reacquired.CurrentUpgradeLevel == archived.CurrentUpgradeLevel
            && Startup.Growth.Get(reacquired) == 41 && Shine.GetState(reacquired) is { Remaining: 7, Maximum: 7, Temporary: false }, "Reacquire must start a full lifecycle after SL.");
    }
    public override CmdResult Process(Player? player, string[] args)
    {
        if (args.FirstOrDefault() == "status") return new(true, _report);
        if (player == null) return new(false, "Run required.");
        _report = "running";
        return new(Run(player, args.FirstOrDefault() ?? "roundtrip"), true, "SL check started.");
    }
    private static async Task Run(Player player, string action)
    {
        try
        {
            if (action == "seed")
            {
                CoreApi.Configuration.Update(CoreApi.Configuration.Current with { ShinePileButton = ShinePileButtonMode.Always });
                Check(Shine.GetDepletedCards(player).Count == 0, "Seed requires a fresh isolated run.");
                var definition = ModelDb.GetById<CardModel>(player.Deck.Cards.First().Id);
                var charged = player.RunState.CreateCard(definition, player);
                charged.UpgradeInternal(); charged.FinalizeUpgradeInternal();
                Startup.Growth.Set(charged, 23);
                await CardPileCmd.Add(charged, PileType.Deck);
                Shine.SetState(charged, 2, 7);
                var spent = player.RunState.CreateCard(definition, player);
                spent.UpgradeInternal(); spent.FinalizeUpgradeInternal();
                Startup.Growth.Set(spent, 41);
                await CardPileCmd.Add(spent, PileType.Deck);
                Shine.SetState(spent, 1, 7);
                await Shine.ResolveAsync(spent, () => Task.CompletedTask, CardResolutionKind.Arrow);
                Check(Shine.GetDepletedCards(player).Count == 1, "Seed must really deplete a permanent card.");
                var shell = Shine.Copy(Shine.GetDepletedModels(player).Single(), CardCopyPurpose.Transfer);
                shell = player.RunState.CreateCard(ModelDb.GetById<CardModel>(shell.Id), player);
                await CardPileCmd.Add(shell, PileType.Deck);
                var rules = JsonSerializer.SerializeToNode(Rules(player))!;
                rules["BookOfFiveRingsPatch"] = 0; rules["LuckyFyshPatch"] = 2;
                Archive.GetMethod("SetRules")!.Invoke(null, [player, rules.Deserialize(Rules(player).GetType())!]);
                Check(player.Deck.Cards.Any(c => Shine.GetState(c) == null), "Ordinary cards must be in the fixture.");
                File.WriteAllText(ExpectedPath, Capture(player).ToJsonString());
            }
            else if (action == "roundtrip")
            {
                var expected = Capture(player);
                Check(Shine.GetDepletedCards(player).Count > 0, "Seed before roundtrip.");
                var json = JsonSerializer.Serialize(RunManager.Instance.ToSave(null), JsonSerializationUtility.GetTypeInfo<SerializableRun>());
                var save = JsonSerializer.Deserialize(json, JsonSerializationUtility.GetTypeInfo<SerializableRun>())!;
                var restored = RunState.FromSerializable(save).Players.Single(p => p.NetId == player.NetId);
                Compare(expected, restored);
                Compare(expected, RunState.FromSerializable(save).Players.Single(p => p.NetId == player.NetId));
                restored.SyncWithSerializedPlayer(save.Players.Single(p => p.NetId == player.NetId));
                Compare(expected, restored);
            }
            else if (action == "verify") Compare(JsonNode.Parse(File.ReadAllText(ExpectedPath))!, player);
            else if (action == "view")
            {
                static IEnumerable<Godot.Node> Descendants(Godot.Node node)
                {
                    foreach (var child in node.GetChildren())
                    {
                        yield return child;
                        foreach (var descendant in Descendants(child)) yield return descendant;
                    }
                }
                var tree = (Godot.SceneTree)Godot.Engine.GetMainLoop();
                var button = Descendants(tree.Root).OfType<Godot.Control>().Single(n => n.GetType().Name == "NTopBarShinePileButton");
                var count = (Godot.Label)button.GetNode("ShinePileCardCount");
                Check(button.IsVisibleInTree() && count.Text == Shine.GetDepletedCards(player).Count.ToString(), "SL must restore the visible pile button and its count.");
                var screen = CoreApi.Get<IShinePileView>().Show(player);
                for (int i = 0; i < 30; i++) await tree.ToSignal(tree, Godot.SceneTree.SignalName.ProcessFrame);
                var visible = Descendants(screen).OfType<NCard>().Select(n => n.Model).ToArray();
                Check(visible.Length == Shine.GetDepletedCards(player).Count && visible.All(c => c != null && c.Owner == player && Shine.GetState(c)?.Remaining == 0), "SL must restore visible depleted cards for the current player.");
            }
            else if (action == "close-view") NCapstoneContainer.Instance!.Close();
            else if (action == "exit")
            {
                await SaveManager.Instance.SaveRun(null);
                Check(SaveManager.Instance.HasRunSave, "Save and quit must create an on-disk run.");
                var exit = typeof(NGame).GetMethod("ReturnToMainMenu", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
                var values = exit.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : throw new InvalidOperationException(p.Name)).ToArray();
                if (exit.Invoke(NGame.Instance, values) is Task pending) await pending;
            }
            else throw new ArgumentException("Unknown SL action: " + action);
            _report = JsonSerializer.Serialize(new { passed = true, action });
        }
        catch (Exception error) { _report = JsonSerializer.Serialize(new { passed = false, action, error = error.ToString() }); }
    }
}
