#if STS2_BETA
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;

namespace ShoujoKageki.Core.Runtime.Patches;

[HarmonyPatch(typeof(NCardExhaustVfx), nameof(NCardExhaustVfx._ExitTree))]
internal static class KarenNativeExhaustCleanupPatch
{
    // Native _ExitTree returns the owned card, but DelayedFree can still resume
    // after the room is replaced. Drop its pooled reference after that cleanup.
    private static void Postfix(ref NCard? ____cardNode) => ____cardNode = null;
}
#endif
