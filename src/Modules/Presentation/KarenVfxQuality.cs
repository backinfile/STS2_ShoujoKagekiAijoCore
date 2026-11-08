using ShoujoKageki.Core.V1.Presentation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;

using ShoujoKageki.Core.Runtime.Modules.Presentation;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;



/// <summary>Local presentation budgets; gameplay and card ownership stay native.</summary>
internal static class KarenVfxQuality
{
    internal static CoreVfxLevel Level => (CoreVfxLevel)(int)CoreEffects.Level;
    internal static bool Enabled => CoreModConfig.EnableShineEffects && CoreEffects.Enabled;
    internal static bool AmbientEffects => Enabled && CoreEffects.AmbientEffects;
    internal static int ParticleStride => CoreEffects.ParticleStride;
    internal static int ParticleCount(int count) => CoreEffects.ParticleCount(count);
    internal static float SpawnInterval(float interval) => Enabled ? CoreEffects.SpawnInterval(interval) : float.PositiveInfinity;
    internal static void Redraw(CanvasItem? surface) => CoreEffects.Redraw(surface, Enabled);
    private static void Suppress(CanvasItem surface) => CoreEffects.Suppress(surface);
    private static void Restore(CanvasItem surface) => CoreEffects.Restore(surface);
    private static bool IsEffect(Type type) => type.Assembly == typeof(KarenVfxQuality).Assembly
        && (type.Name.StartsWith("NKaren", StringComparison.Ordinal) && type.Name.EndsWith("Vfx", StringComparison.Ordinal) || type.Name.StartsWith("NDisableRelic", StringComparison.Ordinal))
        && typeof(CanvasItem).IsAssignableFrom(type);
    private static bool IsSiblingLayer(Node node) => node.GetType() == typeof(Node2D)
        && (node.Name.ToString().StartsWith("Karen", StringComparison.Ordinal)
            || node.Name.ToString().StartsWith("PastAndFutureRing", StringComparison.Ordinal)
            || node.Name.ToString().StartsWith("Aquarium", StringComparison.Ordinal));
    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren()) foreach (var item in Walk(child)) yield return item;
    }
    internal static void ApplyLevel()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var nodes = Walk(tree.Root).ToArray();
        foreach (var item in nodes.OfType<CanvasItem>())
        {
            if (Enabled) Restore(item);
            else if (IsEffect(item.GetType()) || IsSiblingLayer(item)) Suppress(item);
        }
        foreach (var card in nodes.OfType<NCard>()) ShineVfx.RefreshQuality(card);
    }
    // Covers existing effects when a player switches Off during an animation, including
    // Sprite2D/Shader children and separately parented back/HUD layers. Their native clocks
    // and cleanup callbacks keep running, so no hidden animation holds up a card action.
    [HarmonyPatch]
    private static class VisibilityPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(KarenVfxQuality).Assembly.GetTypes()
            .Where(IsEffect).SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.Name is "_Ready" or "_Process");
        private static void Postfix(CanvasItem __instance)
        {
            if (Enabled) return;
            Suppress(__instance);
            if (__instance.GetParent() is not { } parent) return;
            foreach (var sibling in parent.GetChildren())
                if (sibling is CanvasItem canvas && IsSiblingLayer(sibling)) Suppress(canvas);
        }
    }
}
