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
    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren()) foreach (var item in Walk(child)) yield return item;
    }
    internal static void ApplyLevel()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var nodes = Walk(tree.Root).ToArray();
        CoreEffects.RefreshRegistered();
        foreach (var card in nodes.OfType<NCard>()) ShineVfx.RefreshQuality(card);
    }
}
