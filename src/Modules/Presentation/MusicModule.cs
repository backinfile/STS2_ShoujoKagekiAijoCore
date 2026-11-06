using Godot;
using MegaCrit.Sts2.Core.Helpers;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

public sealed class MusicModule : ICoreModule, IMusicPlayback
{
    private static MusicModule _instance = null!;
    private readonly HashSet<long> _priorityLeases = [];
    private long _nextLease;
    private bool _formPriority;
    public static bool FormPriority => _instance._formPriority || _instance._priorityLeases.Count > 0;
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
        context.Configuration.Changed += (_, _) => { NKarenMusicPlayer.ConfigurationChanged(); KarenMusicPlayback.RefreshVolumes(); };
        context.ShuttingDown += () => KarenMusicPlayback.Current?.Dispose();
    }
    internal static void Attach(KarenMusicPlayback playback) => playback.Changed += () => _instance.Changed?.Invoke(_instance.State);
    public void ForceTrack(string resourcePath)
    {
        if (!resourcePath.StartsWith("res://", StringComparison.Ordinal)) throw new ArgumentException("A resource path is required.", nameof(resourcePath));
        if (KarenMusicPlayback.Current is { } playback) TaskHelper.RunSafely(playback.ForceTrack(resourcePath));
    }
    public void SetFormPriority(bool enabled) { _formPriority = enabled; ApplyPriority(); }
    private void ApplyPriority() => KarenMusicPlayback.Current?.SetFormPriority(FormPriority);
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
    public void Stop() => KarenMusicPlayback.Current?.Stop();
    public void PlayPause() => KarenMusicPlayback.Current?.Toggle();
    public void Next() => KarenMusicPlayback.Current?.Step(1);
    public void Previous() => KarenMusicPlayback.Current?.Step(-1);
}
