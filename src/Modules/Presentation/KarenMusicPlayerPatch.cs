using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;


namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

[HarmonyPatch(typeof(NRun), nameof(NRun._Ready))]
internal static class KarenMusicPlayerPatch
{
    private static void Postfix(NRun __instance)
    {
        // Local player is established by run setup before NRun enters the tree.
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null || LocalContext.GetMe(state) == null) return;
        if (__instance.GetNodeOrNull<NKarenMusicPlayer>("KarenMusicPlayer") != null) return;
        __instance.AddChild(new NKarenMusicPlayer { Name = "KarenMusicPlayer" });
    }
}
