using Godot;
using ShoujoKageki.Core.Runtime.Modules.Presentation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using System.Collections.Generic;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

/// <summary>Quiet, persistent card light; the last-Shine warning owns the stronger state.</summary>
public partial class NKarenShineAmbientVfx(NCard card, CardModel model, NKarenShineWarningVfx warning) : Node2D
{
    private float _age, _spawnClock, _phase;
    private int _sparkIndex;
    private bool _hasPose;
    private Transform2D _previousPose;
    private Vector2 _lightWorld;
    private readonly ShineCardBorder _border = new(card);
    private readonly KarenVfxCanvasBatch _drawBatch = new();
    private sealed class Spark(Vector2 position, Vector2 velocity, float life, float radius)
    {
        public Vector2 Position = position, Velocity = velocity;
        public float Age;
        public readonly float Life = life, Radius = radius;
    }
    private readonly List<Spark> _sparks = new(14);
    public bool BelongsTo(CardModel? candidate) => ReferenceEquals(candidate, model);
    public override void _Ready() { SelfModulate = new Color(1,1,1,.65f); _phase = GetInstanceId()%997*.017f; _Process(0); }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(card) || !BelongsTo(card.Model)) { Hide(); QueueFree(); return; }
        bool playing = model.Pile?.Type == PileType.Play;
        Visible = KarenVfxQuality.AmbientEffects && card.IsVisibleInTree() && model.HasShine()
            && !ShineVfx.WillDeplete(model, shineAlreadyDecreased: playing)
            && !(GodotObject.IsInstanceValid(warning) && warning.Visible);
        if (!Visible) { _sparks.Clear(); _hasPose = false; _spawnClock = 0; return; }
        float dt = Mathf.Min((float)delta, .05f);
        _age += dt;
        _border.Update(this);
        var pose = GetGlobalTransformWithCanvas();
        float unit = Mathf.Max(.1f, pose.Scale.Length()/Mathf.Sqrt(2));
        var lightTarget = pose*(_border.TopCenter+new Vector2(28+Mathf.Sin(_age*1.4f+_phase)*12,-8+Mathf.Sin(_age*1.9f+_phase)*5));
        _lightWorld = _hasPose ? _lightWorld.Lerp(lightTarget,1-Mathf.Exp(-12*dt)) : lightTarget;
        _lightWorld = lightTarget+(_lightWorld-lightTarget).LimitLength(18*unit);
        var inherited = _hasPose ? ((pose.Origin-_previousPose.Origin)/Mathf.Max(dt,.001f)).LimitLength(90*unit)*.08f : Vector2.Zero;
        _previousPose = pose; _hasPose = true;
        for (int i=_sparks.Count-1; i>=0; i--)
        {
            var spark = _sparks[i]; spark.Age += dt;
            spark.Position += spark.Velocity*dt;
            if (spark.Age >= spark.Life) _sparks.RemoveAt(i);
        }
        _spawnClock += dt;
        if (_spawnClock >= KarenVfxQuality.SpawnInterval(.065f) && _sparks.Count < KarenVfxQuality.ParticleCount(14))
        {
            _spawnClock = 0;
            int i = _sparkIndex++; float side = i%2==0 ? -1 : 1;
            var origin = _border.At(i*.381966f);
            var velocity = pose.BasisXform(new Vector2(side*(5+i%4),-18-i%5*4))+inherited;
            _sparks.Add(new Spark(pose*origin,velocity,.75f+i%4*.07f,(2.3f+i%3*.5f)*unit));
        }
        KarenVfxQuality.Redraw(this);
    }

    public override void _Draw()
    {
        var canvas = GetGlobalTransformWithCanvas();
        _drawBatch.Clear(1f / Mathf.Max(.1f, canvas.X.Length()));
        float pulse = .5f + .5f * Mathf.Sin(_age * 1.7f + _phase);
        var gold = new Color("#ffd48b"); var hot = new Color("#fff1cc");
        if (_border.Outline.Length > 1)
        {
            _drawBatch.Polyline(_border.Outline, new Color(gold, .10f + pulse * .06f), 12);
            _drawBatch.Polyline(_border.Outline, new Color(gold, .36f + pulse * .12f), 3);
        }
        var lightPoint = canvas.AffineInverse() * _lightWorld;
        float radius = 7 + pulse * 2;
        for (int layer = 5; layer > 0; layer--)
            _drawBatch.Circle(lightPoint, radius * 2 * layer / 5f,
                new Color(gold, .14f * Mathf.Pow(1 - layer / 6f, 2)));
        _drawBatch.Star(lightPoint, radius, .22f, new Color(hot, .55f + pulse * .15f));
        // Old sparks keep their viewport-space motion while all geometry shares one submission.
        _drawBatch.SetTransform(canvas.AffineInverse());
        foreach (var spark in _sparks)
        {
            float t = spark.Age / spark.Life;
            float a = Mathf.Sin(Mathf.Min(1, t * 4) * Mathf.Pi / 2) * Mathf.Pow(1 - t, 1.2f) * .68f;
            _drawBatch.Circle(spark.Position, spark.Radius * 2.2f, new Color(gold, a * .15f));
            _drawBatch.Line(spark.Position - spark.Velocity * .05f, spark.Position,
                new Color(gold, a * .65f), spark.Radius * .7f);
            _drawBatch.Circle(spark.Position, spark.Radius, new Color(hot, a));
        }
        _drawBatch.Flush(this);
    }
}
