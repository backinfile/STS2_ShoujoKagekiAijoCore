namespace ShoujoKageki.Core.V1;

public sealed record MusicPlaybackState(string? Track, bool Playing, bool Paused, float Volume, bool Muted);
public interface IMusicPlayback
{
    bool HasStream { get; }
    MusicPlaybackState State { get; }
    void ForceTrack(string resourcePath);
    void SetFormPriority(bool enabled);
    IDisposable AcquirePriority(string owner);
    void Stop();
    void PlayPause();
    void Next();
    void Previous();
    event Action<MusicPlaybackState>? Changed;
}

/// <summary>A temporary track owns its playback until disposal; nested owners restore the displaced track.</summary>
public interface ITemporaryMusicPlayback
{
    IDisposable AcquireTrack(string owner, string resourcePath, bool loop = true);
}
