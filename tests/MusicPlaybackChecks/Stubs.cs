using Godot;
namespace ShoujoKageki.Core.V1.Presentation { }
namespace MegaCrit.Sts2.Core.Saves
{
    internal sealed class SaveManager
    {
        internal static SaveManager? Instance => null;
        internal Settings? SettingsSave => null;
        internal sealed class Settings { internal float VolumeBgm => 1; }
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Audio
{
    internal static class NAudioManager { internal static Node? Instance => null; }
}
namespace MegaCrit.Sts2.Core.Localization
{
    internal sealed class LocString(string table, string key) { internal string GetFormattedText() => table + "." + key; }
}
namespace ShoujoKageki.Core.Runtime.Modules.Presentation
{
    internal enum MusicPlayMode { List, Shuffle, RepeatOne }
    internal static class CoreModConfig
    {
        internal static string MusicDirectory = "";
        internal static bool MusicPlayerMuted = true;
        internal static float MusicPlayerVolume = .7f;
        internal static bool EnableMusicPlayer = true;
        internal static MusicPlayMode MusicPlayMode = MusicPlayMode.List;
    }
    internal static class MusicModule
    {
        internal static bool FormPriority = false;
        internal static void Attach(KarenMusicPlayback playback) { }
        internal static void RefreshPlayback() => KarenMusicPlayback.Current?.SetFormPriority(FormPriority);
    }
    internal static class MusicOverridesModule
    {
        internal static bool SuppressesNative => false;
        internal static void RefreshNativeVolume() { }
    }
    internal static class CoreResources
    {
        internal static AudioStream? LoadAudioStream(string path, string file, string owner)
            => new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = 8000, Data = new byte[8000 * 2 * 60] };
    }
    internal static class CoreLog
    {
        internal static readonly Log Logger = new();
        internal sealed class Log { internal void Info(string message) => GD.Print(message); }
    }
}
