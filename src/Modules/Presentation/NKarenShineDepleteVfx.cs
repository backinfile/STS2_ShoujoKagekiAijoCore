using ShoujoKageki.Core.Runtime.Modules.Presentation;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using System.Linq;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

/// <summary>Owns only the detached visual; model depletion remains in ShinePileManager.</summary>
public partial class NKarenShineDepleteVfx : Node2D
{
    public override void _EnterTree() => ShoujoKageki.Core.V1.Presentation.CoreEffects.Register(this, static () => CoreModConfig.EnableShineEffects);

    private NCard _card = null!;
    private CardModel _model = null!;
    private Control _target = null!;
    private NTopBarShinePileButton? _pileButton;
    private NCombatRoom _room = null!;
    private Transform2D _source;
    private float _age;
    private float _gatherDuration;
    private bool _released;
    private bool _arrived;
    private bool _fuel;
    private bool _cardVisible;
    private Color _cardColor;
    private Control.MouseFilterEnum _mouseFilter;
    private readonly Vector2[] _star = new Vector2[8];
    private static readonly Color Gold = new("#ffc96c");
    private static readonly Color Hot = new("#fff5dc");

    public static bool IsAnimating(CardModel model) => NRun.Instance?.GlobalUi.GetChildren()
        .OfType<NKarenShineDepleteVfx>().Any(v => v._model == model && !v.IsQueuedForDeletion()) == true;

    public static bool Play(CardModel model, NCard? card)
    {
        if (!KarenVfxQuality.Enabled) return false;
        var room = NCombatRoom.Instance;
        var ui = NRun.Instance?.GlobalUi;
        var target = ui?.TopBar.GetNodeOrNull<Control>("RightAlignedStuff/ShinePile/Control/Icon");
        if (target == null || card == null || room == null || !LocalContext.IsMine(model)) return false;
        var source = card.GetGlobalTransformWithCanvas();
        var effect = new NKarenShineDepleteVfx
        {
            _card = card, _model = model, _target = target, _source = source, _room = room,
            _pileButton = target.GetParent()?.GetParent() as NTopBarShinePileButton,
            _mouseFilter = card.MouseFilter,
            _cardVisible = card.Visible,
            _cardColor = card.Modulate,
            _fuel = false
        };
        var speed = SaveManager.Instance.PrefsSave.FastMode;
        effect._gatherDuration = effect._fuel
            ? (speed == FastModeType.Instant ? .025f : speed == FastModeType.Fast ? .07f : .10f)
            : (speed == FastModeType.Instant ? .07f : speed == FastModeType.Fast ? .24f : .34f);
        ui!.AddChild(effect);
        effect._pileButton?.BeginDepletion(effect);
        // Removing the visual from the play container prevents native RemoveFromCombat
        // from taking ownership a second time. This node returns it to the pool exactly once.
        card.PlayPileTween?.Kill();
        card.PlayPileTween = null;
        card.Reparent(effect);
        card.MouseFilter = Control.MouseFilterEnum.Ignore;
        // Keep a normal card at its original position during the crossfade. Fuel
        // has already burst, so its face must never reappear for this transition.
        card.Visible = effect._cardVisible && !effect._fuel;
        effect.PlaceCard(source);
        return true;
    }

    public override void _Ready()
    {
        ZIndex = 110;
        TopLevel = true;
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        GlobalTransform = GetCanvasTransform().AffineInverse();
    }

    private void PlaceCard(Transform2D canvasTransform)
    {
        var local = GetGlobalTransformWithCanvas().AffineInverse() * canvasTransform;
        _card.Position = local.Origin;
        _card.Rotation = local.Rotation;
        _card.Scale = local.Scale;
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_room) || NCombatRoom.Instance != _room
            || !GodotObject.IsInstanceValid(_target) || !_target.IsInsideTree() || !CombatManager.Instance.IsInProgress)
        { Release(); QueueFree(); return; }
        _age += (float)delta;
        float gather = Ease(_age / _gatherDuration);
        if (!_released && GodotObject.IsInstanceValid(_card) && ReferenceEquals(_card.Model, _model))
        {
            _card.Modulate = new Color(_cardColor, _cardColor.A * (1 - gather));
            _card.Visible = _cardVisible && !_fuel && _age < _gatherDuration;
            float shrink = Mathf.Lerp(1, .10f, gather);
            PlaceCard(new Transform2D(_source.X * shrink, _source.Y * shrink, _source.Origin));
        }
        if (_age >= _gatherDuration)
        {
            // The fixed-position gather now hands straight to the existing icon
            // feedback. No travel head, trail, or invisible flight-time delay.
            _arrived = true;
            Release();
            QueueFree();
            return;
        }
        GlobalTransform = GetCanvasTransform().AffineInverse();
        KarenVfxQuality.Redraw(this);
    }

    public override void _Draw()
    {
        float gather = Ease(_age / _gatherDuration);
        if (!_fuel && _age < _gatherDuration)
        {
            // A loose, card-sized outer cloud curls inward as the actual face shrinks.
            // The whole silhouette participates into a compact light at the original card position.
            for (int i = 0; i < 40; i += KarenVfxQuality.ParticleStride)
            {
                float u = Mathf.Clamp((gather - i % 5 * .022f) / (1 - i % 5 * .022f), 0, 1);
                float angle = i * 2.399963f;
                var offset = new Vector2(Mathf.Cos(angle) * (180 + i % 4 * 18), Mathf.Sin(angle) * (245 + i % 5 * 16));
                var edge = _source * offset - _source.Origin;
                var tangent = new Vector2(-edge.Y, edge.X).Normalized() * (22 + i % 7 * 7);
                var point = _source.Origin + edge * (1-u) + tangent * Mathf.Sin(u * Mathf.Pi);
                float previous = Mathf.Max(0, u - .09f);
                var tail = _source.Origin + edge * (1-previous) + tangent * Mathf.Sin(previous * Mathf.Pi);
                float light = Mathf.Sin(u * Mathf.Pi) * (.55f + i % 3 * .12f);
                DrawLine(tail, point, new Color(Gold, light * .15f), 8, true);
                DrawLine(tail, point, new Color(Gold, light), 2.2f, true);
                DrawCircle(point, i % 4 == 0 ? 4 : 2.2f, new Color(Hot, light));
            }
        }
        var p = _source.Origin;
        float headLight = gather;
        float r = 17 * (.65f + .35f * gather);
        for (int layer = 8; layer > 0; layer--)
            DrawCircle(p, r * 2 * layer / 8f, new Color(Gold, headLight * .22f * Mathf.Pow(1 - layer / 9f, 2)));
        for (int i = 0; i < _star.Length; i++)
        {
            float angle = i * Mathf.Pi / 4 - Mathf.Pi / 2;
            _star[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (i % 2 == 0 ? 1 : .20f);
        }
        if (headLight > .001f && r > .2f)
        {
            // Triangulate at unit scale: shrinking absolute screen coordinates
            // eventually merges vertices at the last, almost-transparent frame.
            DrawSetTransform(p, 0, Vector2.One * r);
            DrawColoredPolygon(_star, new Color(Hot, headLight));
            DrawSetTransform(Vector2.Zero);
        }
    }

    private static float Ease(float progress)
    {
        float t = Mathf.Clamp(progress, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private void Release()
    {
        if (_released) return;
        _released = true;
        if (GodotObject.IsInstanceValid(_pileButton) && _pileButton!.IsInsideTree())
            _pileButton.FinishDepletion(this, _arrived);
        if (GodotObject.IsInstanceValid(_card) && ReferenceEquals(_card.Model, _model))
        {
            _card.MouseFilter = _mouseFilter;
            _card.Visible = _cardVisible;
            _card.Modulate = _cardColor;
            GodotTreeExtensions.QueueFreeSafely(_card);
        }
    }
    public override void _ExitTree() => Release();
}
