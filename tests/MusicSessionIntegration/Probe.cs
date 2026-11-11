using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;

namespace CoreMusicSessionIntegration;

[ModInitializer(nameof(Initialize))]
public static class Startup { public static void Initialize() { } }

public sealed class MusicSessionProbe : AbstractConsoleCmd
{
    public override string CmdName => "core_music_session";
    public override string Args => "status|play|pause|seek|exit";
    public override string Description => "Read and drive local music for session-resume checks.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? player, string[] args)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ShoujoKageki.Core.Runtime.Modules.Presentation.KarenMusicPlayback")).First(t => t != null)!;
        var music = type.GetProperty("Current", flags)!.GetValue(null);
        if (music == null) return new(false, "No music player");
        object? Get(string name) => type.GetProperty(name, flags)!.GetValue(music);
        switch (args.FirstOrDefault())
        {
            case "play": TaskHelper.RunSafely((Task)type.GetMethod("Play", flags)!.Invoke(music, [3])!); break;
            case "pause": type.GetMethod("Toggle", flags)!.Invoke(music, null); break;
            case "seek": type.GetMethod("Seek", flags)!.Invoke(music, [20d]); break;
            case "exit":
                var method = typeof(NGame).GetMethod("ReturnToMainMenu", flags)!;
                var values = method.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : throw new InvalidOperationException("Required exit argument: " + p.Name)).ToArray();
                var task = method.Invoke(NGame.Instance, values) as Task;
                if (task != null) TaskHelper.RunSafely(task);
                return new(true, "Returning to main menu");
        }
        return new(true, JsonSerializer.Serialize(new { playing = Get("IsPlaying"), paused = Get("IsPaused"), loading = Get("Loading"), index = Get("Index"), title = Get("Title"), position = Get("Position"), status = Get("Status") }));
    }
}
