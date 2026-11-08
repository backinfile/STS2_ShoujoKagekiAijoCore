namespace ShoujoKageki.Core.V1;

public enum CombatCardGainRelicMode { Enabled, ShoujoOnly, Disabled }
public enum MusicPlayMode { List, Shuffle, RepeatOne }
public enum CoreVfxLevel { Full, Reduced, Off }
public enum ShinePileButtonMode { Auto, Always, Hidden }
public enum DerivedCardLabelMode { Always, ShoujoOnly, Hidden }

public sealed record CoreSettings
{
    public int FormatVersion { get; init; } = 1;
    public bool ShowSplashScreen { get; init; } = true;
    private bool _enableShineEffects = true;
    public bool EnableShineDepletionAnimation { get => _enableShineEffects; init => _enableShineEffects = value; }
    public bool EnableShineEffects { get => _enableShineEffects; init => _enableShineEffects = value; }
    public ShinePileButtonMode ShinePileButton { get; init; } = ShinePileButtonMode.Auto;
    public CoreVfxLevel VfxLevel { get; init; } = CoreVfxLevel.Full;
    private DerivedCardLabelMode _derivedCardLabel = DerivedCardLabelMode.ShoujoOnly;
    public bool ShowDerivedCardLabel { get => _derivedCardLabel != DerivedCardLabelMode.Hidden; init => _derivedCardLabel = value ? DerivedCardLabelMode.Always : DerivedCardLabelMode.Hidden; }
    public DerivedCardLabelMode DerivedCardLabel { get => _derivedCardLabel; init => _derivedCardLabel = value; }
    private CombatCardGainRelicMode _relicMode = CombatCardGainRelicMode.ShoujoOnly;
    private CombatCardGainRelicMode _bookOfFiveRingsPatch = CombatCardGainRelicMode.ShoujoOnly;
    private CombatCardGainRelicMode _luckyFyshPatch = CombatCardGainRelicMode.ShoujoOnly;
    public CombatCardGainRelicMode CombatCardGainRelicMode
    {
        get => _relicMode;
        init => _relicMode = _bookOfFiveRingsPatch = _luckyFyshPatch = value;
    }
    public CombatCardGainRelicMode BookOfFiveRingsPatch { get => _bookOfFiveRingsPatch; init => _bookOfFiveRingsPatch = value; }
    public CombatCardGainRelicMode LuckyFyshPatch { get => _luckyFyshPatch; init => _luckyFyshPatch = value; }
    public bool EnableInformationCollection { get; init; } = true;
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
