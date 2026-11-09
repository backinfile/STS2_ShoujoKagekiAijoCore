using System.Runtime.CompilerServices;
using Godot;

namespace ShoujoKageki.Core.V1.Presentation;

/// <summary>Shared local presentation budgets. Suppression never pauses gameplay or cleanup.</summary>
public static class CoreEffects
{
    private sealed class DrawClock { internal ulong Next; internal bool Suppressed, WasVisible; }
    private static readonly ConditionalWeakTable<CanvasItem, DrawClock> Clocks = new();
    private sealed class Registration
    {
        private readonly WeakReference<CanvasItem> _surface;
        private readonly Func<bool> _enabled;
        internal bool IsEnabled => Enabled && _enabled();
        internal Registration(CanvasItem surface, Func<bool> enabled)
        {
            _surface = new(surface);
            _enabled = enabled;
            surface.VisibilityChanged += OnVisibilityChanged;
        }
        private void OnVisibilityChanged()
        {
            if (!IsEnabled && _surface.TryGetTarget(out var surface) && surface.Visible)
            {
                Clocks.GetValue(surface, _ => new()).WasVisible = true;
                Suppress(surface);
            }
        }
    }
    private static readonly ConditionalWeakTable<CanvasItem, Registration> Registrations = new();
    private static readonly List<WeakReference<CanvasItem>> Registered = [];
    private static bool _observingConfiguration;

    /// <summary>Registers a visual layer. Its processing and cleanup continue at every effect level.</summary>
    public static void Register(CanvasItem surface) => Register(surface, static () => true);
    public static void Register(CanvasItem surface, Func<bool> enabled)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(enabled);
        if (Registrations.TryGetValue(surface, out _)) return;
        var registration = new Registration(surface, enabled);
        Registrations.Add(surface, registration);
        Registered.RemoveAll(reference => !reference.TryGetTarget(out var previous) || !GodotObject.IsInstanceValid(previous));
        Registered.Add(new(surface));
        if (!_observingConfiguration)
        {
            CoreApi.Configuration.Changed += (_, _) => RefreshRegistered();
            _observingConfiguration = true;
        }
        if (!registration.IsEnabled) Suppress(surface);
    }
    public static void RefreshRegistered()
    {
        Registered.RemoveAll(reference => !reference.TryGetTarget(out var surface) || !GodotObject.IsInstanceValid(surface));
        foreach (var reference in Registered.ToArray())
        {
            if (!reference.TryGetTarget(out var surface)) continue;
            if (Registrations.TryGetValue(surface, out var registration) && registration.IsEnabled) Restore(surface);
            else Suppress(surface);
        }
    }
    public static CoreVfxLevel Level => CoreApi.Configuration.Current.VfxLevel;
    public static bool Enabled => Level != CoreVfxLevel.Off;
    public static bool AmbientEffects => Level == CoreVfxLevel.Full;
    public static int ParticleStride => Level == CoreVfxLevel.Full ? 1 : 4;
    public static int ParticleCount(int count) => count <= 0 ? 0 : Math.Max(1, (count + ParticleStride - 1) / ParticleStride);
    public static float SpawnInterval(float interval) => Enabled ? interval * ParticleStride : float.PositiveInfinity;
    public static void Redraw(CanvasItem? surface, bool enabled = true)
    {
        if (surface == null) return;
        if (!enabled || !Enabled || Registrations.TryGetValue(surface, out var registration) && !registration.IsEnabled) { Suppress(surface); return; }
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
