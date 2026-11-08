using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

// THESIS: a small stage cue rail belongs beside the battle, with the chosen track in charge.
// OWN-WORLD: Karen coral, burgundy stage shadow and warm white; native typography and drawn transport icons.
// STORY: choose a local track, control it in place, or fold the rail to one restore button.
// FIRST VIEW: right-aligned 300×74 rail below the top bar, with its track list tucked underneath.
// FORM: compact persistent game HUD extension; no blocking overlay or added instructional copy.
public partial class NKarenMusicPlayer : Control
{
    internal static NKarenMusicPlayer? Current { get; private set; }
    private static Vector2 _dock = new(26, 158);
    private static readonly Color Ink = Color.FromHtml("f7efdf");
    private static readonly Color Muted = Color.FromHtml("c7b2b5");
    private static readonly Color Coral = Color.FromHtml("fb5458");
    private Panel _card = null!, _library = null!, _notice = null!, _volumePanel = null!;
    private Label _volumeValue = null!;
    private MusicIconButton _volumeIcon = null!, _muteIcon = null!;
    private bool _volumeExpanded;
    private float _unmutedVolume = .7f;
    private Label _noticeTitle = null!;
    private Node? _closeDialog;
    private bool _closePending;
    private double _noticeSeconds;
    private Label _title = null!, _clock = null!, _count = null!, _libraryCount = null!;
    private HSlider _progress = null!, _volume = null!;
    private MusicIconButton _restore = null!, _play = null!, _previous = null!, _next = null!, _shuffle = null!, _mode = null!;
    private VBoxContainer _rows = null!;
    private KarenMusicPlayback _music = null!;
    private bool _expanded, _dragging, _syncing, _seeking, _minDragMoved;
    private Vector2 _minPressPosition;
    private Vector2 _dragOffset;
    private double _clockTick, _reloadAfter;
    private bool _reloadPending;
    private string _lastDirectory = "";
    private readonly List<string> _renderedTracks = new();
    private string _emptyStatus = "";
    private int _selectedRow = -2;

    public override void _Ready()
    {
        Current = this;
        CoreModConfig.MusicPlayerMinimized = true;
        Name = "KarenMusicPlayer";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 70;
        _music = new KarenMusicPlayback(this);
        Build();
        _music.Changed += Refresh;
        _music.TrackStarted += ShowTrackNotice;
        GetViewport().SizeChanged += Place;
        ApplyConfiguration();
    }

    internal static void ConfigurationChanged() => Current?.ApplyConfiguration();

    private void ApplyConfiguration()
    {
        if (_music == null) return;
        bool enabled = CoreModConfig.EnableMusicPlayer;
        Visible = enabled;
        if (!enabled)
        {
            _music.Stop();
            _expanded = false;
            _volumeExpanded = false;
            _notice.Visible = false;
            return;
        }
        if (_lastDirectory != CoreModConfig.MusicDirectory || _music.Tracks.Count == 0)
        {
            _lastDirectory = CoreModConfig.MusicDirectory;
            _music.Stop();
            _reloadPending = true;
            _reloadAfter = .4;
        }
        if (!_reloadPending) MusicModule.RefreshPlayback();
        Refresh();
    }

    private void Build()
    {
        _card = new Panel { Name = "MusicCard", MouseFilter = MouseFilterEnum.Stop };
        _card.AddThemeStyleboxOverride("panel", Surface());
        AddChild(_card);
        _card.GuiInput += Drag;
        _restore = Icon(_card, "Restore", "disc_paused", new(2, 2), new(36, 36), () => { bool dragged = _minDragMoved; _minDragMoved = false; if (!dragged) SetMinimized(false); });
        _restore.GuiInput += DragMinimized;
        _restore.Visible = false;
        var body = new Control { Name = "Body", MouseFilter = MouseFilterEnum.Ignore, Size = new(300, 74) };
        _card.AddChild(body);
        _play = Icon(body, "PlayPause", "play", new(8, 10), new(44, 44), () => _music.Toggle());
        _play.Primary = true;
        _title = LabelAt(body, "TrackTitle", new(60, 5), new(182, 24), 18);
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _clock = LabelAt(body, "Time", new(60, 29), new(92, 20), 12, Muted);
        _previous = Icon(body, "Previous", "previous", new(184, 28), new(24, 24), () => _music.Step(-1));
        _next = Icon(body, "Next", "next", new(212, 28), new(24, 24), () => _music.Step(1));
        _mode = Icon(body, "PlayMode", "list_play", new(246, 28), new(24, 24), CycleMode);
        Icon(body, "Library", "list", new(274, 28), new(22, 24), () => { _expanded = !_expanded; _volumeExpanded = false; Refresh(); });
        Icon(body, "Minimize", "minimize", new(254, 4), new(18, 18), () => SetMinimized(true));
        Icon(body, "Close", "close", new(276, 4), new(18, 18), ConfirmClose);
        _count = LabelAt(body, "TrackCount", new(335, 16), new(56, 22), 14, Muted);
        _count.Visible = false;
        _progress = Slider(body, "Progress", new(58, 56), new(228, 10));
        _progress.DragStarted += () => _seeking = true;
        _progress.DragEnded += changed => { if (changed) _music.Seek(_progress.Value); _seeking = false; UpdateClock(); };
        _progress.ValueChanged += value => { if (!_syncing && !_seeking) { _music.Seek(value); UpdateClock(); } };

        _library = new Panel { Name = "LibraryPanel", Size = new(300, 246), MouseFilter = MouseFilterEnum.Stop };
        _library.AddThemeStyleboxOverride("panel", Surface());
        AddChild(_library);
        LabelAt(_library, "LibraryTitle", new(12, 10), new(150, 22), 16).Text = KarenMusicPlayback.Text("tracks");
        _libraryCount = LabelAt(_library, "LibraryCount", new(178, 10), new(34, 22), 13, Muted);
        _libraryCount.HorizontalAlignment = HorizontalAlignment.Right;
        Icon(_library, "Reload", "reload", new(222, 8), new(26, 26), () => _music.Reload());
        _shuffle = Icon(_library, "Shuffle", "list_play", new(264, 8), new(26, 26), CycleMode);
        _volumeIcon = Icon(body, "VolumeIcon", "volume", new(156, 28), new(22, 24), () => { _volumeExpanded = !_volumeExpanded; _expanded = false; Refresh(); });
        _volumePanel = new Panel { Name = "VolumePanel", Size = new(240, 48), MouseFilter = MouseFilterEnum.Stop, Visible = false };
        _volumePanel.AddThemeStyleboxOverride("panel", Surface());
        AddChild(_volumePanel);
        _muteIcon = Icon(_volumePanel, "VolumeMute", "volume", new(10, 10), new(26, 26), ToggleMute);
        _volume = Slider(_volumePanel, "Volume", new(48, 22), new(126, 10));
        _volume.MaxValue = 100;
        _volume.Step = 1;
        _volumeValue = LabelAt(_volumePanel, "VolumeValue", new(184, 10), new(46, 26), 14, Muted);
        _volumeValue.HorizontalAlignment = HorizontalAlignment.Right;
        _volume.ValueChanged += value =>
        {
            if (_syncing) return;
            CoreModConfig.MusicPlayerVolume = (float)value / 100;
            UpdateVolumeUi();
            SavePreferences();
            _music.UpdateVolumes();
        };
        var scroll = new ScrollContainer { Name = "TrackScroll", Position = new(6, 42), Size = new(288, 194), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _library.AddChild(scroll);
        _rows = new VBoxContainer { Name = "Tracks", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(_rows);
        _notice = new Panel { Name = "TrackNotice", Size = new(280, 32), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _notice.AddThemeStyleboxOverride("panel", Surface());
        AddChild(_notice);
        _noticeTitle = LabelAt(_notice, "TrackNoticeTitle", new(8, 4), new(264, 24), 16);
        _noticeTitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
    }

    private void SetMinimized(bool value)
    {
        CoreModConfig.MusicPlayerMinimized = value;
        SavePreferences();
        _expanded = false;
        _volumeExpanded = false;
        _notice.Visible = false;
        Refresh();
    }

    private void ToggleMute()
    {
        CoreModConfig.MusicPlayerMuted = !CoreModConfig.MusicPlayerMuted;
        UpdateVolumeUi();
        SavePreferences();
        _music.UpdateVolumes();
    }

    private void UpdateVolumeUi()
    {
        float volume = CoreModConfig.MusicPlayerVolume;
        if (volume > 0) _unmutedVolume = volume;
        bool syncing = _syncing; _syncing = true;
        _volume.Value = volume * 100;
        _syncing = syncing;
        _volumeValue.Text = $"{_volume.Value:0}%";
        _volumeIcon.Icon = _muteIcon.Icon = volume > 0 ? "volume" : "volume_muted";
        _volumeIcon.QueueRedraw(); _muteIcon.QueueRedraw();
    }

    public override void _Input(InputEvent input)
    {
        if (!_volumePanel.Visible || input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse) return;
        if (_volumePanel.GetGlobalRect().HasPoint(mouse.GlobalPosition) || _volumeIcon.GetGlobalRect().HasPoint(mouse.GlobalPosition)) return;
        _volumeExpanded = false;
        Refresh();
    }

    private void CycleMode()
    {
        CoreModConfig.MusicPlayMode = (MusicPlayMode)(((int)CoreModConfig.MusicPlayMode + 1) % 3);
        SavePreferences();
        Refresh();
    }

    private void ShowTrackNotice(string title)
    {
        if (!Visible || !CoreModConfig.MusicPlayerMinimized) return;
        _noticeTitle.Text = title;
        _noticeSeconds = 3;
        _notice.Visible = true;
        Place();
        UpdateProcessing();
    }

    private async void ConfirmClose()
    {
        if (_closePending) return;
        _closePending = true;
        try
        {
            var popup = NGenericPopup.Create();
            if (popup == null || NModalContainer.Instance == null) return;
            _closeDialog = popup;
            NModalContainer.Instance.Add(popup);
            bool confirmed = await popup.WaitForConfirmation(
                body: new LocString("settings_ui", "CORE_MUSIC.close_body"),
                header: new LocString("settings_ui", "CORE_MUSIC.close_title"),
                noButton: new LocString("main_menu_ui", "GENERIC_POPUP.cancel"),
                yesButton: new LocString("settings_ui", "CORE_MUSIC.close_action"));
            if (confirmed && GodotObject.IsInstanceValid(this) && IsInsideTree())
            {
                CoreModConfig.EnableMusicPlayer = false;
                BaseLib.Config.ModConfig.SaveDebounced<CoreModConfig>(0);
            }
        }
        catch (Exception error) { CoreLog.Logger.Info($"[MusicPlayer] Close confirmation ended ({error.GetType().Name})."); }
        finally { _closeDialog = null; _closePending = false; }
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!Visible || _closePending || NKarenMusicSetting.IsEditing || _music.FormPriority || input is not InputEventKey { Pressed: true, Echo: false } key) return;
        var focus = GetViewport().GuiGetFocusOwner();
        if (focus is LineEdit or TextEdit || NModalContainer.Instance?.OpenModal != null) return;
        if (Matches(key, CoreModConfig.MusicPlayPauseKey)) _music.Toggle();
        else if (Matches(key, CoreModConfig.MusicNextKey)) _music.Step(1);
        else if (Matches(key, CoreModConfig.MusicPreviousKey)) _music.Step(-1);
        else return;
        GetViewport().SetInputAsHandled();
    }

    internal static bool Matches(InputEventKey key, string binding)
    {
        var parts = (binding ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        bool ctrl = false, alt = false, shift = false, meta = false;
        foreach (string part in parts.Take(parts.Length - 1))
            switch (part.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": ctrl = true; break;
                case "ALT": alt = true; break;
                case "SHIFT": shift = true; break;
                case "META": case "WIN": case "CMD": meta = true; break;
                default: return false;
            }
        Key code = OS.FindKeycodeFromString(parts[^1]);
        return code != Key.None && (key.Keycode == code || key.PhysicalKeycode == code)
            && key.CtrlPressed == ctrl && key.AltPressed == alt && key.ShiftPressed == shift && key.MetaPressed == meta;
    }

    private void Refresh()
    {
        if (!IsInsideTree()) return;
        _syncing = true;
        bool minimized = CoreModConfig.MusicPlayerMinimized;
        _card.GetNode<Control>("Body").Visible = !minimized;
        _restore.Visible = minimized;
        bool playing = _music.IsPlaying || _music.FormPriority;
        _restore.Icon = playing ? "disc_playing" : "disc_paused";
        _restore.Active = playing;
        _restore.QueueRedraw();
        _card.Size = minimized ? new(40, 40) : new(300, 74);
        _title.Text = _music.Title;
        _count.Text = _music.Index < 0 ? "" : $"{_music.Index + 1} / {_music.Tracks.Count}";
        _play.Icon = _music.IsPlaying ? "pause" : "play";
        _play.Disabled = (_music.Tracks.Count == 0 && !_music.HasStream) || _music.Loading || _music.FormPriority;
        _previous.Disabled = _next.Disabled = (_music.Tracks.Count == 0 || (_music.Tracks.Count == 1 && _music.Index >= 0)) || _music.Loading || _music.FormPriority;
        _progress.Editable = _music.Duration > 0 && !_music.Loading && !_music.FormPriority;
        UpdateVolumeUi();
        string modeIcon = CoreModConfig.MusicPlayMode switch { MusicPlayMode.Shuffle => "shuffle", MusicPlayMode.RepeatOne => "repeat_one", _ => "list_play" };
        _shuffle.Icon = _mode.Icon = modeIcon;
        _shuffle.Active = _mode.Active = CoreModConfig.MusicPlayMode != MusicPlayMode.List;
        _libraryCount.Text = _music.Tracks.Count.ToString();
        _library.GetNode<Label>("LibraryTitle").Text = KarenMusicPlayback.Text(_music.UsesBuiltin ? "builtin" : "local");
        _library.Visible = _expanded && !minimized;
        _volumePanel.Visible = _volumeExpanded && !minimized;
        bool rebuild = _library.Visible && (_rows.GetChildCount() == 0 || !_renderedTracks.SequenceEqual(_music.Tracks)
            || (_music.Tracks.Count == 0 && _emptyStatus != _music.Status));
        if (rebuild)
        {
            _renderedTracks.Clear(); _renderedTracks.AddRange(_music.Tracks);
            _emptyStatus = _music.Status;
            _selectedRow = -2;
            foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
            if (_music.Tracks.Count == 0)
            {
                var empty = new Label { Text = KarenMusicPlayback.Text(_music.Status), CustomMinimumSize = new(0, 100), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                empty.AddThemeColorOverride("font_color", Muted);
                empty.AddThemeFontSizeOverride("font_size", 19);
                _rows.AddChild(empty);
            }
            for (int i = 0; i < _music.Tracks.Count; i++)
            {
                int index = i;
                var row = new Button { Name = "Track" + i, Text = $"{i + 1:00}    {_music.TrackTitle(_music.Tracks[i])}", CustomMinimumSize = new(272, 30), Alignment = HorizontalAlignment.Left, ClipText = true, FocusMode = FocusModeEnum.All };
                row.AddThemeFontSizeOverride("font_size", 16);
                row.AddThemeColorOverride("font_color", i == _music.Index ? Ink : Muted);
                row.AddThemeColorOverride("font_hover_color", Ink);
                row.AddThemeColorOverride("font_focus_color", Ink);
                row.AddThemeStyleboxOverride("normal", RowStyle(i == _music.Index ? Color.FromHtml("583038") : Colors.Transparent));
                row.AddThemeStyleboxOverride("hover", RowStyle(Color.FromHtml("50333d")));
                row.AddThemeStyleboxOverride("pressed", RowStyle(Color.FromHtml("76404a")));
                row.AddThemeStyleboxOverride("focus", FocusStyle());
                row.Pressed += () => { _ = _music.Play(index); };
                _rows.AddChild(row);
            }
        }
        // Preserve row focus and scroll position when playback/selection changes.
        if (_selectedRow != _music.Index)
        {
            _selectedRow = _music.Index;
            int i = 0;
            foreach (var row in _rows.GetChildren().OfType<Button>())
            {
                bool selected = i++ == _music.Index;
                row.AddThemeColorOverride("font_color", selected ? Ink : Muted);
                row.AddThemeStyleboxOverride("normal", RowStyle(selected ? Color.FromHtml("583038") : Colors.Transparent));
            }
        }
        foreach (var row in _rows.GetChildren().OfType<Button>()) row.Disabled = _music.FormPriority;
        UpdateClock();
        Place();
        _mode.QueueRedraw();
        _play.QueueRedraw();
        _shuffle.QueueRedraw();
        _syncing = false;
        UpdateProcessing();
    }

    private void UpdateProcessing()
    {
        // Hidden/minimized/paused UI has no clock to animate; transient notices and reloads wake it.
        SetProcess(Visible && (_reloadPending || _notice.Visible
            || (!CoreModConfig.MusicPlayerMinimized && _music.IsPlaying)));
    }

    public override void _Process(double delta)
    {
        if (_reloadPending && Visible && (_reloadAfter -= delta) <= 0)
        {
            _reloadPending = false;
            _music.Reload();
        }
        if (!Visible) return;
        if (_notice.Visible && (_noticeSeconds -= delta) <= 0)
        {
            _notice.Visible = false;
            UpdateProcessing();
        }
        if (CoreModConfig.MusicPlayerMinimized || !_music.IsPlaying) return;
        _clockTick += delta;
        if (_clockTick >= .15) { _clockTick = 0; UpdateClock(); }
    }

    private void UpdateClock()
    {
        _clock.Text = _music.FormPriority ? "" : _music.Status is "loading" or "error" ? KarenMusicPlayback.Text(_music.Status)
            : _music.Duration > 0 ? $"{Time(_music.Position)}  /  {Time(_music.Duration)}"
            : _music.Tracks.Count > 0 ? KarenMusicPlayback.Text("ready") : "";
        if (!_seeking)
        {
            bool syncing = _syncing; _syncing = true;
            _progress.MaxValue = Math.Max(1, _music.Duration);
            _progress.Value = _music.Position;
            _syncing = syncing;
        }
    }

    private void Place()
    {
        Vector2 size = GetViewportRect().Size;
        float height = _card.Size.Y + (_library.Visible ? _library.Size.Y + 8 : _volumePanel.Visible ? _volumePanel.Size.Y + 8 : 0);
        _dock.X = Math.Clamp(_dock.X, 12, Math.Max(12, size.X - _card.Size.X - 12));
        _dock.Y = Math.Clamp(_dock.Y, 86, Math.Max(86, size.Y - height - 16));
        _card.Position = new(size.X - _card.Size.X - _dock.X, _dock.Y);
        _library.Position = _card.Position + new Vector2(0, _card.Size.Y + 8);
        _volumePanel.Position = _card.Position + new Vector2(_card.Size.X - _volumePanel.Size.X, _card.Size.Y + 8);
        float noticeX = _card.Position.X - _notice.Size.X - 8;
        if (noticeX < 8) noticeX = _card.Position.X + _card.Size.X + 8;
        _notice.Position = new(Math.Clamp(noticeX, 8, Math.Max(8, size.X - _notice.Size.X - 8)), _card.Position.Y + 4);
    }

    private void DragMinimized(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            _dragging = click.Pressed;
            if (click.Pressed)
            {
                _minDragMoved = false;
                _minPressPosition = click.GlobalPosition;
                _dragOffset = click.GlobalPosition - _card.GlobalPosition;
            }
        }
        else if (input is InputEventMouseMotion motion && _dragging)
        {
            if (!_minDragMoved && motion.GlobalPosition.DistanceTo(_minPressPosition) < 6) return;
            _minDragMoved = true;
            Vector2 point = motion.GlobalPosition - _dragOffset;
            _dock = new(GetViewportRect().Size.X - point.X - _card.Size.X, point.Y);
            Place();
            _restore.AcceptEvent();
        }
    }

    private void Drag(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            _dragging = click.Pressed;
            _dragOffset = click.GlobalPosition - _card.GlobalPosition;
            if (_dragging) _card.AcceptEvent();
        }
        else if (input is InputEventMouseMotion motion && _dragging)
        {
            Vector2 point = motion.GlobalPosition - _dragOffset;
            _dock = new(GetViewportRect().Size.X - point.X - _card.Size.X, point.Y);
            Place(); _card.AcceptEvent();
        }
    }

    public override void _ExitTree()
    {
        _music.TrackStarted -= ShowTrackNotice;
        GetViewport().SizeChanged -= Place;
        if (GodotObject.IsInstanceValid(_closeDialog))
        {
            if (NModalContainer.Instance?.OpenModal == _closeDialog) NModalContainer.Instance.Clear();
            _closeDialog!.QueueFree();
        }
        _music.Changed -= Refresh;
        _music.Dispose();
        if (Current == this) Current = null;
    }

    private static void SavePreferences() => BaseLib.Config.ModConfig.SaveDebounced<CoreModConfig>();

    private static string Time(double seconds) => $"{(int)Math.Max(0, seconds) / 60}:{(int)Math.Max(0, seconds) % 60:00}";
    private static StyleBoxFlat Surface() => new() { BgColor = Color.FromHtml("281e27fa"), CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8, BorderWidthTop = 2, BorderColor = Coral, ShadowColor = new Color(0, 0, 0, .3f), ShadowSize = 6, ShadowOffset = new(0, 5) };
    private static StyleBoxFlat RowStyle(Color color) => new() { BgColor = color, ContentMarginLeft = 12, ContentMarginRight = 12, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 };
    private static StyleBoxFlat IconStyle(Color color)
    {
        var style = RowStyle(color);
        style.ContentMarginLeft = style.ContentMarginRight = 0;
        return style;
    }
    private static StyleBoxEmpty FocusStyle() => new();
    private static Label LabelAt(Node parent, string name, Vector2 position, Vector2 size, int fontSize, Color? color = null)
    {
        var label = new Label { Name = name, Position = position, Size = size, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeColorOverride("font_color", color ?? Ink);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        parent.AddChild(label); return label;
    }
    private static MusicIconButton Icon(Node parent, string name, string icon, Vector2 position, Vector2 size, Action pressed)
    {
        var button = new MusicIconButton { Name = name, Icon = icon, Position = position, Size = size, FocusMode = FocusModeEnum.All };
        parent.AddChild(button); button.Pressed += pressed; return button;
    }
    private static HSlider Slider(Node parent, string name, Vector2 position, Vector2 size)
    {
        var slider = new HSlider { Name = name, Position = position, Size = size, MinValue = 0, MaxValue = 1, Step = .1, FocusMode = FocusModeEnum.All };
        slider.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        slider.AddThemeStyleboxOverride("slider", new StyleBoxFlat { BgColor = Color.FromHtml("65505b"), ContentMarginTop = 2, ContentMarginBottom = 2 });
        slider.AddThemeStyleboxOverride("grabber_area", new StyleBoxFlat { BgColor = Coral, ContentMarginTop = 2, ContentMarginBottom = 2 });
        slider.AddThemeStyleboxOverride("grabber_area_highlight", new StyleBoxFlat { BgColor = Ink, ContentMarginTop = 2, ContentMarginBottom = 2 });
        slider.AddThemeIconOverride("grabber", new GradientTexture2D { Width = 8, Height = 8, Gradient = new Gradient { Colors = new[] { Ink, Ink } } });
        parent.AddChild(slider); return slider;
    }

    private partial class MusicIconButton : Button
    {
        internal new string Icon = "play";
        internal bool Primary, Active;
        public override void _Ready()
        {
            AddThemeStyleboxOverride("normal", IconStyle(Colors.Transparent));
            AddThemeStyleboxOverride("hover", IconStyle(Color.FromHtml("60404b")));
            AddThemeStyleboxOverride("pressed", IconStyle(Color.FromHtml("80505a")));
            AddThemeStyleboxOverride("focus", FocusStyle());
            MouseEntered += QueueRedraw; MouseExited += QueueRedraw; FocusEntered += QueueRedraw; FocusExited += QueueRedraw;
        }
        public override void _Draw()
        {
            Vector2 c = Size / 2; Color ink = Disabled ? new Color(Muted, .35f) : Active ? Coral : Ink;
            if (Primary) { DrawCircle(c, Math.Min(Size.X, Size.Y) / 2 - 3, Disabled ? new Color(Coral, .35f) : Coral); ink = Disabled ? Muted : Color.FromHtml("281e27"); }
            void Line(float x, float y, float xx, float yy) => DrawLine(c + new Vector2(x, y), c + new Vector2(xx, yy), ink, 2, true);
            void Triangle(float x, float scale = 1) => DrawColoredPolygon(new[] { c + new Vector2(x - 5 * scale, -8 * scale), c + new Vector2(x + 7 * scale, 0), c + new Vector2(x - 5 * scale, 8 * scale) }, ink);
            switch (Icon)
            {
                case "play": Triangle(1); break;
                case "pause": DrawRect(new(c + new Vector2(-7, -8), new(4, 16)), ink); DrawRect(new(c + new Vector2(3, -8), new(4, 16)), ink); break;
                case "previous": DrawColoredPolygon(new[] { c + new Vector2(6, -7), c + new Vector2(-4, 0), c + new Vector2(6, 7) }, ink); Line(-7, -7, -7, 7); break;
                case "next": Triangle(-2, .85f); Line(7, -7, 7, 7); break;
                case "close": Line(-4, -4, 4, 4); Line(4, -4, -4, 4); break;
                case "list_play": Line(-8, -6, 3, -6); Line(-8, 0, 3, 0); Line(-8, 6, 3, 6); DrawColoredPolygon(new[] { c + new Vector2(6, -4), c + new Vector2(11, 0), c + new Vector2(6, 4) }, ink); break;
                case "repeat_one": DrawArc(c, 8, -2.6f, 2.1f, 24, ink, 2, true); Line(7, -8, 7, -1); Line(7, -1, 0, -1); Line(0, -4, 0, 4); Line(-2, -2, 0, -4); break;
                case "minimize": Line(-6, 3, 6, 3); break;
                case "list": for (int i = -1; i <= 1; i++) { Line(-7, i * 5, 7, i * 5); } break;
                case "volume":
                case "volume_muted":
                    DrawColoredPolygon(new[] { c + new Vector2(-8, -3), c + new Vector2(-3, -3), c + new Vector2(2, -8), c + new Vector2(2, 8), c + new Vector2(-3, 3), c + new Vector2(-8, 3) }, ink);
                    if (Icon == "volume_muted") { Line(5, -4, 11, 4); Line(11, -4, 5, 4); }
                    else DrawArc(c + new Vector2(1, 0), 8, -.75f, .75f, 14, ink, 2, true);
                    break;
                case "reload": DrawArc(c, 8, -2.6f, 2f, 24, ink, 2, true); Line(7, -8, 7, -1); Line(7, -1, 0, -1); break;
                case "shuffle": Line(-8, -5, -3, -5); Line(-3, -5, 4, 5); Line(4, 5, 9, 5); Line(-8, 5, -4, 5); Line(3, -5, 9, -5); Line(6, -8, 9, -5); Line(9, -5, 6, -2); Line(6, 2, 9, 5); Line(9, 5, 6, 8); break;
                case "disc_playing":
                case "disc_paused":
                    DrawCircle(c, 16, Color.FromHtml("58323e"));
                    DrawArc(c, 13, 0, Mathf.Tau, 40, Active ? Coral : Muted, 1.5f, true);
                    if (Icon == "disc_playing")
                    {
                        DrawRect(new(c + new Vector2(-5, -6), new(3, 12)), ink);
                        DrawRect(new(c + new Vector2(2, -6), new(3, 12)), ink);
                    }
                    else Triangle(1, .65f);
                    break;
            }
        }
    }
}
