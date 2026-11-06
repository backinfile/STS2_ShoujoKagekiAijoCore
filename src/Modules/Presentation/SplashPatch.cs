using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

[HarmonyPatch(typeof(NLogoAnimation), nameof(NLogoAnimation.PlayAnimation))]
internal static class SplashPatch
{
    private static bool _played;
    [HarmonyReversePatch(HarmonyReversePatchType.Snapshot)]
    private static Task Original(NLogoAnimation instance, CancellationToken token) => throw new NotImplementedException();
    private static bool Prefix(NLogoAnimation __instance, CancellationToken token, ref Task __result)
    {
        if (_played || !CoreApi.Configuration.Current.ShowSplashScreen) return true;
        _played = true;
        __result = Play(__instance, token);
        return false;
    }
    private static async Task Play(NLogoAnimation logo, CancellationToken token)
    {
        if (!token.IsCancellationRequested) await KarenSplashScreen.Play(logo, token);
        if (!token.IsCancellationRequested) await Original(logo, token);
    }
}
