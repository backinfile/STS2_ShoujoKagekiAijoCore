namespace ShoujoKageki.Core.V1;

public enum CombatCardGainRelicMode { Enabled, ShoujoOnly, Disabled }
public enum MusicPlayMode { List, Shuffle, RepeatOne }
public enum CoreVfxLevel { Full, Reduced, Off }
public enum ShinePileButtonMode { Auto, Always, Hidden }

public sealed record CoreSettings
{
    public int FormatVersion { get; init; } = 1;
    public bool ShowSplashScreen { get; init; } = true;
    public bool EnableShineDepletionAnimation { get; init; } = true;
    public ShinePileButtonMode ShinePileButton { get; init; } = ShinePileButtonMode.Auto;
    public CoreVfxLevel VfxLevel { get; init; } = CoreVfxLevel.Full;
    public bool ShowDerivedCardLabel { get; init; } = true;
    public CombatCardGainRelicMode CombatCardGainRelicMode { get; init; } = CombatCardGainRelicMode.ShoujoOnly;
    public bool EnableDataUpload { get; init; } = true;
    public bool EnableErrorUpload { get; init; } = true;
    public string TelemetryBaseUrl { get; init; } = "";
    public bool EnableMusicPlayer { get; init; } = true;
    public string MusicDirectory { get; init; } = "";
    public MusicPlayMode MusicPlayMode { get; init; } = MusicPlayMode.List;
    public float MusicPlayerVolume { get; init; } = .7f;
    public bool MusicPlayerMuted { get; init; }
    public string MusicPlayPauseKey { get; init; } = "F8";
    public string MusicNextKey { get; init; } = "F9";
    public string MusicPreviousKey { get; init; } = "F7";
}

public interface ICoreConfiguration
{
    CoreSettings Current { get; }
    event Action<CoreSettings, CoreSettings>? Changed;
    void Update(CoreSettings settings);
    void Reset();
}
