using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;
namespace ShoujoKageki.Core.Runtime.Modules.Presentation;
[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.SetBgmVol))]
internal static class MusicVolumePatch
{
    private static void Postfix() => KarenMusicPlayback.RefreshVolumes();
}
