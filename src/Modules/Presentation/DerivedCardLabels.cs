using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Cards;
using System.Runtime.CompilerServices;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Hooks;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

/// <summary>
/// 在 NCard 标题正上方显示"衍生牌"标签。
/// 显示条件：IsMutable && DeckVersion == null && 战斗中生成
/// </summary>
public static class NCardDerivedLabelPatch
{
    private static readonly SpireField<NCard, MegaLabel> _derivedLabels = new SpireField<NCard, MegaLabel>(() => null);
    private static readonly SpireField<CardModel, bool> _generatedInCombat = new SpireField<CardModel, bool>(() => false);
    private static readonly LocString DerivedCardLabel = new("gameplay_ui", "CORE_DERIVED_CARD_LABEL");


    public static bool GeneratedInCombat(CardModel card)
    {
        return _generatedInCombat.Get(card);
    }


    internal static void RefreshAll(Node root)
    {
        if (root is NCard card) NCard_UpdateTitleLabel_Patch.Postfix(card);
        foreach (var child in root.GetChildren()) RefreshAll(child);
    }
    [HarmonyPatch(typeof(NCard), nameof(NCard.OnFreedToPool))]
    private static class PoolResetPatch
    {
        private static void Prefix(NCard __instance)
        {
            if (_derivedLabels.Get(__instance) is { } label && GodotObject.IsInstanceValid(label)) label.Visible = false;
        }
    }
    [HarmonyPatch(typeof(NCard), "_Ready")]
    public static class NCard_Ready_Patch
    {
        private static void Postfix(NCard __instance)
        {
            var label = new MegaLabel
            {
                Name = "DerivedLabel",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Position = new Vector2(-105f, -228f),
                Size = new Vector2(210f, 24f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                MaxFontSize = 20,
                MinFontSize = 10,
                Visible = false
            };

            // 复用 TitleLabel 的字体和描边样式
            var titleLabel = __instance.GetNode<MegaLabel>("%TitleLabel");
            label.AddThemeFontOverride(ThemeConstants.Label.Font, titleLabel.GetThemeFont(ThemeConstants.Label.Font));
            label.AddThemeColorOverride(ThemeConstants.Label.FontColor, StsColors.cream);
            label.AddThemeColorOverride(ThemeConstants.Label.FontOutlineColor, StsColors.cardTitleOutlineCommon);
            label.AddThemeConstantOverride(ThemeConstants.Label.OutlineSize, 8);

            __instance.Body.AddChild(label);
            _derivedLabels.Set(__instance, label);
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardGeneratedForCombat))]
    public static class CombatHistory_CardGenerated_Patch
    {
        private static void Postfix(CardModel card)
        {
            _generatedInCombat.Set(card, true);
        }
    }

    [HarmonyPatch(typeof(NCard), "UpdateTitleLabel")]
    public static class NCard_UpdateTitleLabel_Patch
    {
        internal static void Postfix(NCard __instance)
        {
            var label = _derivedLabels.Get(__instance);
            if (label == null) return;

            var model = __instance.Model;
            var mode = CoreApi.Configuration.Current.DerivedCardLabel;
            if (mode != DerivedCardLabelMode.Hidden && model != null && model.IsMutable
                && (mode == DerivedCardLabelMode.Always || CoreDiscovery.IsShoujoCharacter(model.Owner?.Character))
                && model.DeckVersion == null && _generatedInCombat.Get(model))
            {
                label.SetTextAutoSize(DerivedCardLabel.GetFormattedText());
                label.Visible = true;
            }
            else
            {
                label.Visible = false;
            }
        }
    }
}
