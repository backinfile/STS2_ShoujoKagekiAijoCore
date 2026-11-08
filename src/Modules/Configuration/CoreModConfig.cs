using BaseLib.Config;
using BaseLib.Config.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using Godot;
using ShoujoKageki.Core.V1;
using ShoujoKageki.Core.Runtime;
using ShoujoKageki.Core.Runtime.Modules.Configuration;

namespace ShoujoKagekiAijoCore;

public sealed class CoreModConfig : SimpleModConfig
{
    [ConfigIgnore]
    public static bool MusicPlayerMinimized { get; set; } = true;
    [ConfigHideInUI]
    public static int FormatVersion { get => CoreConfiguration.Instance.Current.FormatVersion; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { FormatVersion = value }); }
    public static bool ShowSplashScreen { get => CoreConfiguration.Instance.Current.ShowSplashScreen; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { ShowSplashScreen = value }); }
    [ConfigHideInUI]
    public static bool EnableShineDepletionAnimation { get => CoreConfiguration.Instance.Current.EnableShineDepletionAnimation; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { EnableShineDepletionAnimation = value }); }
    public static ShinePileButtonMode ShinePileButton { get => CoreConfiguration.Instance.Current.ShinePileButton; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { ShinePileButton = value }); }
    [ConfigHideInUI]
    public static CoreVfxLevel VfxLevel { get => CoreConfiguration.Instance.Current.VfxLevel; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { VfxLevel = value }); }
    [ConfigHideInUI]
    public static bool ShowDerivedCardLabel { get => CoreConfiguration.Instance.Current.ShowDerivedCardLabel; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { DerivedCardLabel = value ? DerivedCardLabelMode.ShoujoOnly : DerivedCardLabelMode.Hidden }); }
    public static DerivedCardLabelMode DerivedCardLabel { get => CoreConfiguration.Instance.Current.DerivedCardLabel; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { DerivedCardLabel = value }); }
    [ConfigHideInUI]
    public static bool EnableShineEffects { get => CoreConfiguration.Instance.Current.EnableShineEffects; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { EnableShineEffects = value }); }
    [ConfigHideInUI]
    public static CombatCardGainRelicMode CombatCardGainRelicMode { get => CoreConfiguration.Instance.Current.CombatCardGainRelicMode; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { CombatCardGainRelicMode = value }); }
    [ConfigHideInUI]
    public static CombatCardGainRelicMode BookOfFiveRingsPatch { get => CoreConfiguration.Instance.Current.BookOfFiveRingsPatch; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { BookOfFiveRingsPatch = value }); }
    [ConfigHideInUI]
    public static CombatCardGainRelicMode LuckyFyshPatch { get => CoreConfiguration.Instance.Current.LuckyFyshPatch; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { LuckyFyshPatch = value }); }
    [ConfigHideInUI]
    public static bool EnableInformationCollection { get => CoreConfiguration.Instance.Current.EnableInformationCollection; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { EnableInformationCollection = value }); }
    [ConfigHideInUI]
    public static bool EnableDataUpload { get => CoreConfiguration.Instance.Current.EnableDataUpload; set => CoreConfiguration.Instance.Update(CoreConfiguration.Instance.Current with { EnableDataUpload = value }); }
    [ConfigHideInUI]
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
        var effects = CreateCollapsibleSection("Effects", collapsedByDefault: true);
        container.AddChild(effects);
        foreach (var name in new[] { nameof(VfxLevel), nameof(EnableShineEffects) })
            effects.ContentContainer.AddChild(GenerateOptionFromProperty(typeof(CoreModConfig).GetProperty(name)!));
        var music = CreateCollapsibleSection("MusicPlayer", collapsedByDefault: true);
        container.AddChild(music);
        foreach (var name in new[] { nameof(EnableMusicPlayer), nameof(MusicDirectory), nameof(MusicPlayMode), nameof(MusicPlayerVolume), nameof(MusicPlayerMuted), nameof(MusicPlayPauseKey), nameof(MusicNextKey), nameof(MusicPreviousKey) })
        {
            var property = typeof(CoreModConfig).GetProperty(name)!;
            if (name == nameof(MusicDirectory) || name.EndsWith("Key", StringComparison.Ordinal))
                music.ContentContainer.AddChild(CreateStandardOption(p => new ShoujoKageki.Core.Runtime.Modules.Presentation.NKarenMusicSetting(this, p, CreateRawButtonControl), property));
            else music.ContentContainer.AddChild(GenerateOptionFromProperty(property));
        }
        var patches = CreateCollapsibleSection("Patches", collapsedByDefault: true);
        container.AddChild(patches);
        foreach (var name in new[] { nameof(BookOfFiveRingsPatch), nameof(LuckyFyshPatch) })
            patches.ContentContainer.AddChild(GenerateOptionFromProperty(typeof(CoreModConfig).GetProperty(name)!));
        var collection = CreateCollapsibleSection("InformationCollection", collapsedByDefault: true);
        container.AddChild(collection);
        foreach (var name in new[] { nameof(EnableInformationCollection), nameof(EnableDataUpload), nameof(EnableErrorUpload) })
            collection.ContentContainer.AddChild(GenerateOptionFromProperty(typeof(CoreModConfig).GetProperty(name)!));
        NConfigOptionRow unlock = null!;
        unlock = CreateButton("UnlockAllButton", "UnlockAction", () => UnlockAll((NConfigButton)unlock.SettingControl));
        container.AddChild(unlock);
        AddRestoreDefaultsButton(container);
        SetupFocusNeighbors(container);
    }
    private static void UnlockAll(NConfigButton button)
    {
        var title = new LocString("settings_ui", "SHOUJOKAGEKIAIJOCORE-UNLOCK_ALL_BUTTON.title").GetFormattedText();
        if (GodotObject.IsInstanceValid(NRun.Instance))
        {
            ShowMessage(title, new LocString("settings_ui", "SHOUJOKAGEKIAIJOCORE-UNLOCK_ALL.inRun").GetFormattedText());
            return;
        }
        button.Disable();
        try
        {
            var backup = CoreUnlockAll.Apply();
            var message = new LocString("settings_ui", "SHOUJOKAGEKIAIJOCORE-UNLOCK_ALL.complete");
            message.AddObj("Backup", backup);
            ShowMessage(title, message.GetFormattedText());
        }
        catch (Exception error)
        {
            CoreLog.Logger.Error($"Unlock all failed: {error}");
            var message = new LocString("settings_ui", "SHOUJOKAGEKIAIJOCORE-UNLOCK_ALL.failed");
            message.AddObj("Error", error.Message);
            ShowMessage(title, message.GetFormattedText());
        }
        finally
        {
            if (GodotObject.IsInstanceValid(button)) button.Enable();
        }
    }
    private static void ShowMessage(string title, string message)
    {
        if (NGame.Instance == null) { CoreLog.Logger.Info($"{title}: {message}"); return; }
        var dialog = new AcceptDialog { Title = title, DialogText = message, MinSize = new Vector2I(560, 220) };
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        dialog.CloseRequested += dialog.QueueFree;
        NGame.Instance.AddChild(dialog);
        dialog.PopupCentered();
    }

}
