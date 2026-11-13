using System;
using System.IO;
using System.Reflection;
using BaseLib.Config;
using BaseLib.Config.UI;
using Godot;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

// Keep settings as actions: choose a folder and press a key, rather than enter a syntax.
public partial class NKarenMusicSetting : HBoxContainer
{
    private static NKarenMusicSetting? _editor;
    internal static bool IsEditing => GodotObject.IsInstanceValid(_editor) && _editor!.IsVisibleInTree();
    private readonly CoreModConfig _config;
    private readonly PropertyInfo _property;
    private readonly NConfigButton _button, _reset;
    private readonly Label? _path;
    private readonly bool _directory;
    private FileDialog? _dialog;
    private Key _releaseKey;

    public NKarenMusicSetting(CoreModConfig config, PropertyInfo property, Func<string, Action, NConfigButton> button)
    {
        _config = config;
        _property = property;
        _directory = property.Name == nameof(CoreModConfig.MusicDirectory);
        Name = property.Name + "Control";
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        SizeFlagsVertical = SizeFlags.Fill;
        CustomMinimumSize = new(_directory ? 610 : 540, 64);
        AddThemeConstantOverride("separation", 10);
        if (_directory)
        {
            _path = new Label { Name = "SelectedDirectory", CustomMinimumSize = new(300, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore };
            _path.AddThemeFontSizeOverride("font_size", 24);
            AddChild(_path);
        }
        _button = button("", _directory ? ChooseDirectory : BeginCapture);
        _button.Name = _directory ? "BrowseDirectory" : "CaptureKey";
        _button.CustomMinimumSize = new(_directory ? 180 : 300, 64);
        AddChild(_button);
        if (!_directory)
        {
            var clear = button(KarenMusicPlayback.Text("clear"), () => { _editor?.EndEditing(); Save(""); });
            clear.Name = "ClearKey";
            clear.CustomMinimumSize = new(110, 64);
            AddChild(clear);
        }
        _reset = button(KarenMusicPlayback.Text(_directory ? "clear" : "reset"), Reset);
        _reset.Name = _directory ? "ClearDirectory" : "ResetKey";
        _reset.CustomMinimumSize = new(110, 64);
        AddChild(_reset);
        Refresh();
    }

    public override void _Ready()
    {
        _config.OnConfigReloaded += EndEditing;
        VisibilityChanged += OnVisibilityChanged;
        SetProcessInput(false);
    }

    private void Refresh()
    {
        string value = (string?)_property.GetValue(null) ?? "";
        if (_path != null) { _path.Text = value.Length == 0 ? KarenMusicPlayback.Text("builtin") : value; _path.TooltipText = value; }
        SetButtonText(_directory ? KarenMusicPlayback.Text("browse") : value.Length == 0 ? KarenMusicPlayback.Text("unbound") : value);
    }

    private void SetButtonText(string text) => _button.GetNode<Label>("Label").Text = text;
    private void Save(string value)
    {
        _property.SetValue(null, value);
        _config.Changed();
        ModConfig.SaveDebounced<CoreModConfig>();
        Refresh();
    }

    private void Reset()
    {
        _editor?.EndEditing();
        string value = _directory ? "" : _property.Name switch { nameof(CoreModConfig.MusicNextKey) => "F9", nameof(CoreModConfig.MusicPreviousKey) => "F7", _ => "F8" };
        if (!_directory)
        {
            var key = new InputEventKey { Keycode = OS.FindKeycodeFromString(value) };
            foreach (string other in new[] { nameof(CoreModConfig.MusicPlayPauseKey), nameof(CoreModConfig.MusicNextKey), nameof(CoreModConfig.MusicPreviousKey) })
                if (other != _property.Name && NKarenMusicPlayer.Matches(key, (string?)typeof(CoreModConfig).GetProperty(other)!.GetValue(null) ?? ""))
                { SetButtonText(KarenMusicPlayback.Text("key_used")); return; }
        }
        Save(value);
    }

    private void BeginCapture()
    {
        _editor?.EndEditing();
        _editor = this;
        SetButtonText(KarenMusicPlayback.Text("press_key"));
        SetProcessInput(true);
    }

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey key) return;
        Key released = key.Keycode == Key.None ? key.PhysicalKeycode : key.Keycode;
        if (_releaseKey != Key.None && !key.Pressed && released == _releaseKey)
        {
            GetViewport().SetInputAsHandled();
            _releaseKey = Key.None;
            SetProcessInput(_editor == this);
            return;
        }
        if (_editor != this || _directory) return;
        GetViewport().SetInputAsHandled();
        if (!key.Pressed || key.Echo) return;
        Key code = key.Keycode == Key.None ? key.PhysicalKeycode : key.Keycode;
        if (code == Key.Escape) { FinishKey(code); return; }
        if (code is Key.None or Key.Ctrl or Key.Alt or Key.Shift or Key.Meta) return;
        string binding = (key.CtrlPressed ? "Ctrl+" : "") + (key.AltPressed ? "Alt+" : "") + (key.ShiftPressed ? "Shift+" : "") + (key.MetaPressed ? "Meta+" : "") + OS.GetKeycodeString(code);
        foreach (string other in new[] { nameof(CoreModConfig.MusicPlayPauseKey), nameof(CoreModConfig.MusicNextKey), nameof(CoreModConfig.MusicPreviousKey) })
            if (other != _property.Name && NKarenMusicPlayer.Matches(key, (string?)typeof(CoreModConfig).GetProperty(other)!.GetValue(null) ?? ""))
            { SetButtonText(KarenMusicPlayback.Text("key_used")); return; }
        FinishKey(code);
        Save(binding);
    }

    private void FinishKey(Key code)
    {
        EndEditing();
        _releaseKey = code;
        SetProcessInput(true);
    }

    private void ChooseDirectory()
    {
        _editor?.EndEditing();
        _editor = this;
        _dialog = new FileDialog { Name = "MusicDirectoryDialog", Title = KarenMusicPlayback.Text("browse"), Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenDir, UseNativeDialog = true, MinSize = new(800, 520) };
        AddChild(_dialog);
        string value = CoreModConfig.MusicDirectory;
        if (Directory.Exists(value)) _dialog.CurrentDir = value;
        _dialog.DirSelected += path => { if (IsInsideTree()) Save(path); EndEditing(); };
        _dialog.Canceled += EndEditing;
        _dialog.PopupCenteredRatio(.7f);
    }

    private void EndEditing()
    {
        if (_editor == this) _editor = null;
        SetProcessInput(false);
        if (GodotObject.IsInstanceValid(_dialog)) _dialog!.QueueFree();
        _dialog = null;
        Refresh();
    }
    private void OnVisibilityChanged() { if (!IsVisibleInTree() && _editor == this) EndEditing(); }
    public override void _ExitTree()
    {
        _config.OnConfigReloaded -= EndEditing;
        if (_editor == this) _editor = null;
    }
}
