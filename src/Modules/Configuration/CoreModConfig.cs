using BaseLib.Config;
using Godot;
using ShoujoKageki.Core.V1;
using ShoujoKageki.Core.Runtime.Modules.Configuration;

namespace ShoujoKagekiAijoCore;

public sealed class CoreModConfig : SimpleModConfig
{
    [ConfigIgnore]
    public static bool MusicPlayerMinimized { get; set; } = true;
    [ConfigHideInUI]
    public static int FormatVersion { get => CoreConfiguration.Instance.Current.FormatVersion; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { FormatVersion = value }); }
    public static bool ShowSplashScreen { get => CoreConfiguration.Instance.Current.ShowSplashScreen; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { ShowSplashScreen = value }); }
    public static bool EnableShineDepletionAnimation { get => CoreConfiguration.Instance.Current.EnableShineDepletionAnimation; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { EnableShineDepletionAnimation = value }); }
    public static ShinePileButtonMode ShinePileButton { get => CoreConfiguration.Instance.Current.ShinePileButton; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { ShinePileButton = value }); }
    public static CoreVfxLevel VfxLevel { get => CoreConfiguration.Instance.Current.VfxLevel; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { VfxLevel = value }); }
    public static bool ShowDerivedCardLabel { get => CoreConfiguration.Instance.Current.ShowDerivedCardLabel; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { ShowDerivedCardLabel = value }); }
    public static CombatCardGainRelicMode CombatCardGainRelicMode { get => CoreConfiguration.Instance.Current.CombatCardGainRelicMode; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { CombatCardGainRelicMode = value }); }
    public static bool EnableDataUpload { get => CoreConfiguration.Instance.Current.EnableDataUpload; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { EnableDataUpload = value }); }
    public static bool EnableErrorUpload { get => CoreConfiguration.Instance.Current.EnableErrorUpload; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { EnableErrorUpload = value }); }
    [ConfigHideInUI]
    public static string TelemetryBaseUrl { get => CoreConfiguration.Instance.Current.TelemetryBaseUrl; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { TelemetryBaseUrl = value }); }
    [ConfigHideInUI]
    public static bool EnableMusicPlayer { get => CoreConfiguration.Instance.Current.EnableMusicPlayer; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { EnableMusicPlayer = value }); }
    [ConfigHideInUI]
    public static string MusicDirectory { get => CoreConfiguration.Instance.Current.MusicDirectory; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { MusicDirectory = value }); }
    [ConfigHideInUI]
    public static MusicPlayMode MusicPlayMode { get => CoreConfiguration.Instance.Current.MusicPlayMode; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { MusicPlayMode = value }); }
    [ConfigHideInUI]
    public static float MusicPlayerVolume { get => CoreConfiguration.Instance.Current.MusicPlayerVolume; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { MusicPlayerVolume = value }); }
    [ConfigHideInUI]
    public static bool MusicPlayerMuted { get => CoreConfiguration.Instance.Current.MusicPlayerMuted; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { MusicPlayerMuted = value }); }
    [ConfigHideInUI]
    public static string MusicPlayPauseKey { get => CoreConfiguration.Instance.Current.MusicPlayPauseKey; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { MusicPlayPauseKey = value }); }
    [ConfigHideInUI]
    public static string MusicNextKey { get => CoreConfiguration.Instance.Current.MusicNextKey; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { MusicNextKey = value }); }
    [ConfigHideInUI]
    public static string MusicPreviousKey { get => CoreConfiguration.Instance.Current.MusicPreviousKey; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { MusicPreviousKey = value }); }
    public override void SetupConfigUI(Control container)
    {
        GenerateOptionsForAllProperties(container);
        var music = CreateCollapsibleSection("MusicPlayer", collapsedByDefault: true);
        container.AddChild(music);
        foreach (var name in new[] { nameof(EnableMusicPlayer), nameof(MusicDirectory), nameof(MusicPlayMode), nameof(MusicPlayerVolume), nameof(MusicPlayerMuted), nameof(MusicPlayPauseKey), nameof(MusicNextKey), nameof(MusicPreviousKey) })
        {
            var property = typeof(CoreModConfig).GetProperty(name)!;
            if (name == nameof(MusicDirectory) || name.EndsWith("Key", StringComparison.Ordinal))
                music.ContentContainer.AddChild(CreateStandardOption(p => new ShoujoKageki.Core.Runtime.Modules.Presentation.NKarenMusicSetting(this, p, CreateRawButtonControl), property));
            else music.ContentContainer.AddChild(GenerateOptionFromProperty(property));
        }
        AddRestoreDefaultsButton(container);
        SetupFocusNeighbors(container);
    }
}
