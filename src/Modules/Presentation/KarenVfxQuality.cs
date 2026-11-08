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
    private sealed class DrawClock
    {
        public ulong Next;
        public bool Suppressed, WasVisible;
    }
    private static readonly ConditionalWeakTable<CanvasItem, DrawClock> DrawClocks = new();
    internal static CoreVfxLevel Level => CoreModConfig.VfxLevel;
    internal static bool Enabled => CoreModConfig.EnableShineEffects && Level != CoreVfxLevel.Off;
    internal static bool AmbientEffects => Enabled && Level == CoreVfxLevel.Full;
    internal static int ParticleStride => Level == CoreVfxLevel.Full ? 1 : 4;
    // Godot particle emitters require at least one particle. Off prevents their factories
    // and emissions instead of assigning the invalid Amount=0.
    internal static int ParticleCount(int count) => count <= 0 ? 0 : Math.Max(1, (count + ParticleStride - 1) / ParticleStride);
    internal static float SpawnInterval(float interval) => Enabled ? interval * ParticleStride : float.PositiveInfinity;

    internal static void Redraw(CanvasItem? surface)
    {
        if (surface == null) return;
        if (!Enabled) { Suppress(surface); return; }
        Restore(surface);
        if (Level == CoreVfxLevel.Full) { surface.QueueRedraw(); return; }
        var clock = DrawClocks.GetValue(surface, static _ => new DrawClock());
        ulong now = Time.GetTicksUsec();
        if (now < clock.Next) return;
        clock.Next = now + 50000UL;
        surface.QueueRedraw();
    }

    private static void Suppress(CanvasItem item)
    {
        var clock = DrawClocks.GetValue(item, static _ => new DrawClock());
        if (!clock.Suppressed) { clock.WasVisible = item.Visible; clock.Suppressed = true; }
        item.Hide();
    }
    private static void Restore(CanvasItem item)
    {
        if (!DrawClocks.TryGetValue(item, out var clock) || !clock.Suppressed) return;
        clock.Suppressed = false;
        item.Visible = clock.WasVisible;
        clock.Next = 0;
    }
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
