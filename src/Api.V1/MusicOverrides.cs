using Godot;

namespace ShoujoKageki.Core.V1;

public sealed record MusicOverrideRequest
{
    public required string ResourcePath { get; init; }
    public float Volume { get; init; } = 1;
    public bool Loop { get; init; } = true;
    public bool UsePlayerSettingsWhenDisabled { get; init; }
    public float FadeOutSeconds { get; init; } = .35f;
    public Node? Lifetime { get; init; }
}
public interface IMusicOverrides
{
    IDisposable AcquireTrack(string owner, MusicOverrideRequest request);
    IDisposable AcquireSilence(string owner, Node? lifetime = null);
    void Refresh();
}
