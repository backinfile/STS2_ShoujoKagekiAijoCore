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
        if (tips.OfType<HoverTip>().Any(x => x.Id == KeywordId)) return;
        tips.Add(new HoverTip(new LocString("card_keywords", KeywordId + ".title"), new LocString("card_keywords", KeywordId + ".description")) { Id = KeywordId });
        __result = tips;
    }
}
