using ShoujoKageki.Core.V1.Presentation;
using Godot;
using ShoujoKageki.Core.Runtime.Modules.Presentation;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

/// <summary>Last-light embers follow the card from hand through the whole play.</summary>
public partial class NKarenShineWarningVfx(NCard card, CardModel model) : Node2D
{
    public override void _EnterTree() => ShoujoKageki.Core.V1.Presentation.CoreEffects.Register(this, static () => CoreModConfig.EnableShineEffects);

    private float _age;
    private bool _warnedInHand;
    private bool _hasPose;
    private Transform2D _previousPose;
    private Vector2 _lightWorld, _lightVelocity;
    private readonly ShineCardBorder _border = new(card);
    private float _spawnClock;
    private int _sparkIndex;
    private struct Spark(Vector2 position, Vector2 velocity, float life, float radius, float phase)
    {
        public Vector2 Position = position, Velocity = velocity;
        public float Age;
        public readonly float Life = life, Radius = radius, Phase = phase;
    }
    private readonly List<Spark> _sparks = new(40);
    public bool BelongsTo(CardModel? candidate) => ReferenceEquals(candidate, model);
    public override void _Ready() => Visible = false;

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(card) || !BelongsTo(card.Model)) { Hide(); QueueFree(); return; }
        if (!KarenVfxQuality.Enabled || !card.IsVisibleInTree())
        {
            Visible = false; _sparks.Clear(); _hasPose = false; _spawnClock = 0;
            return;
        }
        bool inHand = model.Pile?.Type == PileType.Hand;
        bool inPlay = model.Pile?.Type == PileType.Play;
        if (inHand) _warnedInHand = ShineVfx.WillDeplete(model);
        // Shine is decremented at the start of OnPlayWrapper. Keep the hand warning
        // during queue/Play transitions, without mistaking a 2→1 play for depletion.
        Visible = card.DisplayingPile is PileType.Hand or PileType.Play
            && ((inHand && _warnedInHand) || (inPlay && (_warnedInHand || ShineVfx.WillDeplete(model, shineAlreadyDecreased: true))));
        if (!Visible) { _sparks.Clear(); _hasPose = false; return; }
        float dt = Mathf.Min((float)delta, .05f);
        _age += dt;
        _border.Update(this);
        var pose = GetGlobalTransformWithCanvas();
        float unit = Mathf.Max(.1f, pose.Scale.Length() / Mathf.Sqrt(2));
        var target = pose * (_border.TopCenter + new Vector2(28 + Mathf.Sin(_age * 1.9f) * 12, -22 + Mathf.Sin(_age * 2.7f + .8f) * 7));
        if (!_hasPose) { _lightWorld = target; _lightVelocity = Vector2.Zero; _previousPose = pose; }
        _lightVelocity = (_lightVelocity + (target - _lightWorld) * (110 * dt)) * Mathf.Exp(-13 * dt);
        _lightWorld += _lightVelocity * dt;
        // A little inertia, bounded to this card even during the fast hand→Play tween.
        _lightWorld = target + (_lightWorld - target).LimitLength(38 * unit);
        var inherited = ((pose.Origin - _previousPose.Origin) / Mathf.Max(dt, .001f)).LimitLength(150 * unit) * .12f;
        _previousPose = pose; _hasPose = true;
        for (int i = _sparks.Count - 1; i >= 0; i--) {
            var spark = _sparks[i]; spark.Age += dt;
            spark.Velocity += new Vector2(Mathf.Sin(spark.Age * 6 + spark.Phase) * 12, -6) * (unit * dt);
            spark.Position += spark.Velocity * dt;
            if (spark.Age >= spark.Life) _sparks.RemoveAt(i);
            else _sparks[i] = spark;
        }
        _spawnClock += dt;
        float interval = KarenVfxQuality.SpawnInterval(.025f);
        while (_spawnClock >= interval) {
            _spawnClock -= interval;
            if (_sparks.Count >= KarenVfxQuality.ParticleCount(40)) break;
            int i = _sparkIndex++; float side = i % 2 == 0 ? -1 : 1;
            var origin = _border.At(i * .381966f);
            var velocity = pose.BasisXform(new Vector2(side * (7 + i * 13 % 20), -32 - i * 19 % 42)) + inherited;
            _sparks.Add(new Spark(pose * origin, velocity, .48f + i % 7 * .045f, (1.5f + i % 3 * .65f) * unit, i * 2.39996f));
        }
        KarenVfxQuality.Redraw(this);
    }

    public override void _Draw()
    {
        var drawBatch = ShineVfx.DrawBatch;
        var canvas = GetGlobalTransformWithCanvas();
        drawBatch.Clear(1f / Mathf.Max(.1f, canvas.X.Length()));
        float pulse = .5f + .5f * Mathf.Sin(_age * 3.6f);
        var gold = new Color("#ffcb62"); var hot = new Color("#fff4ce");
        if (_border.Outline.Length > 1)
        {
            drawBatch.Polyline(_border.Outline, new Color(gold, .08f + pulse * .10f), 20);
            drawBatch.Polyline(_border.Outline, new Color(gold, .25f + pulse * .25f), 7);
            for (int j = 0; j < 10; j += KarenVfxQuality.ParticleStride)
            {
                float t = Mathf.PosMod(_age * (.065f + j * .003f) + j * .101f, 1);
                float ember = Mathf.Sin(t * Mathf.Pi) * (.65f + .35f * Mathf.Sin(_age * 3.6f - j * .7f));
                drawBatch.Polyline(_border.Segment(t, 18 + j % 3 * 6), new Color(hot, ember * .9f), 3.5f);
            }
        }
        var lastLight = canvas.AffineInverse() * _lightWorld;
        float radius = 13 + pulse * 5;
        for (int layer = 8; layer > 0; layer--)
            drawBatch.Circle(lastLight, radius * 2 * layer / 8f,
                new Color(gold, (.12f + pulse * .12f) * Mathf.Pow(1 - layer / 9f, 2)));
        drawBatch.Star(lastLight, radius, .23f, new Color(hot, .75f + pulse * .25f),
            Mathf.Sin(_age * 1.5f) * .12f);
        drawBatch.SetTransform(canvas.AffineInverse());
        foreach (var spark in _sparks)
        {
            float t = spark.Age / spark.Life;
            float a = Mathf.Sin(Mathf.Min(1, t * 5) * Mathf.Pi / 2) * Mathf.Pow(1 - t, 1.2f);
            drawBatch.Circle(spark.Position, spark.Radius * 2.4f, new Color(gold, a * .16f));
            drawBatch.Line(spark.Position - spark.Velocity * .055f, spark.Position,
                new Color(gold, a * .7f), spark.Radius * .7f);
            drawBatch.Circle(spark.Position, spark.Radius, new Color(hot, a));
        }
        drawBatch.Flush(this);
    }
}
