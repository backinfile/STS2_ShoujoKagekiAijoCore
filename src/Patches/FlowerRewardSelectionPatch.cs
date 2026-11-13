using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using ShoujoKageki.Core.Runtime.Modules.Relics;

namespace ShoujoKageki.Core.Runtime.Patches;

// Native reward indices address the top-level set. Flower children use a native
// player choice after selecting their parent, so the wire index stays valid.
[HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward))]
internal static class FlowerRewardSelectionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(RewardsSetSynchronizer __instance, Reward reward, ref Task<bool> __result)
    {
        if (reward.ParentRewardSet is not KarenFlowerRewardSet flower) return true;
        __result = flower.SelectLocalReward(__instance, reward);
        return false;
    }
}
