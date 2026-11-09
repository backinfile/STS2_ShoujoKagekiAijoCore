using Godot;
using MegaCrit.Sts2.Core.Helpers;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

public sealed class MusicModule : ICoreModule, IMusicPlayback, ITemporaryMusicPlayback
{
    private static MusicModule _instance = null!;
    private readonly Dictionary<long, bool> _priorityLeases = [];
    private long _nextLease;
    private bool _formPriority;
    public static bool FormPriority => _instance._formPriority || _instance._priorityLeases.Count > 0;
    internal static bool TemporaryPlaybackSuspended => _instance._formPriority || _instance._priorityLeases.Values.Any(playerOnly => !playerOnly);
    public string Id => "music";
    public int Order => 0;
    public bool HasStream => MusicOverridesModule.HasStream || KarenMusicPlayback.Current?.HasStream == true;
    public MusicPlaybackState State => new(MusicOverridesModule.Title ?? KarenMusicPlayback.Current?.Title, MusicOverridesModule.IsPlaying || KarenMusicPlayback.Current?.IsPlaying == true,
        MusicOverridesModule.HasStream ? MusicOverridesModule.IsPaused : KarenMusicPlayback.Current?.IsPaused == true, CoreApi.Configuration.Current.MusicPlayerVolume, CoreApi.Configuration.Current.MusicPlayerMuted);
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
        playback.Changed += () => _instance.Changed?.Invoke(_instance.State);
    }
    internal static void Publish() => _instance.Changed?.Invoke(_instance.State);
    internal static void RefreshPlayback() => _instance.ApplyPriority();
    public void ForceTrack(string resourcePath)
    {
        if (!resourcePath.StartsWith("res://", StringComparison.Ordinal)) throw new ArgumentException("A resource path is required.", nameof(resourcePath));
        if (KarenMusicPlayback.Current is { } playback) TaskHelper.RunSafely(playback.ForceTrack(resourcePath));
    }
    public void SetFormPriority(bool enabled) { _formPriority = enabled; ApplyPriority(); }
    private void ApplyPriority()
    {
        KarenMusicPlayback.Current?.SetFormPriority(FormPriority);
        MusicOverridesModule.RefreshActive();
    }
    public IDisposable AcquireTrack(string owner, string resourcePath, bool loop = true)
        => CoreApi.Get<IMusicOverrides>().AcquireTrack(owner, new MusicOverrideRequest
        {
            ResourcePath = resourcePath, Loop = loop, FadeOutSeconds = 0, UsePlayerSettingsWhenDisabled = true
        });

    public IDisposable AcquirePriority(string owner) => AcquirePriority(owner, playerOnly: false);
    internal static IDisposable AcquirePlayerPriority(string owner) => _instance.AcquirePriority(owner, playerOnly: true);
    private IDisposable AcquirePriority(string owner, bool playerOnly)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        var id = ++_nextLease;
        _priorityLeases.Add(id, playerOnly);
        ApplyPriority();
        return new PriorityLease(this, id);
    }
    private sealed class PriorityLease(MusicModule music, long id) : IDisposable
    {
        public void Dispose() { if (music._priorityLeases.Remove(id)) music.ApplyPriority(); }
    }
    public void Stop()
    {
        if (CoreApi.Get<IMusicOverrides>() is MusicOverridesModule overrides) overrides.Clear();
        _priorityLeases.Clear(); _formPriority = false;
        if (KarenMusicPlayback.Current is { } playback) { playback.TemporaryLoop = false; playback.Stop(); playback.SetFormPriority(false); }
    }
    public void PlayPause() => KarenMusicPlayback.Current?.Toggle();
    public void Next() => KarenMusicPlayback.Current?.Step(1);
    public void Previous() => KarenMusicPlayback.Current?.Step(-1);
}
