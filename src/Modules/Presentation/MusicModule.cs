using Godot;
using MegaCrit.Sts2.Core.Helpers;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

public sealed class MusicModule : ICoreModule, IMusicPlayback, ITemporaryMusicPlayback
{
    private static MusicModule _instance = null!;
    private readonly HashSet<long> _priorityLeases = [];
    private long _nextLease;
    private bool _formPriority;
    private sealed record Track(long Id, string Path, bool Loop)
    {
        internal KarenMusicPlayback.InterruptedPlayback? State;
    }
    private readonly List<Track> _tracks = [];
    private Track? _activeTrack;

    public static bool FormPriority => _instance._formPriority || _instance._priorityLeases.Count > 0 || _instance._tracks.Count > 0;
    public string Id => "music";
    public int Order => 0;
    public bool HasStream => KarenMusicPlayback.Current?.HasStream == true;
    public MusicPlaybackState State => new(KarenMusicPlayback.Current?.Title, KarenMusicPlayback.Current?.IsPlaying == true,
        KarenMusicPlayback.Current?.IsPaused == true, CoreApi.Configuration.Current.MusicPlayerVolume, CoreApi.Configuration.Current.MusicPlayerMuted);
    public event Action<MusicPlaybackState>? Changed;
    public void Initialize(CoreContext context)
    {
        _instance = this;
        context.Services.Add<IMusicPlayback>(this);
        context.Services.Add<ITemporaryMusicPlayback>(this);
        context.Configuration.Changed += (_, _) => { NKarenMusicPlayer.ConfigurationChanged(); KarenMusicPlayback.RefreshVolumes(); };
        context.ShuttingDown += () => KarenMusicPlayback.Current?.Dispose();
    }
    internal static void Attach(KarenMusicPlayback playback)
    {
        _instance._activeTrack = null;
        playback.Changed += () => _instance.Changed?.Invoke(_instance.State);
    }
    internal static void PlaybackReset() => _instance._activeTrack = null;
    internal static void RefreshPlayback() => _instance.ApplyPriority();
    public void ForceTrack(string resourcePath)
    {
        if (!resourcePath.StartsWith("res://", StringComparison.Ordinal)) throw new ArgumentException("A resource path is required.", nameof(resourcePath));
        if (KarenMusicPlayback.Current is { } playback) TaskHelper.RunSafely(playback.ForceTrack(resourcePath));
    }
    public void SetFormPriority(bool enabled) { _formPriority = enabled; ApplyPriority(); }
    private void ApplyPriority()
    {
        if (KarenMusicPlayback.Current is not { } playback) return;
        var next = _tracks.LastOrDefault();
        bool suspended = _formPriority || _priorityLeases.Count > 0;
        if (!ReferenceEquals(_activeTrack, next))
        {
            if (_activeTrack != null) _activeTrack.State = playback.CaptureTemporary();
            if (next == null) playback.SetFormPriority(false);
            playback.SetFormPriority(FormPriority);
            playback.TemporaryLoop = next?.Loop == true;
            if (next?.State is { } state) playback.RestoreTemporary(state);
            else if (next != null) TaskHelper.RunSafely(playback.ForceTrack(next.Path));
            _activeTrack = next;
        }
        else playback.SetFormPriority(FormPriority);
        if (next != null) playback.SuspendTemporary(suspended);
    }
    public IDisposable AcquireTrack(string owner, string resourcePath, bool loop = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        if (!resourcePath.StartsWith("res://", StringComparison.Ordinal)) throw new ArgumentException("A resource path is required.", nameof(resourcePath));
        var track = new Track(++_nextLease, resourcePath, loop);
        _tracks.Add(track); ApplyPriority();
        return new TrackLease(this, track);
    }
    private sealed class TrackLease(MusicModule music, Track track) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; music._tracks.Remove(track); music.ApplyPriority();
        }
    }

    public IDisposable AcquirePriority(string owner)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        var id = ++_nextLease;
        _priorityLeases.Add(id);
        ApplyPriority();
        return new PriorityLease(this, id);
    }
    private sealed class PriorityLease(MusicModule music, long id) : IDisposable
    {
        public void Dispose() { music._priorityLeases.Remove(id); music.ApplyPriority(); }
    }
    public void Stop()
    {
        _tracks.Clear(); _activeTrack = null;
        _priorityLeases.Clear(); _formPriority = false;
        if (KarenMusicPlayback.Current is { } playback) { playback.TemporaryLoop = false; playback.Stop(); playback.SetFormPriority(false); }
    }
    public void PlayPause() => KarenMusicPlayback.Current?.Toggle();
    public void Next() => KarenMusicPlayback.Current?.Step(1);
    public void Previous() => KarenMusicPlayback.Current?.Step(-1);
}
