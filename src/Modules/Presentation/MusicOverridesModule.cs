using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Saves;
using ShoujoKageki.Core.V1.Presentation;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

/// <summary>One owner stack for temporary tracks and silence, independent of the player interface.</summary>
public sealed class MusicOverridesModule : ICoreModule, IMusicOverrides, IDisposable
{
    private sealed class Entry(string owner, MusicOverrideRequest? request, Node? lifetime)
    {
        internal readonly string Owner = owner;
        internal readonly MusicOverrideRequest? Request = request;
        internal readonly Node? Lifetime = lifetime;
        internal AudioStreamPlayer? Player;
        internal Action? Release;
    }
    private static MusicOverridesModule? _instance;
    private readonly List<Entry> _entries = [];
    private Entry? _active;
    private Entry? _fading;
    private Tween? _fade;
    private IDisposable? _priority;
    private CoreContext? _context;
    internal static bool SuppressesNative => _instance is { } module && (module._entries.Count > 0 || module._fading != null);
    private static Entry? CurrentEntry => _instance?._active ?? _instance?._fading;
    internal static bool HasStream => CurrentEntry?.Player?.Stream != null;
    internal static bool IsPlaying => CurrentEntry?.Player is { Playing: true, StreamPaused: false };
    internal static bool IsPaused => CurrentEntry?.Player?.StreamPaused == true;
    internal static string? Title => Path.GetFileNameWithoutExtension(CurrentEntry?.Request?.ResourcePath);
    internal static void RefreshActive() => _instance?.Refresh();
    public string Id => "music-overrides";
    public int Order => 0;
    public void Initialize(CoreContext context)
    {
        _instance = this;
        _context = context;
        context.Services.Add<IMusicOverrides>(this);
        context.Configuration.Changed += ConfigurationChanged;
        context.ShuttingDown += Dispose;
    }
    private void ConfigurationChanged(CoreSettings previous, CoreSettings current) => Refresh();
    public IDisposable AcquireTrack(string owner, MusicOverrideRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ResourcePath.StartsWith("res://", StringComparison.Ordinal) || !float.IsFinite(request.Volume)
            || request.Volume < 0 || !float.IsFinite(request.FadeOutSeconds) || request.FadeOutSeconds < 0)
            throw new ArgumentException("Invalid temporary music request.", nameof(request));
        return Acquire(new(owner, request, request.Lifetime));
    }
    public IDisposable AcquireSilence(string owner, Node? lifetime = null) => Acquire(new(owner, null, lifetime));
    private IDisposable Acquire(Entry entry)
    {
        ArgumentException.ThrowIfNullOrEmpty(entry.Owner);
        if (entry.Lifetime != null && (!GodotObject.IsInstanceValid(entry.Lifetime) || !entry.Lifetime.IsInsideTree()))
            throw new ArgumentException("The music lifetime must be inside the scene tree.");
        CancelFade();
        _priority ??= MusicModule.AcquirePlayerPriority(nameof(MusicOverridesModule));
        _entries.Add(entry);
        entry.Release = () => Release(entry, fade: false);
        if (entry.Lifetime != null) entry.Lifetime.TreeExiting += entry.Release;
        try { Refresh(); }
        catch { Release(entry, fade: false); throw; }
        return new Lease(() => Release(entry));
    }
    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
    private void Release(Entry entry) => Release(entry, fade: true);
    private void Release(Entry entry, bool fade)
    {
        if (!_entries.Remove(entry)) return;
        if (GodotObject.IsInstanceValid(entry.Lifetime)) entry.Lifetime!.TreeExiting -= entry.Release;
        if (ReferenceEquals(_active, entry))
        {
            _active = null;
            if (fade && entry.Request is { FadeOutSeconds: > 0 } request && GodotObject.IsInstanceValid(entry.Player) && entry.Player!.Playing)
            {
                CancelFade();
                _fading = entry;
                _fade = entry.Player.CreateTween();
                _fade.TweenProperty(entry.Player, "volume_linear", 0f, request.FadeOutSeconds);
                _fade.Finished += () => { Free(entry); _fade = null; _fading = null; Refresh(); };
                RefreshNativeVolume();
                MusicModule.Publish();
                return;
            }
        }
        Free(entry);
        Refresh();
    }
    private static void Free(Entry entry)
    {
        if (!GodotObject.IsInstanceValid(entry.Player)) return;
        entry.Player!.Stop();
        entry.Player.QueueFree();
        entry.Player = null;
    }
    private void CancelFade()
    {
        _fade?.Kill();
        _fade = null;
        if (_fading != null) Free(_fading);
        _fading = null;
    }
    public void Refresh()
    {
        if (_fading != null) return;
        var next = _entries.LastOrDefault();
        if (!ReferenceEquals(next, _active))
        {
            if (GodotObject.IsInstanceValid(_active?.Player)) _active!.Player!.StreamPaused = true;
            _active = next;
        }
        if (next?.Request is { } request)
        {
            bool useSettings = CoreModConfig.EnableMusicPlayer || request.UsePlayerSettingsWhenDisabled;
            float gain = useSettings ? CoreModConfig.MusicPlayerMuted ? 0 : CoreModConfig.MusicPlayerVolume : request.Volume;
            if (!GodotObject.IsInstanceValid(next.Player))
            {
                var stream = CoreResources.LoadAudioStream(request.ResourcePath, request.ResourcePath.Split('/').Last(), nameof(MusicOverridesModule))?.Duplicate() as AudioStream
                    ?? throw new InvalidOperationException("Temporary music could not be loaded.");
                if (stream is AudioStreamMP3 mp3) mp3.Loop = request.Loop;
                if (stream is AudioStreamOggVorbis ogg) ogg.Loop = request.Loop;
                if (stream is AudioStreamWav wav) wav.LoopMode = request.Loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                EnsureBus();
                next.Player = new AudioStreamPlayer { Stream = stream, Bus = "CoreTemporaryMusic", VolumeLinear = gain };
                (request.Lifetime ?? NGame.Instance!).AddChild(next.Player);
                next.Player.Play();
            }
            next.Player!.StreamPaused = MusicModule.TemporaryPlaybackSuspended;
            next.Player.VolumeLinear = gain;
        }
        if (next == null)
        {
            var priority = _priority;
            _priority = null;
            priority?.Dispose();
        }
        RefreshNativeVolume();
        MusicModule.Publish();
    }
    private static void EnsureBus()
    {
        if (AudioServer.GetBusIndex("CoreTemporaryMusic") >= 0) return;
        AudioServer.AddBus();
        AudioServer.SetBusName(AudioServer.BusCount - 1, "CoreTemporaryMusic");
        AudioServer.SetBusSend(AudioServer.BusCount - 1, "Master");
    }
    internal static void RefreshNativeVolume()
    {
        float volume = SaveManager.Instance?.SettingsSave?.VolumeBgm ?? 1;
        int bus = AudioServer.GetBusIndex("CoreTemporaryMusic");
        if (bus >= 0) AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Math.Max(.00001f, volume)));
        var proxy = NAudioManager.Instance?.GetNodeOrNull<Node>("Proxy");
        if (GodotObject.IsInstanceValid(proxy)) proxy!.Call("set_bgm_volume", SuppressesNative || KarenMusicPlayback.OverridesBgm ? 0f : volume * volume);
    }
    internal void Clear()
    {
        CancelFade();
        foreach (var entry in _entries.ToArray()) Release(entry, fade: false);
    }
    public void Dispose()
    {
        Clear();
        if (_context != null)
        {
            _context.Configuration.Changed -= ConfigurationChanged;
            _context.ShuttingDown -= Dispose;
            _context = null;
        }
    }
}
