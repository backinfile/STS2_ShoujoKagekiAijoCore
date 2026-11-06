using ShoujoKageki.Core.Runtime.Modules.Presentation;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

using System.Linq;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

public static class ShineVfx
{
    // Canonical preview cards have no combat depletion state; their Owner getter requires mutability.
    public static bool WillDeplete(CardModel card, bool shineAlreadyDecreased = false) => card.IsMutable
        && V1.Shine.GetState(card) is { } state && !card.IsDupe
        && (state.Remaining <= (shineAlreadyDecreased ? 0 : 1) || state.PendingDepletion)
        && !card.Keywords.Contains(CardKeyword.Eternal);

    private static readonly SpireField<NCard, NKarenShineWarningVfx?> Warnings = new(() => null);
    private static readonly SpireField<NCard, NKarenShineAmbientVfx?> Ambients = new(() => null);

    private static void Refresh(NCard card)
    {
        if (!card.IsNodeReady()) return;
        if (!KarenVfxQuality.Enabled) { Clear(card); return; }
        var old = Warnings.Get(card);
        var ambient = Ambients.Get(card);
        if (GodotObject.IsInstanceValid(old) && old!.BelongsTo(card.Model)
            && !old.IsQueuedForDeletion() && old.GetParent() == card.OverlayContainer
            && GodotObject.IsInstanceValid(ambient) && ambient!.BelongsTo(card.Model)
            && !ambient.IsQueuedForDeletion() && ambient.GetParent() == card.OverlayContainer) return;
        Clear(card);
        if (card.Model?.IsShineCard() != true) return;
        var warning = new NKarenShineWarningVfx(card, card.Model);
        card.OverlayContainer.AddChild(warning);
        Warnings.Set(card, warning);
        ambient = new NKarenShineAmbientVfx(card, card.Model, warning);
        card.OverlayContainer.AddChild(ambient);
        Ambients.Set(card, ambient);
    }

    internal static void RefreshQuality(NCard card) { Clear(card); if (KarenVfxQuality.Enabled) Refresh(card); }

    private static void Clear(NCard card)
    {
        var ambient = Ambients.Get(card);
        Ambients.Set(card, null);
        if (GodotObject.IsInstanceValid(ambient))
        {
            ambient!.GetParent()?.RemoveChild(ambient);
            ambient.QueueFree();
        }
        var old = Warnings.Get(card);
        Warnings.Set(card, null);
        if (!GodotObject.IsInstanceValid(old)) return;
        old!.GetParent()?.RemoveChild(old);
        old.QueueFree();
    }

    // The native card pool builds 30 nodes at startup. Prepare shared geometry there,
    // before a model's first visible Shine overlay needs the atlas pixels.
    [HarmonyPatch(typeof(NCard), nameof(NCard.OnInstantiated))]
    private static class PoolWarmupPatch
    {
        private static void Postfix(NCard __instance) => ShineCardBorder.Prepare(__instance);
    }

    [HarmonyPatch(typeof(NCard), nameof(NCard.UpdateVisuals))]
    private static class VisualPatch { private static void Postfix(NCard __instance) => Refresh(__instance); }

    [HarmonyPatch(typeof(NCard), "ReloadOverlay")]
    private static class OverlayPatch { private static void Postfix(NCard __instance) => Refresh(__instance); }

    [HarmonyPatch(typeof(NCard), nameof(NCard.OnFreedToPool))]
    private static class PoolPatch { private static void Prefix(NCard __instance) => Clear(__instance); }
}
