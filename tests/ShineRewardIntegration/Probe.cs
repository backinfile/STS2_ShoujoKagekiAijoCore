using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Unlocks;
using ShoujoKageki.Core.V1;

namespace CoreShineRewardIntegration;

[MegaCrit.Sts2.Core.Modding.ModInitializer(nameof(Initialize))]
public static class ProbeStartup { public static void Initialize() { } }

public sealed class ShineRewardProbe : AbstractConsoleCmd
{
    private static string Report = "{}";
    private static CardPoolModel? HiddenPool;
    public override string CmdName => "shine_reward_fixture";
    public override string Args => "run|report|prepare|grant|inspect|prepare-rewards|show-rewards";
    public override string Description => "Directed Shine pool, reward and reproduction checks.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? player, string[] args) => args.FirstOrDefault() == "report" ? new(true, Report) :
        player == null ? new(false, "Run required") : args.FirstOrDefault() == "inspect" ? new(true, Inspect(player)) : player.PlayerCombatState == null ? new(false, "Combat required") :
        new(args.FirstOrDefault() == "show-rewards" ? ShowRewards(player) : args.FirstOrDefault() == "prepare-rewards" ? PrepareRewards(player) : args.FirstOrDefault() == "prepare" ? Prepare(player) : args.FirstOrDefault() == "grant" ? Grant(player) : Run(player), true, "running");
    private static bool HidePool(CardModel card, ref ShineState? __result)
    {
        if (HiddenPool == null || card.Pool != HiddenPool) return true;
        __result = null;
        return false;
    }
    private static async Task Run(Player player)
    {
        var rows = new List<object>();
        var patcher = new Harmony("CoreShineRewardIntegration.Pools");
        void Check(string name, bool passed)
        {
            rows.Add(new { name, passed });
            if (!passed) throw new InvalidOperationException(name);
        }
        Report = JsonSerializer.Serialize(new { status = "running" });
        try
        {
            var reproduction = ShineRewards.Reproduction;
            Check("reproduction preserves old model ID", reproduction.Id.Entry == "KAREN_SHINE_REPRODUCE");
            Check("reproduction belongs to Core", reproduction.GetType().Assembly.GetName().Name!.StartsWith("ShoujoKagekiAijoCore.Runtime"));
            Check("reproduction is a one-use token", reproduction.Rarity == CardRarity.Token && Shine.GetState(reproduction)?.Maximum == 1);
            var mutable = player.RunState.CreateCard(reproduction, player);
            var restored = CardModel.FromSerializable(mutable.ToSerializable());
            Check("old card ID round-trips through native serialization", restored.GetType() == reproduction.GetType() && restored.Id == reproduction.Id);
            var upgraded = (CardModel)mutable.MutableClone(); upgraded.UpgradeInternal();
            Check("upgrade retains zero cost", upgraded.EnergyCost.GetWithModifiers(CostModifiers.Local) == 0);
            var created = ShineRewards.Create(mutable, player);
            var loaded = Reward.FromSerializable(created.ToSerializable(), player);
            Check("native reward restores Core presentation", loaded.GetType() == created.GetType());
            Check("reward icon resolves without Karen", ResourceLoader.Exists(CoreApi.ResourceRoot + "images/powers/shine_reward_power.png"));
            Check("reproduction portrait resolves without Karen", ResourceLoader.Exists(CoreApi.ResourceRoot + "images/cards/shine_reproduce.png"));
            var discovery = CoreApi.Get<ICoreDiscovery>();
            CharacterModel? Character(string mod) => discovery.Models.Where(m => m.Kind == "character" && m.ModId == mod)
                .Select(m => ModelDb.GetById<CharacterModel>(ModelDb.GetId(m.ModelType))).FirstOrDefault();
            var junna = Character("ShoujoKagekiJunna")!;
            var karen = Character("ShoujoKagekiAijoKaren");
            Check("Junna models discovered", junna != null);
            var jp = Player.CreateForNewRun(junna!, UnlockState.all, 2001); jp.RunState = player.RunState;
            var jcards = ShineRewards.GetCandidates(jp);
            Check("Junna receives her own Shine pool", jcards.Count > 1 && jcards.Where(c => c.Id != reproduction.Id).All(c => c.Pool == junna!.CardPool));
            Check("Junna receives shared reproduction", jcards.Count(c => c.Id == reproduction.Id) == 1);
            var first = jcards.First(c => c.Id != reproduction.Id);
            Check("model exclusion is honored", ShineRewards.GetCandidates(jp, first).All(c => c.Id != first.Id));
            Check("reproduction exclusion is honored", ShineRewards.GetCandidates(jp, reproduction).All(c => c.Id != reproduction.Id));
            Check("basic cards stay excluded", jcards.All(c => c.Rarity != CardRarity.Basic));
            var covered = new HashSet<int>();
            for (uint seed = 0; seed < 4000 && covered.Count < 100; seed++)
            {
                var expectedRng = new Rng(seed); var roll = expectedRng.NextInt(100);
                if (!covered.Add(roll)) continue;
                var selected = ShineRewards.Roll(jp, new Rng(seed));
                var expected = jcards.Where(c => roll < 42 ? c.Rarity == CardRarity.Common : roll < 84 ? c.Rarity == CardRarity.Uncommon : roll < 99 ? c.Rarity == CardRarity.Rare : c.Id == reproduction.Id).ToArray();
                if (!(expected.Length == 0 ? jcards.Contains(selected) : expected.Contains(selected))) throw new InvalidOperationException("Weighted boundary " + roll);
            }
            Check("all 100 weighted outcomes follow the selected pool", covered.Count == 100);
            var ironclad = Player.CreateForNewRun<Ironclad>(UnlockState.all, 2002); ironclad.RunState = player.RunState;
            var fallback = ShineRewards.GetCandidates(ironclad);
            Check("non-Shoujo uses Karen fallback or shared-only", karen == null ? fallback.Count == 1 && fallback[0].Id == reproduction.Id : fallback.Count > 1 && fallback.Where(c => c.Id != reproduction.Id).All(c => c.Pool == karen.CardPool));
            if (karen != null)
            {
                var kp = Player.CreateForNewRun(karen, UnlockState.all, 2003); kp.RunState = player.RunState;
                Check("Karen keeps her own pool", ShineRewards.GetCandidates(kp).Where(c => c.Id != reproduction.Id).All(c => c.Pool == karen.CardPool));
                var restricted = new[] { "KAREN_STAR_FRIEND", "KAREN_SUNLIGHT", "KAREN_REWRITE", "KAREN_NO_HESITATE", "KAREN_FIGHT_RELAY" };
                Check("fallback preserves Karen sharing restrictions", fallback.All(c => !restricted.Contains(c.Id.Entry)));
            }
            var stateMethod = AccessTools.Method(CoreApi.Get<IShineService>().GetType(), nameof(IShineService.GetState));
            patcher.Patch(stateMethod, prefix: new HarmonyMethod(typeof(ShineRewardProbe), nameof(HidePool)));
            HiddenPool = junna!.CardPool;
            var empty = ShineRewards.GetCandidates(jp);
            Check("empty Shoujo pool falls back before adding reproduction", karen == null ? empty.Count == 1 && empty[0].Id == reproduction.Id : empty.Count > 1 && empty.Where(c => c.Id != reproduction.Id).All(c => c.Pool == karen.CardPool));
            HiddenPool = null; patcher.UnpatchAll(patcher.Id);
            if (karen == null)
            {
                try { ShineRewards.Roll(ironclad, new Rng(0), reproduction); Check("empty excluded pool fails explicitly", false); }
                catch (InvalidOperationException) { Check("empty excluded pool fails explicitly", true); }
            }
            var marker = player.Creature.Powers.FirstOrDefault(p => p.Id.Entry == "KAREN_SHINE_CARD_REWARD_POWER")?.Amount ?? 0;
            await ShineRewards.Add(player);
            Check("combat reward creates shared marker", player.Creature.Powers.FirstOrDefault(p => p.Id.Entry == "KAREN_SHINE_CARD_REWARD_POWER")?.Amount == marker + 1);
            Report = JsonSerializer.Serialize(new { status = "done", passed = true, rows, candidates = jcards.Select(c => c.Id.ToString()), fallback = fallback.Select(c => c.Id.ToString()) });
        }
        catch (Exception error) { Report = JsonSerializer.Serialize(new { status = "done", passed = false, rows, error = error.ToString() }); }
        finally { HiddenPool = null; patcher.UnpatchAll(patcher.Id); }
    }
    private static async Task Prepare(Player player)
    {
        Report = "{\"status\":\"preparing\"}";
        try
        {
        foreach (var card in player.PlayerCombatState!.AllCards.ToArray()) await CardPileCmd.RemoveFromCombat(card);
        var source = ShineRewards.GetCandidates(player).First(c => c.Id.Entry == "JUNNA_JS35");
        var permanent = player.RunState.CreateCard(source, player); permanent.UpgradeInternal(); await CardPileCmd.Add(permanent, PileType.Deck);
        var combat = CardCopies.ForCombat(permanent, player, CardCopyPurpose.Transfer, permanent);
        await CardPileCmd.AddGeneratedCardToCombat(combat, PileType.Hand, player);
        Shine.SetState(permanent, 1, Shine.GetState(permanent)!.Maximum);
        Shine.SetState(combat, 1, Shine.GetState(combat)!.Maximum);
        var reproduce = player.RunState.CreateCard(ShineRewards.Reproduction, player); await CardPileCmd.Add(reproduce, PileType.Deck);
        await CardPileCmd.AddGeneratedCardToCombat(CardCopies.ForCombat(reproduce, player, CardCopyPurpose.Transfer, reproduce), PileType.Hand, player);
        Report = "{\"status\":\"prepared\"}";
        }
        catch (Exception error) { Report = JsonSerializer.Serialize(new { status = "prepare-failed", error = error.ToString() }); }
    }
    private static async Task PrepareRewards(Player player)
    {
        Report = "{\"status\":\"preparing\"}";
        try
        {
            foreach (var card in player.PlayerCombatState!.AllCards.ToArray()) await CardPileCmd.RemoveFromCombat(card);
            foreach (var enemy in player.Creature.CombatState!.HittableEnemies) enemy.SetCurrentHpInternal(1);
            await ShineRewards.Add(player);
            await ShineRewards.Add(player);
            await CardPileCmd.AddGeneratedCardToCombat(player.Creature.CombatState.CreateCard<MegaCrit.Sts2.Core.Models.Cards.Whirlwind>(player), PileType.Hand, player);
            Report = "{\"status\":\"prepared\"}";
        }
        catch (Exception error) { Report = JsonSerializer.Serialize(new { status = "prepare-failed", error = error.ToString() }); }
    }
    private static string Inspect(Player player) => JsonSerializer.Serialize(new
    {
        deck = player.Deck.Cards.Select(card => new { id = card.Id.Entry, upgrades = card.CurrentUpgradeLevel, state = Shine.GetState(card) }),
        hand = player.PlayerCombatState?.Hand.Cards.Select(card => new { id = card.Id.Entry, upgrades = card.CurrentUpgradeLevel, state = Shine.GetState(card) }),
        archive = Shine.GetDepletedCards(player),
        rewards = player.RunState.CurrentRoom is CombatRoom room && room.ExtraRewards.TryGetValue(player, out var rewards) ? rewards.Count : 0
    });
    private static Task ShowRewards(Player player) => RewardsCmd.OfferCustom(player, Enumerable.Range(0, 2)
        .Select(_ => ShineRewards.Create(player.RunState.CreateCard(ShineRewards.Roll(player, player.PlayerRng.Rewards), player), player)).ToList());
    private static Task Grant(Player player) => ShineRewards.Add(player);
}
