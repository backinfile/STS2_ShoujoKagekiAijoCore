using Godot;
using System;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

public static class KarenResourceLoader
{
    public static T? Load<T>(string path, string owner) where T : Resource
    {
        var resource = ResourceLoader.Load<T>(path);
        if (resource == null)
            GD.PrintErr($"[{owner}] 资源加载失败: {path}");

        return resource;
    }

    public static Texture2D? LoadTexture(string path, string owner)
    {
        return Load<Texture2D>(path, owner);
    }

    public static AudioStream? LoadAudioStream(string path, string fileName, string owner)
    {
        var stream = ResourceLoader.Load<AudioStream>(path);
        if (stream != null)
            return stream;

        stream = LoadAudioStreamFromFile(path, fileName, owner);
        if (stream == null)
            GD.PrintErr($"[{owner}] 音频解码失败: {path}");

        return stream;
    }

    private static AudioStream? LoadAudioStreamFromFile(string path, string fileName, string owner)
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PrintErr($"[{owner}] 音频资源加载失败，且源文件不存在: {path}");
            return null;
        }

        var bytes = Godot.FileAccess.GetFileAsBytes(path);
        if (bytes.Length == 0)
        {
            GD.PrintErr($"[{owner}] 音频文件读取失败: {path}");
            return null;
        }

        if (fileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            return AudioStreamOggVorbis.LoadFromBuffer(bytes);

        if (fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            return new AudioStreamMP3 { Data = bytes };

        if (fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            return AudioStreamWav.LoadFromFile(path) ?? AudioStreamWav.LoadFromBuffer(bytes);

        GD.PrintErr($"[{owner}] 不支持的音频格式: {fileName}");
        return null;
    }
}
