using BaseLib.Config;
using ShoujoKageki.Core.V1;

namespace ShoujoKageki.Core.Runtime.Modules.Configuration;

public sealed class CoreConfiguration : ICoreConfiguration
{
    public static CoreConfiguration Instance { get; private set; } = null!;
    private CoreSettings _current = new();
    internal ShoujoKagekiCore.CoreModConfig? View;
    public CoreSettings Current => _current;
    public event Action<CoreSettings, CoreSettings>? Changed;
    public CoreConfiguration() => Instance = this;
    internal void Initialize()
    {
        MigrateLegacyConfig(Path.Combine(Godot.OS.GetUserDataDir(), "mod_configs"));
        View = new ShoujoKagekiCore.CoreModConfig();
        if (Current.FormatVersion > 1) throw new InvalidDataException($"Core config format {Current.FormatVersion} requires a newer Core.");
        Update(Current with { FormatVersion = 1 });
        View.Save();
    }

    internal static void MigrateLegacyConfig(string directory)
    {
        var current = Path.Combine(directory, CoreApi.ModId + ".cfg");
        var legacy = Path.Combine(directory, "ShoujoKagekiAijoCore.cfg");
        if (File.Exists(current) || !File.Exists(legacy)) return;

        var temporary = Path.Combine(directory, $".{CoreApi.ModId}-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(legacy, temporary);
            File.Move(temporary, current);
        }
        catch (IOException) when (File.Exists(current)) { }
        finally { File.Delete(temporary); }
    }

    internal void RegisterView() => ModConfigRegistry.Register(CoreApi.ModId, View!);
    internal void ClearObservers() => Changed = null;

    public void Update(CoreSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings with
        {
            MusicPlayerVolume = float.IsFinite(settings.MusicPlayerVolume) ? Math.Clamp(settings.MusicPlayerVolume, 0, 1) : .7f,
            MusicDirectory = settings.MusicDirectory ?? "",
            TelemetryBaseUrl = settings.TelemetryBaseUrl ?? "",
            MusicPlayMode = Enum.IsDefined(settings.MusicPlayMode) ? settings.MusicPlayMode : MusicPlayMode.List,
            VfxLevel = Enum.IsDefined(settings.VfxLevel) ? settings.VfxLevel : CoreVfxLevel.Full,
            ShinePileButton = Enum.IsDefined(settings.ShinePileButton) ? settings.ShinePileButton : ShinePileButtonMode.Auto,
            DerivedCardLabel = Enum.IsDefined(settings.DerivedCardLabel) ? settings.DerivedCardLabel : DerivedCardLabelMode.ShoujoOnly,
            CombatCardGainRelicMode = Enum.IsDefined(settings.CombatCardGainRelicMode) ? settings.CombatCardGainRelicMode : CombatCardGainRelicMode.ShoujoOnly,
            BookOfFiveRingsPatch = Enum.IsDefined(settings.BookOfFiveRingsPatch) ? settings.BookOfFiveRingsPatch : CombatCardGainRelicMode.ShoujoOnly,
            LuckyFyshPatch = Enum.IsDefined(settings.LuckyFyshPatch) ? settings.LuckyFyshPatch : CombatCardGainRelicMode.ShoujoOnly
        };
        var previous = _current;
        if (previous == normalized) return;
        _current = normalized;
        View?.Save();
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try { ((Action<CoreSettings, CoreSettings>)handler)(previous, normalized); }
            catch (Exception error) { CoreApi.Get<ICoreDiagnostics>().Report(DiagnosticSeverity.Error, "configuration", "observer.failed", "Configuration observer failed.", error); }
        }
    }
    public void Reset() => Update(new CoreSettings());
}
