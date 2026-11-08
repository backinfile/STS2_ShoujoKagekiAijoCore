using ShoujoKageki.Core.V1.Presentation;
using Godot;

using System.Threading;
using System.Threading.Tasks;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

/// <summary>
/// Karen Mod 自定义启动画面（Splash Screen）。
/// 在官方 MegaCrit Slash 动画之前显示：黑色背景 + 旋转长颈鹿 Logo + Revue 音效。
/// 支持点击/按键跳过，10 秒后自动结束。
/// </summary>
public partial class KarenSplashScreen : Control
{
    private const string ImagePath = "res://ShoujoKagekiAijoCore/images/ui/giraffe.png";
    private const string SoundFile = "karen_revue.ogg";
    private const float Duration = 10f;
    private const float RotationSpeed = 360f / 60f * 4f; // 每秒旋转角度，与 STS1 一致
    private const float LogoScale = 0.4f;

    private Sprite2D _sprite = null!;
    private AudioStreamPlayer? _audioPlayer;
    private float _timeLeft;
    private bool _isDone;

    /// <summary>创建并播放 Splash，完成后自动从父节点移除。</summary>
    public static async Task Play(Control parent, CancellationToken token)
    {
        var splash = new KarenSplashScreen();
        splash.SetAnchorsPreset(LayoutPreset.FullRect);
        parent.AddChild(splash);

        await splash.Run(token);

        splash.QueueFree();
    }

    public override void _Ready()
    {
        // 黑色全屏背景
        var bg = new ColorRect
        {
            Color = Colors.Black,
            AnchorRight = 1,
            AnchorBottom = 1
        };
        AddChild(bg);

        // 长颈鹿 Logo
        var texture = GD.Load<Texture2D>(ImagePath);
        _sprite = new Sprite2D
        {
            Texture = texture,
            Centered = true
        };
        AddChild(_sprite);

        // 计算缩放和位置（参照 STS1：按屏幕比例缩放，居中）
        var screenSize = GetViewportRect().Size;
        float baseScale = Mathf.Min(
            screenSize.X / texture.GetWidth(),
            screenSize.Y / texture.GetHeight()
        ) * LogoScale;
        _sprite.Scale = Vector2.One * baseScale;
        _sprite.Position = screenSize / 2;

        // 播放 Revue 音效
        _audioPlayer = new AudioStreamPlayer { Stream = CoreResources.LoadAudioStream("res://ShoujoKagekiAijoCore/audio/" + SoundFile, SoundFile, nameof(KarenSplashScreen)), Bus = "Master" };
        AddChild(_audioPlayer);
        _audioPlayer.Play();
    }

    public async Task Run(CancellationToken token)
    {
        _timeLeft = Duration;
        _isDone = false;

        while (_timeLeft > 0 && !_isDone && !token.IsCancellationRequested)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            _timeLeft -= (float)GetProcessDeltaTime();
            _sprite.RotationDegrees += RotationSpeed * (float)GetProcessDeltaTime();
            if (_sprite.RotationDegrees > 360)
                _sprite.RotationDegrees -= 360;
        }

        _audioPlayer?.Stop();
        _audioPlayer?.QueueFree();
    }

    public override void _Input(InputEvent @event)
    {
        // 点击或按键跳过（与 STS1 逻辑一致）
        if (@event is InputEventMouseButton { Pressed: true }
            || @event is InputEventScreenTouch { Pressed: true, Canceled: false }
            || @event.IsActionPressed("ui_accept")
            || @event.IsActionPressed("ui_cancel"))
        {
            _isDone = true;
        }
    }
}
