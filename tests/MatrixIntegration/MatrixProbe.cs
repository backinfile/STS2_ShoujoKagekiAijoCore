using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using BaseLib.Patches.Content;
using BaseLib.Patches.Saves;
using CoreRoleIntegration;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Saves;
using ShoujoKageki.Core.V1;
using ShoujoKagekiJunna.Combat;
using ShoujoKagekiAijoKaren.src.Core.PromisePileSystem;

namespace CoreMatrixIntegration;

[ModInitializer(nameof(Initialize))]
public static class MatrixStartup
{
    public static void Initialize() => new Harmony("CoreMatrixIntegration").PatchAll();
}
// Test-only restriction: never request firewall rules or expose the test transport.
[HarmonyPatch(typeof(ENetConnection), nameof(ENetConnection.CreateHostBound))]
internal static class LoopbackHost
{
    private static void Prefix(ref string __0) => __0 = "127.0.0.1";
}
[HarmonyPatch(typeof(ENetConnection), nameof(ENetConnection.CreateHost))]
internal static class LoopbackClient
{
    private static bool Prefix(ENetConnection __instance, int __0, int __1, int __2, int __3, ref Error __result)
    {
        __result = __instance.CreateHostBound("127.0.0.1", 0, __0, __1, __2, __3); return false;
    }
}

// Extend the local MCP test surface with native console dispatch for networked fixtures.
// The shipped MCP currently exposes console commands only on its solo route.
[HarmonyPatch]
internal static class MultiplayerConsoleBridge
{
    private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("KarenSTS2MCP.McpMod"), "ExecuteMultiplayerAction");
    private static bool Prefix(string action, Dictionary<string, JsonElement> data, ref Dictionary<string, object?> __result)
    {
        if (action != "run_command") return true;
        var result = new DevConsole(true).ProcessCommand(data["command"].GetString()!);
        __result = new() { ["status"] = result.success ? "ok" : "error", ["message"] = result.msg, ["async"] = result.task != null };
        return false;
    }
}

public sealed class MatrixFixture : AbstractConsoleCmd
{
    internal static string Report = "idle";
    public override string CmdName => "matrix_fixture";
    public override string Args => "rules";
    public override string Description => "Networked shared-service rule and save verification.";
    public override bool IsNetworked => true;
    public override CmdResult Process(Player? player, string[] args) => player == null ? new(false, "Player required") : new(Run(player), true, "running");
    private static async Task Run(Player player)
    {
        var rows = new List<object>();
        void Check(string name, bool pass) { rows.Add(new { name, pass }); if (!pass) throw new InvalidOperationException(name); }
        Report = JsonSerializer.Serialize(new { status = "running", actor = player.NetId });
        try
        {
            Check("one Core runtime", AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name?.StartsWith("ShoujoKagekiAijoCore.Runtime.") == true) == 1);
            Check("no None custom pile", !CustomPiles.CustomPileProviders.ContainsKey(PileType.None));
            Check("one promise pile in first native collection", player.PlayerCombatState!.AllPiles.Count(p => p.Type == PromisePileManager.GetPromisePile(player).Type) == 1);
            await CoreRoleCommand.Run(player);
            using (var report = JsonDocument.Parse(CoreRoleCommand.LastResult))
                Check("native role resolution suite", report.RootElement.GetProperty("passed").GetBoolean());

            var state = player.Creature.CombatState!;
            var source = player.RunState.CreateCard(ModelDb.Card<ResolutionProbeCard>(), player);
            source.UpgradeInternal(); source.FinalizeUpgradeInternal();
            Shine.SetState(source, 1, 7);
            await CardPileCmd.Add(source, PileType.Deck);
            foreach (var recipient in state.Players)
            {
                var temporary = CardCopies.ForCombat(source, recipient, CardCopyPurpose.TemporaryCopy);
                Check("combat copy belongs to recipient without changing source", temporary.Owner == recipient && source.Owner == player && state.ContainsCard(temporary));
                Check("combat copy preserves extended values", temporary.CurrentUpgradeLevel == 1 && Shine.GetState(temporary) is { Remaining: 1, Maximum: 7, Temporary: true });
                temporary.RemoveFromState();
            }
            var actual = CardCopies.ForCombat(source, player, CardCopyPurpose.Transfer, source);
            await CardPileCmd.Add(actual, PileType.Hand);
            int count = Shine.GetDepletedCards(player).Count;
            rows.Add(new { name = "permanent copy before play", pass = true, state = Shine.GetState(actual), sourceState = Shine.GetState(source), inDeck = player.Deck.Cards.Contains(source), sameSource = actual.DeckVersion == source });
            await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), actual, player.Creature);
            rows.Add(new { name = "permanent copy after play", pass = true, state = Shine.GetState(actual), sourceState = Shine.GetState(source), archiveBefore = count, archiveAfter = Shine.GetDepletedCards(player).Count, inDeck = player.Deck.Cards.Contains(source), pile = actual.Pile?.Type });
            Check("permanent source archived once", Shine.GetDepletedCards(player).Count == count + 1 && !player.Deck.Cards.Contains(source));
            var archived = Shine.GetDepletedModels(player).Last();
            Check("archive preserves upgrade and maximum", archived.CurrentUpgradeLevel == 1 && Shine.GetState(archived)?.Maximum == 7);
            Check("ordinary archived copy is shell", CardCopies.Preview(archived, CardCopyPurpose.TemporaryCopy).Id != archived.Id);
            foreach (var preview in Enumerable.Range(0, 32).Select(_ => CardCopies.Preview(archived, CardCopyPurpose.Reacquire)))
                Check("preview remains detached", !state.ContainsCard(preview) && preview.Pile == null);
            var acquired = await CardCopies.Acquire(archived, player, addToHand: false);
            Check("explicit acquire begins full permanent lifecycle", acquired.success && Shine.GetState(acquired.cardAdded) is { Remaining: 7, Maximum: 7, Temporary: false });

            var move = state.CreateCard(ModelDb.Card<ResolutionProbeCard>(), player);
            var transitions = new List<CardMovement>();
            using (CoreApi.Get<ICardMovementPipeline>().Register(async movement => { await Task.Yield(); if (movement.Card == move) transitions.Add(movement); }))
            {
                await CardPileCmd.Add(move, PileType.Hand);
                await CardPileCmd.Add(move, PromisePileManager.GetPromisePile(player));
                await CardPileCmd.Add(move, PileType.Discard);
            }
            Check("awaited movement reaches every registered pile", transitions.Count == 3 && transitions[1].NewPile == PromisePileManager.GetPromisePile(player).Type && transitions[2].OldPile == transitions[1].NewPile);
            await CardPileCmd.RemoveFromCombat(move);

            var save = RunManager.Instance.ToSave(null);
            var writer = new PacketWriter(); writer.Write(RunManager.CanonicalizeSave(save, player.NetId));
            var reader = new PacketReader(); reader.Reset(writer.Buffer);
            var loaded = RunState.FromSerializable(reader.Read<SerializableRun>());
            var restored = loaded.Players.Single(p => p.NetId == player.NetId);
            Check("native network archive roundtrip", Shine.GetDepletedCards(restored).Count == Shine.GetDepletedCards(player).Count);
            Check("native network extended card roundtrip", Shine.GetDepletedModels(restored).Last() is { CurrentUpgradeLevel: 1 } saved && Shine.GetState(saved)?.Maximum == 7);
            restored.SyncWithSerializedPlayer(player.ToSerializable());
            Check("native player synchronization uses registered data", Shine.GetDepletedCards(restored).Count == Shine.GetDepletedCards(player).Count);

            var serializedPlayer = save.Players.Single(p => p.NetId == player.NetId);
            const string id = "ShoujoKagekiAijoCore.player-state@1";
            var data = ExtendedSaveHandlers<Player, SerializablePlayer>.ExtendedData[serializedPlayer].DictForType<string>();
            string original = data[id]; var bad = JsonNode.Parse(original)!; bad["Rules"]!["NetworkFingerprint"] = "matrix-wrong-fingerprint"; data[id] = bad.ToJsonString();
            bool rejected = false;
            try { var invalid = new PacketWriter(); invalid.Write(serializedPlayer); var input = new PacketReader(); input.Reset(invalid.Buffer); input.Read<SerializablePlayer>(); }
            catch (InvalidDataException) { rejected = true; }
            finally { data[id] = original; }
            Check("different Core fingerprint is rejected", rejected);
            Report = JsonSerializer.Serialize(new { status = "done", actor = player.NetId, passed = true, rows });
        }
        catch (Exception error) { Report = JsonSerializer.Serialize(new { status = "done", actor = player.NetId, passed = false, rows, error = error.ToString() }); }
    }
}

public sealed class MatrixLocal : AbstractConsoleCmd
{
    public override string CmdName => "matrix_local";
    public override string Args => "status | snapshot | quality Full/Reduced/Off";
    public override string Description => "Read matrix evidence and configure local presentation.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? player, string[] args)
    {
        if (args.FirstOrDefault() == "status") return new(true, MatrixFixture.Report);
        if (args.FirstOrDefault() == "quality")
        {
            CoreApi.Configuration.Update(CoreApi.Configuration.Current with { VfxLevel = Enum.Parse<CoreVfxLevel>(args[1]) });
            return new(true, CoreApi.Configuration.Current.VfxLevel.ToString());
        }
        var run = RunManager.Instance.DebugOnlyGetState();
        return new(true, JsonSerializer.Serialize(new
        {
            players = run?.Players.OrderBy(p => p.NetId).Select(p => new
            {
                id = p.NetId, character = p.Character.Id.Entry, hp = p.Creature.CurrentHp, block = p.Creature.Block,
                energy = p.PlayerCombatState?.Energy,
                powers = p.Creature.Powers.Select(power => new { id = power.Id.Entry, power.Amount }),
                relics = p.Relics.Select(relic => relic.Id.Entry),
                promiseType = (int)PromisePileManager.GetPromisePile(p).Type,
                deck = p.Deck.Cards.Select(c => new { id = c.Id.Entry, upgrade = c.CurrentUpgradeLevel, shine = Shine.GetState(c) }),
                piles = p.PlayerCombatState?.AllPiles.OrderBy(pile => (int)pile.Type).Select(pile => new { type = (int)pile.Type, cards = pile.Cards.Select(c => new { id = c.Id.Entry, upgrade = c.CurrentUpgradeLevel, shine = Shine.GetState(c) }) }),
                archive = Shine.GetDepletedCards(p)
            }),
            rng = RunManager.Instance.ToSave(null).SerializableRng,
            enemies = player?.Creature.CombatState?.Enemies.Select(c => new { id = c.Monster?.Id.Entry, c.CurrentHp, c.Block, powers = c.Powers.Select(p => new { id = p.Id.Entry, p.Amount }) }),
            arrows = ArrowCombat.ResolutionOrder().Select(a => new { card = a.Card.Id.Entry, owner = a.Card.Owner.NetId, a.Remaining, a.PlayCount, a.Sequence })
        }));
    }
}
