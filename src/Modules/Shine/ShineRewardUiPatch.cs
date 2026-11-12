using System;
using System.Reflection.Emit;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Runs;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Nodes.Screens;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Saves.Runs;


using ShoujoKageki.Core.V1;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

[HarmonyPatch(typeof(NRewardButton), nameof(NRewardButton._Ready))]
internal static class KarenShineRewardUiPatch
{
    private static void Postfix(NRewardButton __instance, TextureRect ____background)
    {
        if (__instance.Reward is not (CoreShineCardReward or CoreShineCardRewardChoice)) return;
        ____background.SelfModulate = new Color("#fff0d0");
        var stars = new NCoreShineRewardVfx { Name = "KarenShineRewardStars" };
        __instance.AddChild(stars);
        stars.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }
}

[HarmonyPatch(typeof(NClickableControl), nameof(NClickableControl._GuiInput))]
internal static class KarenShineRewardInspectPatch
{
    private static bool Prefix(NClickableControl __instance, InputEvent inputEvent)
    {
        if (__instance is not NRewardButton button ||
            !button.IsEnabled || !button.IsVisibleInTree()) return true;

        var cards = button.Reward switch
        {
            CoreShineCardRewardChoice choice => choice.Cards,
            CoreShineCardReward single => new[] { single.Card },
            _ => null
        };
        if (cards == null) return true;
        bool rightClick = inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Right };
        if (!rightClick) return true;
        if (inputEvent.IsPressed() && NGame.Instance?.GetInspectCardScreen() is { Visible: false } screen)
        {
            NHoverTipSet.Remove(button);
            screen.Open(cards.ToList(), 0);
        }
        button.AcceptEvent();
        return false;
    }
}

// Native SpecialCard serialization already preserves the complete card and multiplayer payload.
// Reapply the presentation after loading, including rewards saved before this UI existed.
[HarmonyPatch(typeof(Reward), nameof(Reward.FromSerializable))]
internal static class KarenShineRewardRestorePatch
{
    private static readonly AccessTools.FieldRef<SpecialCardReward, CardModel> CardField =
        AccessTools.FieldRefAccess<SpecialCardReward, CardModel>("_card");

    private static void Postfix(SerializableReward save, Player player, ref Reward __result)
    {
        if (__result is not SpecialCardReward special || __result is CoreShineCardReward ||
            save.CustomDescriptionEncounterSourceId != ModelId.none) return;
        var card = CardField(special);
        if (card != null && V1.Shine.IsShineCard(card)) __result = new CoreShineCardReward(card, player);
    }
}

// Group only the display list; the room and synchronizer keep every original reward.
[HarmonyPatch(typeof(NRewardsScreen), nameof(NRewardsScreen._Ready))]
internal static class KarenShineRewardGroupPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var toList = AccessTools.Method(typeof(Enumerable), nameof(Enumerable.ToList), generics: [typeof(Reward)]);
        var group = AccessTools.Method(typeof(KarenShineRewardGroupPatch), nameof(GroupForDisplay));
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (!instruction.Calls(toList)) continue;
            yield return new CodeInstruction(OpCodes.Call, group);
            replaced++;
        }
        if (replaced != 1) throw new InvalidOperationException("Expected one reward display list.");
    }

    private static List<Reward> GroupForDisplay(List<Reward> list)
    {
        var groups = list.OfType<CoreShineCardReward>()
            .Where(reward => reward.ParentRewardSet == null).GroupBy(reward => reward.Player).ToList();
        foreach (var group in groups)
        {
            var cards = group.ToList();
            int index = list.IndexOf(cards[0]);
            list.RemoveAll(reward => reward is CoreShineCardReward shine && cards.Contains(shine));
            list.Insert(index, new CoreShineCardRewardChoice(cards));
        }
        return list;
    }
}

[HarmonyPatch(typeof(NRewardButton), "GetReward")]
internal static class KarenShineRewardClaimPatch
{
    private static bool Prefix(NRewardButton __instance, ref Task __result)
    {
        if (__instance.Reward is not CoreShineCardRewardChoice choice) return true;
        __result = Select(__instance, choice);
        return false;
    }

    private static async Task Select(NRewardButton button, CoreShineCardRewardChoice choice)
    {
        button.Disable();
        await choice.SelectCards();
        if (GodotObject.IsInstanceValid(button) && !button.IsQueuedForDeletion())
            button.EmitSignal(NRewardButton.SignalName.RewardClaimed, button);
    }
}

// When the display is empty, finalize any deliberately unselected original cards before
// the native screen checks backend completion. Other visible rewards retain their buttons.
[HarmonyPatch(typeof(NRewardsScreen), "UpdateScreenState")]
internal static class KarenShineRewardCompletePatch
{
    private static void Prefix(List<Control> ____rewardButtons, RewardsSet ____rewardsSet)
    {
        if (____rewardButtons.Count != 0 ||
            !____rewardsSet.Rewards.OfType<CoreShineCardReward>().Any()) return;
        var synchronizer = RunManager.Instance.RewardsSetSynchronizer;
        if (!synchronizer.IsRewardsSetCompleted(____rewardsSet)) synchronizer.SkipLocalRewardsSet();
    }
}
