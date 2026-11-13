using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

[HarmonyPatch(typeof(CardModel), nameof(CardModel.HoverTips), MethodType.Getter)]
internal static class ShineHoverTipPatch
{
    public const string KeywordId = "SHOUJO_CORE_SHINE";
    private static void Postfix(CardModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (V1.Shine.GetState(__instance) == null) return;
        var tips = __result.ToList();
        var tip = new HoverTip(new LocString("card_keywords", KeywordId + ".title"), new LocString("card_keywords", KeywordId + ".description"))
        {
#if !STS2_BETA
            Id = KeywordId
#endif
        };
        if (tips.OfType<HoverTip>().Any(x => x.Id == tip.Id)) return;
        tips.Add(tip);
        __result = tips;
    }
}
