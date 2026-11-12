using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using ShoujoKageki.Core.V1;
using ShoujoKageki.Core.V1.Presentation;
using ShoujoKagekiJunna.Combat;
using ShoujoKagekiJunna.Models;
using ShoujoKagekiAijoKaren.src.Core;
using ShoujoKagekiAijoKaren.src.Core.Models.Cards;
using ShoujoKagekiAijoKaren.src.Models.Cards;
using ShoujoKagekiAijoKaren.src.Core.Models.Cards.promisePile;
using ShoujoKagekiAijoKaren.src.Core.Models.Powers;
using ShoujoKagekiAijoKaren.src.Core.PromisePileSystem;
using ShoujoKagekiAijoKaren.src.Core.PromisePileSystem.Vfx;

namespace CoreReviewIntegration;

public sealed class RuntimeFlowsFixture : AbstractConsoleCmd
{
    private static string Report = "{}";
    private static CardModel? Restricted;
    private static Creature? Blocked;
    private static bool BlockAll;
    public override string CmdName => "runtime_flows_fixture";
    public override string Args => "run|report";
    public override string Description => "Directed deployment, target, preview and persistent-effect regression.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? player, string[] args) =>
        args.FirstOrDefault() == "report" ? new(true, Report) :
        player?.PlayerCombatState == null ? new(false, "Combat required") : new(Run(player), true, "running");

    private static void RestrictTarget(CardModel __instance, Creature __0, ref bool __result)
    {
        if (ReferenceEquals(__instance, Restricted) && (BlockAll || ReferenceEquals(__0, Blocked))) __result = false;
    }

    private static async Task Run(Player player)
    {
        var rows = new List<object>();
        var patcher = new Harmony("CoreReviewIntegration.RuntimeFlows");
        var context = new BlockingPlayerChoiceContext();
        var combat = player.Creature.CombatState!;
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
        async Task ExpectFailure(Func<Task> action, Exception expected)
        {
            try { await action(); }
            catch (Exception error) when (ReferenceEquals(error, expected)) { return; }
            throw new InvalidOperationException("Expected original deployment failure");
        }
        try
        {
            var karen = typeof(KarenFall);
            var junna = typeof(JunnaFullDraw);
            Check("both role models discovered", ReflectionHelper.ModTypes.Contains(karen) && ReflectionHelper.ModTypes.Contains(junna));
            Check("embedded and independent Core converge to one runtime",
                ModManager.Mods.Count(mod => mod.manifest?.id == CoreApi.ModId) == 2
                && ModManager.Mods.Count(mod => mod.manifest?.id == CoreApi.ModId && mod.state == ModLoadState.Loaded) == 1);
#if !STS2_BETA
            foreach (var (id, model) in new[] { ("ShoujoKagekiAijoKaren", karen), ("ShoujoKagekiJunna", junna) })
            {
                var loader = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == id);
                Check(id + " model bridge includes implementation",
                    ReflectionHelper.GetSubtypesFromAssembly(loader, typeof(CardModel)).Contains(model));
            }

#endif
            using var single = KnowledgeCombat.SingleCard();
            await Clear();
            var material = combat.CreateCard<DefendIronclad>(player);
            await CardPileCmd.Add(material, PileType.Hand);
            var failure = new InvalidOperationException("move-hook-failure");
            using (CoreApi.Get<ICardMovementPipeline>().Register(move =>
                ReferenceEquals(move.Card, material) && move.NewPile == ArrowPile.PileId
                    ? throw failure : Task.CompletedTask))
                await ExpectFailure(() => HandArrow.Play(context, material), failure);
            Check("ordinary deployment retains committed arrow after hook failure",
                material.Pile?.Type == ArrowPile.PileId && ArrowCombat.Find(material) != null && HandArrow.IsArrow(material));
            Check("ordinary deployment counted once", ArrowCombat.Find(material)!.PlayCount == 1);

            CardModel? copy = null;
            using (CoreApi.Get<ICardMovementPipeline>().Register(move =>
            {
                if (move.NewPile != ArrowPile.PileId || ReferenceEquals(move.Card, material)) return Task.CompletedTask;
                copy = move.Card;
                throw failure;
            }))
                await ExpectFailure(() => ArrowCombat.CopyAll(context, player), failure);
            Check("copied deployment retains committed arrow after hook failure",
                copy?.Pile?.Type == ArrowPile.PileId && ArrowCombat.Find(copy) != null && HandArrow.IsArrow(copy));
            Check("copy retains remaining turns and replay count",
                ArrowCombat.Find(copy!)!.Remaining == ArrowCombat.Find(material)!.Remaining && ArrowCombat.Find(copy!)!.PlayCount == 1);
            Check("one deployment per actual card", ArrowCombat.Deployed.Select(arrow => arrow.Card).Distinct().Count() == ArrowCombat.Deployed.Count);
            var originalDeployment = ArrowCombat.Find(material)!;
            long originalSequence = originalDeployment.Sequence;
            ArrowCombat.Deploy(material, originalDeployment.Play, originalDeployment.Remaining, (_, _) => Task.CompletedTask);
            Check("same-card redeployment merges repeats without changing sequence",
                ReferenceEquals(originalDeployment, ArrowCombat.Find(material)) && originalDeployment.PlayCount == 2
                && originalDeployment.Sequence == originalSequence);
            await ArrowCombat.CopyAll(context, player);
            Check("successful copies keep unique ordered deployments",
                ArrowCombat.Deployed.Count == 4 && ArrowCombat.Deployed.Select(arrow => arrow.Card).Distinct().Count() == 4
                && ArrowCombat.Deployed.Select(arrow => arrow.Sequence).SequenceEqual(new long[] { 0, 1, 2, 3 }));

            await Clear();
            material = combat.CreateCard<DefendIronclad>(player);
            await CardPileCmd.Add(material, PileType.Hand);
            var play = new CardPlay { Card = material, Target = player.Creature, PlayCount = 1,
                ResultPile = PileType.Discard, IsAutoPlay = true, PlayIndex = 0,
                Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
#if STS2_BETA
                Player = player,
#endif
            };
            var pending = new ArrowDeployment(material, play, 3, player.PlayerCombatState!.TurnNumber, (_, _) => Task.CompletedTask);
            var place = typeof(ArrowCombat).GetMethod("Place", BindingFlags.NonPublic | BindingFlags.Static)!;
            await ExpectFailure(() => (Task)place.Invoke(null, [pending, (Func<Task>)(() => Task.FromException(failure)), false])!, failure);
            Check("failed move clears provisional registration and preserves hand",
                material.Pile?.Type == PileType.Hand && ArrowCombat.Find(material) == null);

            var arrow = combat.CreateCard<JunnaFullDraw>(player);
            var enemy = combat.HittableEnemies.First();
            var arrowPlay = new CardPlay { Card = arrow, Target = enemy, PlayCount = 1,
                ResultPile = PileType.Discard, IsAutoPlay = true, PlayIndex = 0,
                Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
#if STS2_BETA
                Player = player,
#endif
            };
            ArrowCombat.Deploy(arrow, arrowPlay, 3, (_, _) => Task.CompletedTask);
            await CardPileCmd.Add(arrow, ArrowPile.PileId);
            var existing = ArrowCombat.Find(arrow);
            patcher.Patch(AccessTools.Method(typeof(CardModel), nameof(CardModel.IsValidTarget), [typeof(Creature)]),
                postfix: new HarmonyMethod(typeof(RuntimeFlowsFixture), nameof(RestrictTarget)));
            Restricted = arrow; BlockAll = true;
            Check("invalid paid target preserves original deployment",
                !await CardExecution.Play(context, arrow, new CardExecutionRequest { Target = enemy, Payment = CardPayment.Paid }) && ReferenceEquals(existing, ArrowCombat.Find(arrow)));
            await CardActions.FreePlay(context, arrow, enemy, keepTarget: true);
            Check("invalid retained target preserves original deployment", ReferenceEquals(existing, ArrowCombat.Find(arrow)));
            Restricted = combat.CreateCard<JunnaFullDraw>(player);
            await CardPileCmd.Add(Restricted, PileType.Hand);
            await HandArrow.Play(context, Restricted);
            Check("automatic play does not convert without a valid target",
                Restricted.Pile?.Type == PileType.Hand && ArrowCombat.Find(Restricted) == null);
            BlockAll = false; Blocked = enemy;
            var targetModule = typeof(CardActions).Assembly.GetType("ShoujoKagekiJunna.Combat.CardTargets", true)!;
            var candidates = (Creature[])targetModule.GetMethod("Enemies", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Restricted])!;
            Check("target candidates exclude card-specific restrictions", !candidates.Contains(enemy) && candidates.All(Restricted.IsValidTarget));
            if (candidates.Length > 0)
            {
                await HandArrow.Play(context, Restricted);
                Check("automatic deployment uses first valid target", ArrowCombat.Find(Restricted)?.Target == candidates[0]);
            }
            Restricted = null;
            patcher.UnpatchAll(patcher.Id);

            await Clear();
            await KarenFlowChecks.Run(player, Check);
            Report = JsonSerializer.Serialize(new { status = "done", passed = true, rows });
        }
        catch (Exception error)
        {
            Report = JsonSerializer.Serialize(new { status = "done", passed = false, rows, error = error.ToString() });
        }
        finally { Restricted = null; patcher.UnpatchAll(patcher.Id); }
    }
}
