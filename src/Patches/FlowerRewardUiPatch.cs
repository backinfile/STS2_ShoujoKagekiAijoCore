using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using ShoujoKageki.Core.Runtime.Modules.Relics;

namespace ShoujoKageki.Core.Runtime.Patches;

/// <summary>Floral connector and correctly typed native reward signals for Flower choices.</summary>
[HarmonyPatch(typeof(NLinkedRewardSet), "Reload")]
public static class FlowerRewardUiPatch
{
    public static void Postfix(NLinkedRewardSet __instance, Control ____rewardContainer,
        Control ____chainsContainer)
    {
        if (__instance.LinkedRewardSet is not KarenFlowerRewardSet) return;
        var buttons = ____rewardContainer.GetChildren().OfType<NRewardButton>().ToList();
        foreach (var button in buttons)
        {
            // Native Reload binds a zero-argument callback to a one-argument signal.
            foreach (var connection in button.GetSignalConnectionList(NRewardButton.SignalName.RewardClaimed))
            {
                var callable = connection["callable"].AsCallable();
                if (callable.Method != NRewardButton.SignalName.RewardClaimed &&
                    button.IsConnected(NRewardButton.SignalName.RewardClaimed, callable))
                    button.Disconnect(NRewardButton.SignalName.RewardClaimed, callable);
            }
            button.Connect(NRewardButton.SignalName.RewardClaimed, Callable.From<NRewardButton>(_ =>
            {
                // NRewardsScreen removes and frees the complete pair from this signal.
                __instance.EmitSignal(NLinkedRewardSet.SignalName.RewardClaimed, __instance);
            }));
        }
        var texture = ResourceLoader.Load<Texture2D>(V1.CoreApi.ResourceRoot + "images/ui/flower/chain.tres");
        var chains = ____chainsContainer.GetChildren().OfType<TextureRect>().ToList();
        foreach (var chain in chains)
        {
            chain.Texture = texture;
            chain.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            chain.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            chain.Size = new Vector2(60, 60);
        }

        void AlignChains()
        {
            if (!GodotObject.IsInstanceValid(__instance) || __instance.IsQueuedForDeletion()) return;
            for (var i = 0; i < chains.Count && i + 1 < buttons.Count; i++)
            {
                var upper = buttons[i].GetGlobalRect();
                var lower = buttons[i + 1].GetGlobalRect();
                var center = new Vector2(
                    (upper.GetCenter().X + lower.GetCenter().X) * 0.5f,
                    (upper.End.Y + lower.Position.Y) * 0.5f);
                chains[i].GlobalPosition = center - chains[i].Size * 0.5f;
            }
        }

        // Native chain offsets target the old artwork. Measure the real button gap after
        // the VBox has laid out its children, including subsequent container resizes.
        var align = Callable.From(AlignChains);
        if (____rewardContainer is Container container)
            container.SortChildren += () => align.CallDeferred();
        __instance.Resized += () => align.CallDeferred();
        align.CallDeferred();
    }
}
