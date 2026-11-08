using ShoujoKageki.Core.Runtime.Modules.Presentation;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

public partial class NTopBarShinePileButton : MegaCrit.Sts2.Core.Nodes.TopBar.NTopBarButton
{
    private static readonly StringName _v = new StringName("v");
    private static readonly HoverTip _hoverTip = new HoverTip(
        new LocString("gameplay_ui", "CORE_SHINE_PILE_BUTTON_TITLE"),
        new LocString("gameplay_ui", "CORE_SHINE_PILE_BUTTON_DESC"));

    private const float _defaultV = 0.9f;
    private Player? _player;
    private Label _countLabel = null!;
    private int _count, _modelCount, _countTarget;
    private float _numberStepClock;
    private readonly HashSet<NKarenShineDepleteVfx> _inFlight = new();
    private Tween? _bumpTween;
    private NKarenShinePileArrivalVfx? _arrivalVfx;

    public void BeginDepletion(NKarenShineDepleteVfx flight) => _inFlight.Add(flight);

    public void FinishDepletion(NKarenShineDepleteVfx flight, bool arrived)
    {
        if (!_inFlight.Remove(flight)) return;
        RefreshCount(arrived);
        if (arrived) PlayDepletionArrival();
    }

    public override void _Process(double delta)
    {
        if (_player != null)
        {
            var mode = CoreApi.Configuration.Current.ShinePileButton;
            Visible = mode != ShinePileButtonMode.Hidden && (mode == ShinePileButtonMode.Always
                || CoreDiscovery.IsShoujoCharacter(_player.Character));
        }
        _numberStepClock = Mathf.Max(0, _numberStepClock-(float)delta);
        if (_count < _countTarget && _numberStepClock <= 0)
            ShowCount(_count+1, true);
    }

    public void PlayDepletionArrival()
    {
        if (!KarenVfxQuality.Enabled) return;
        if (GodotObject.IsInstanceValid(_arrivalVfx) && !_arrivalVfx!.IsQueuedForDeletion())
            _arrivalVfx.Restart();
        else
        {
            _arrivalVfx = new NKarenShinePileArrivalVfx((TextureRect)_icon, GetNode<Control>("Control"));
            AddChild(_arrivalVfx);
        }
    }

    public override void _Ready()
    {
        InitTopBarButton();
        _countLabel = GetNode<Label>("ShinePileCardCount");
        CoreLog.Logger.Info($"[ShinePileButton] _Ready: countLabel type={_countLabel.GetType().Name}");
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        _inFlight.Clear();
        _bumpTween?.Kill();
        if (_player != null)
        {
            var pile = ShinePileManager.GetShinePile(_player);
            pile.ContentsChanged -= OnPileContentsChanged;
        }
    }

    public void Initialize(Player player)
    {
        if (_player != null)
            ShinePileManager.GetShinePile(_player).ContentsChanged -= OnPileContentsChanged;
        _inFlight.Clear();
        _player = player;
        var pile = ShinePileManager.GetShinePile(player);
        pile.ContentsChanged += OnPileContentsChanged;
        RefreshCount(false);
        CoreLog.Logger.Info($"[ShinePileButton] Initialized for player {player.Character.Id.Entry}, count={ShinePileManager.GetShinePileCount(player)}");
    }

    private void OnPileContentsChanged()
    {
        if (_player == null) return;
        CoreLog.Logger.Info($"[ShinePileButton] OnPileContentsChanged: count={ShinePileManager.GetShinePileCount(_player)}, displayed={_count}");
        RefreshCount(true);
    }

    private void RefreshCount(bool animate)
    {
        if (_player == null) return;
        // The model settles immediately; its visual count waits for outstanding flights.
        _modelCount = ShinePileManager.GetShinePileCount(_player);
        _countTarget = Mathf.Max(0, _modelCount-_inFlight.Count);
        if (!animate || _countTarget < _count)
            ShowCount(_countTarget, false);
        else if (_count < _countTarget && _numberStepClock <= 0)
            ShowCount(_count+1, true);
    }

    private void ShowCount(int count, bool animate)
    {
        animate &= KarenVfxQuality.Enabled;
        _count = count;
        if (_countLabel is MegaLabel megaLabel) megaLabel.SetTextAutoSize(count.ToString());
        else _countLabel.Text = count.ToString();
        _countLabel.PivotOffset = _countLabel.Size*.5f;
        _bumpTween?.Kill();
        if (animate)
        {
            // Leave successive increments readable, with a softer grow/hold/settle.
            _numberStepClock = .18f;
            _bumpTween = CreateTween();
            _bumpTween.TweenProperty(_countLabel,"scale",Vector2.One*1.24f,.10f)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            _bumpTween.TweenInterval(.11f);
            _bumpTween.TweenProperty(_countLabel,"scale",Vector2.One,.42f)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
        }
        else
        {
            _numberStepClock = 0;
            _countLabel.Scale = Vector2.One;
        }
    }

    protected override void OnRelease()
    {
        base.OnRelease();
        if (IsOpen())
        {
            NCapstoneContainer.Instance?.Close();
            CoreLog.Logger.Info("[ShinePileButton] Closing existing screen");
        }
        else if (_player != null)
        {
            if (NCapstoneContainer.Instance == null)
            {
                CoreLog.Logger.Error("[ShinePileButton] NCapstoneContainer.Instance is null, cannot open screen");
                return;
            }
            CoreLog.Logger.Info($"[ShinePileButton] Opening ShinePile screen for player {_player.Character.Id.Entry}");
            ShinePileManager.ShowScreen(_player);
        }
        else
        {
            CoreLog.Logger.Warn("[ShinePileButton] _player is null, cannot open screen");
        }
        UpdateScreenOpen();
        _hsv?.SetShaderParameter(_v, 0.9f);
    }

    protected override bool IsOpen()
    {
        var screen = NCapstoneContainer.Instance?.CurrentCapstoneScreen;
        if (screen is not Node node) return false;
        return node.Name.ToString().StartsWith("NCardPileScreen-ShinePile");
    }

    public void ToggleAnimState()
    {
        UpdateScreenOpen();
    }

    protected override void OnFocus()
    {
        base.OnFocus();
        var nHoverTipSet = NHoverTipSet.CreateAndShow(this, _hoverTip);
        if (nHoverTipSet != null)
            nHoverTipSet.GlobalPosition = base.GlobalPosition + new Vector2(base.Size.X - nHoverTipSet.Size.X, base.Size.Y + 20f);
    }

    protected override void OnUnfocus()
    {
        base.OnUnfocus();
        NHoverTipSet.Remove(this);
    }

    protected override async Task AnimHover(CancellationTokenSource cancelToken)
    {
        float timer = 0f;
        float startAngle = _icon.Rotation;
        for (; timer < 0.5f; timer += (float)GetProcessDeltaTime())
        {
            if (cancelToken.IsCancellationRequested) return;
            _icon.Rotation = Mathf.LerpAngle(startAngle, Mathf.Pi, Ease.BackOut(timer / 0.5f));
            if (!this.IsValid() || !IsInsideTree()) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _icon.Rotation = Mathf.Pi;
    }

    protected override async Task AnimUnhover(CancellationTokenSource cancelToken)
    {
        float timer = 0f;
        float startAngle = _icon.Rotation;
        for (; timer < 1f; timer += (float)GetProcessDeltaTime())
        {
            if (cancelToken.IsCancellationRequested) return;
            _icon.Rotation = Mathf.LerpAngle(startAngle, 0f, Ease.ElasticOut(timer / 1f));
            _hsv?.SetShaderParameter(_v, Mathf.Lerp(1.1f, 1f, Ease.ExpoOut(timer / 1f)));
            _icon.Scale = _hoverScale.Lerp(Vector2.One, Ease.ExpoOut(timer / 1f));
            if (!this.IsValid() || !IsInsideTree()) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _hsv?.SetShaderParameter(_v, 1f);
        _icon.Rotation = 0f;
        _icon.Scale = Vector2.One;
    }
}
