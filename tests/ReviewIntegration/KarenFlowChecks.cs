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
using ShoujoKagekiAijoKaren.src.Core;
using ShoujoKagekiAijoKaren.src.Core.Models.Cards;
using ShoujoKagekiAijoKaren.src.Models.Cards;
using ShoujoKagekiAijoKaren.src.Core.Models.Cards.promisePile;
using ShoujoKagekiAijoKaren.src.Core.Models.Powers;
using ShoujoKagekiAijoKaren.src.Core.PromisePileSystem;
using ShoujoKagekiAijoKaren.src.Core.PromisePileSystem.Vfx;

namespace CoreReviewIntegration;

internal static class KarenFlowChecks
{
    internal static async Task Run(Player player, Action<string, bool> Check)
    {
        var context = new BlockingPlayerChoiceContext();
        var combat = player.Creature.CombatState!;
        var tree = (SceneTree)Engine.GetMainLoop();
        async Task Frame() { await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame); }
            await PromisePileManager.EnterMode(player, PromisePileMode.None);
            await PowerCmd.Apply<KarenNewDayPower>(context, player.Creature, 3, player.Creature, null);
            var newDay = player.Creature.GetPower<KarenNewDayPower>()!;
            var normal = combat.CreateCard<DefendIronclad>(player);
            var skip = combat.CreateCard<KarenNewDay>(player);
            var preview = typeof(KarenFall).Assembly.GetType("ShoujoKagekiAijoKaren.src.Core.PromisePileSystem.Vfx.PromiseTransferPreviews", true)!
                .GetMethod("ForCard", BindingFlags.Public | BindingFlags.Static)!;
            foreach (var card in new CardModel[] { normal, skip, combat.CreateCard<KarenFall>(player) })
            {
                await CardPileCmd.Add(card, PileType.Hand);
                card.ExhaustOnNextPlay = true;
                var before = card.ToSerializable();
                string json = JsonSerializer.Serialize(before, MegaCrit.Sts2.Core.Saves.JsonSerializationUtility.GetTypeInfo<MegaCrit.Sts2.Core.Saves.Runs.SerializableCard>());
                preview.Invoke(null, [card]);
                Check(card.GetType().Name + " preview is read-only",
                    card.ExhaustOnNextPlay && newDay.Amount == 3 && json == JsonSerializer.Serialize(card.ToSerializable(), MegaCrit.Sts2.Core.Saves.JsonSerializationUtility.GetTypeInfo<MegaCrit.Sts2.Core.Saves.Runs.SerializableCard>()));
                card.ExhaustOnNextPlay = false;
            }
            Check("ordinary card preview includes New Day", preview.Invoke(null, [normal])!.ToString() == "Put");
            Check("skip card keeps its own transfer preview", preview.Invoke(null, [skip])!.ToString() == "Put");
#if STS2_BETA
            var redirected = newDay.ModifyCardPlayResultLocation(normal, false, new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
                new CardLocation(player, PileType.Discard, CardPilePosition.Bottom));
            var skipped = newDay.ModifyCardPlayResultLocation(skip, false, new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
                new CardLocation(player, PileType.Discard, CardPilePosition.Bottom));
            Check("native New Day direction and skip rule preserved",
                redirected.pileType == KarenCustomEnum.PromisePile && skipped.pileType == PileType.Discard);
#else
            var redirected = newDay.ModifyCardPlayResultPileTypeAndPosition(normal, false, new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 }, PileType.Discard, CardPilePosition.Bottom);
            var skipped = newDay.ModifyCardPlayResultPileTypeAndPosition(skip, false, new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 }, PileType.Discard, CardPilePosition.Bottom);
            Check("native New Day direction and skip rule preserved",
                redirected.Item1 == KarenCustomEnum.PromisePile && skipped.Item1 == PileType.Discard);
#endif
            Check("native New Day still executes", await CardExecution.Play(context, normal, new CardExecutionRequest()));
            Check("New Day keeps actual transfer and consumption",
                normal.Pile?.Type == KarenCustomEnum.PromisePile && newDay.Amount == 2);

            await PowerCmd.Apply<KarenFormPower>(context, player.Creature, 1, player.Creature, null);
            KarenFormVfxManager.Start(player); KarenFormVfxManager.Start(player);
            KarenPastAndFutureRingVfxManager.Start(player); KarenPastAndFutureRingVfxManager.Start(player);
            await Frame(); await Frame();
            var creature = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
            int Forms() => creature.GetChildren().OfType<NKarenFormVfx>().Count(node => !node.IsQueuedForDeletion());
            int Rings() => creature.GetParent().GetChildren().OfType<NKarenPastAndFutureRingVfx>().Count(node => node.BelongsTo(creature) && !node.IsQueuedForDeletion());
            Check("repeated starts reuse one persistent node", Forms() == 1 && Rings() == 1);
            KarenFormVfxManager.PulseDraw(player); KarenPastAndFutureRingVfxManager.Pulse(player);
            KarenFormVfxManager.Stop(player); KarenPastAndFutureRingVfxManager.Stop(player);
            KarenFormVfxManager.Start(player); KarenPastAndFutureRingVfxManager.Start(player);
            await Frame(); await Frame();
            Check("same-frame stop and restart reuse nodes", Forms() == 1 && Rings() == 1);
            KarenFormVfxManager.Stop(player); KarenPastAndFutureRingVfxManager.Stop(player);
            await tree.ToSignal(tree.CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
            Check("persistent nodes finish their original fade and release", Forms() == 0 && Rings() == 0);
            await CoreEffects.DrawFrame(NCombatRoom.Instance!);
    }
}
