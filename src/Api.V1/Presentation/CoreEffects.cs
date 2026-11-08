using System.Runtime.CompilerServices;
using Godot;

namespace ShoujoKageki.Core.V1.Presentation;

/// <summary>Shared local presentation budgets. Suppression never pauses gameplay or cleanup.</summary>
public static class CoreEffects
{
    private sealed class DrawClock { internal ulong Next; internal bool Suppressed, WasVisible; }
    private static readonly ConditionalWeakTable<CanvasItem, DrawClock> Clocks = new();
    public static CoreVfxLevel Level => CoreApi.Configuration.Current.VfxLevel;
    public static bool Enabled => Level != CoreVfxLevel.Off;
    public static bool AmbientEffects => Level == CoreVfxLevel.Full;
    public static int ParticleStride => Level == CoreVfxLevel.Full ? 1 : 4;
    public static int ParticleCount(int count) => count <= 0 ? 0 : Math.Max(1, (count + ParticleStride - 1) / ParticleStride);
    public static float SpawnInterval(float interval) => Enabled ? interval * ParticleStride : float.PositiveInfinity;
    public static void Redraw(CanvasItem? surface, bool enabled = true)
    {
        if (surface == null) return;
        if (!enabled || !Enabled) { Suppress(surface); return; }
        Restore(surface);
        if (Level == CoreVfxLevel.Full) { surface.QueueRedraw(); return; }
        var clock = Clocks.GetValue(surface, _ => new());
        ulong now = Time.GetTicksUsec();
        if (now < clock.Next) return;
        clock.Next = now + 50000UL;
        surface.QueueRedraw();
    }
    /// <summary>Finish real rendering even when the game window is minimized.</summary>
    public static async Task DrawFrame(Node owner)
    {
        await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
        var completed = owner.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        RenderingServer.ForceDraw(swapBuffers: false);
        await completed;
    }
    public static void Suppress(CanvasItem surface)
    {
        var clock = Clocks.GetValue(surface, _ => new());
        if (!clock.Suppressed) { clock.WasVisible = surface.Visible; clock.Suppressed = true; }
        surface.Hide();
    }
    public static void Restore(CanvasItem surface)
    {
        if (!Clocks.TryGetValue(surface, out var clock) || !clock.Suppressed) return;
        clock.Suppressed = false; surface.Visible = clock.WasVisible; clock.Next = 0;
    }
}
