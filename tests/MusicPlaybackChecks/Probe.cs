using Godot;
using ShoujoKageki.Core.Runtime.Modules.Presentation;

public partial class Probe : Node
{
    private KarenMusicPlayback? _music;
    public override async void _Ready()
    {
        try
        {
            var directory = ProjectSettings.GlobalizePath("res://../../out/tmp/music-player-session-resume/tracks");
            System.IO.Directory.CreateDirectory(directory);
            using (var wave = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = 8000, Data = new byte[8000 * 2 * 60] })
            {
                wave.SaveToWav(System.IO.Path.Combine(directory, "one.wav"));
                wave.SaveToWav(System.IO.Path.Combine(directory, "two.wav"));
            }
            CoreModConfig.MusicDirectory = directory;
            _music = Create();
            Check("fresh launch waits for playback", !_music.IsPlaying && !_music.IsPaused);
            await _music.Play(1);
            _music.Seek(12);
            await Frames();
            _music.RememberSession();
            _music.Stop(); // Karen's ReturnToMainMenu prefix runs before node disposal.
            _music.RememberSession(); // Repeated exit hooks must not replace the captured state.
            _music.Dispose();
            _music = Create();
            await Frames();
            Check("playing track resumes after player recreation", _music.IsPlaying && _music.Index == 1);
            Check("playing position resumes", Math.Abs(_music.Position - 12) < 1);
            _music.Toggle();
            _music.Seek(24);
            _music.Dispose();
            _music = Create();
            await Frames();
            Check("paused track stays paused", _music.IsPaused && !_music.IsPlaying && _music.Index == 1);
            Check("paused seek target resumes", Math.Abs(_music.Position - 24) < .1);
            _music.Toggle();
            await Frames();
            Check("paused track can resume at saved target", _music.IsPlaying && Math.Abs(_music.Position - 24) < 1);
            _music.SetFormPriority(true);
            await _music.ForceTrack("res://temporary.wav");
            _music.Dispose();
            _music = Create();
            await Frames();
            Check("temporary music restores interrupted user track", _music.IsPlaying && _music.Index == 1 && Math.Abs(_music.Position - 24) < 1);
            _music.Dispose();
            CoreModConfig.MusicDirectory = System.IO.Path.Combine(directory, "missing");
            _music = Create();
            Check("changed directory does not replay stale track", !_music.IsPlaying && !_music.IsPaused && !_music.HasStream);
            _music.Dispose();
            CoreModConfig.MusicDirectory = directory;
            _music = Create();
            await _music.Play(1);
            _music.Dispose();
            CoreModConfig.EnableMusicPlayer = false;
            _music = Create();
            Check("disabled player stays stopped", !_music.IsPlaying && !_music.IsPaused);
            _music.Dispose();
            CoreModConfig.EnableMusicPlayer = true;
            _music = Create();
            await _music.Play(1);
            _music.Reload();
            await Frames();
            Check("manual library reload does not restore an old session", !_music.IsPlaying && !_music.IsPaused);
            _music.Dispose();
            _music = null;
            await Frames();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames();
            GD.Print("PASS music playback session checks");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.Print("FAIL " + error);
            _music?.Dispose();
            GetTree().Quit(1);
        }
    }
    private KarenMusicPlayback Create()
    {
        var playback = new KarenMusicPlayback(this);
        playback.Reload();
        return playback;
    }
    private async Task Frames()
    {
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private static void Check(string name, bool passed)
    {
        if (!passed) throw new InvalidOperationException(name);
        GD.Print("PASS " + name);
    }
}
