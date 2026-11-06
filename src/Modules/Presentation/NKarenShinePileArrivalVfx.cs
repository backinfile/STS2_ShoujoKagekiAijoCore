using ShoujoKageki.Core.Runtime.Modules.Presentation;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using System.Collections.Generic;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

/// <summary>A short catch on the visual wrapper; native hover still owns the icon itself.</summary>
public partial class NKarenShinePileArrivalVfx(TextureRect icon, Control visual) : Node2D
{
    private readonly List<float> _impacts = new(3);
    private readonly Vector2 _baseScale = visual.Scale, _basePivot = visual.PivotOffset;
    private readonly float _baseRotation = visual.Rotation;
    private Vector2 _startScale = Vector2.One;
    private float _time, _age, _duration, _startRotation;
    private int _arrivals;
    private static readonly Color Gold = new("#ffcf79"), Hot = new("#fff4d5");

    public override void _Ready()
    {
        TopLevel = true; ZIndex = 111;
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        Restart();
    }

    public void Restart()
    {
        _startScale = visual.Scale / _baseScale;
        _startRotation = visual.Rotation - _baseRotation;
        _duration = SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant ? .20f : .46f;
        _age = 0; _arrivals++;
        if (_impacts.Count == 3) _impacts.RemoveAt(0);
        _impacts.Add(_time);
        UpdatePose();
        KarenVfxQuality.Redraw(this);
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(icon) || !icon.IsInsideTree()
            || !GodotObject.IsInstanceValid(visual)) { QueueFree(); return; }
        if (!KarenVfxQuality.Enabled) { Restore(); QueueFree(); return; }
        _time += (float)delta; _age += (float)delta;
        if (_age >= _duration) { Restore(); QueueFree(); return; }
        UpdatePose(); KarenVfxQuality.Redraw(this);
    }

    private void UpdatePose()
    {
        GlobalTransform = GetCanvasTransform().AffineInverse();
        // Rotate and scale about the icon centre, keeping the flight destination fixed.
        visual.PivotOffset = icon.Position + icon.PivotOffset;
        float t = Mathf.Clamp(_age / _duration, 0, 1);
        Vector2 stretch = t < .12f ? _startScale.Lerp(new Vector2(1.13f,.88f), Ease(t / .12f))
            : t < .34f ? new Vector2(1.13f,.88f).Lerp(new Vector2(1.23f,1.21f), Ease((t-.12f)/.22f))
            : new Vector2(1.23f,1.21f).Lerp(Vector2.One, 1-Mathf.Pow(1-(t-.34f)/.66f,3));
        visual.Scale = _baseScale * stretch;
        float turn = t < .18f ? Mathf.Lerp(_startRotation,-.28f,Ease(t/.18f))
            : t < .46f ? Mathf.Lerp(-.28f,.21f,Ease((t-.18f)/.28f))
            : t < .72f ? Mathf.Lerp(.21f,-.085f,Ease((t-.46f)/.26f))
            : Mathf.Lerp(-.085f,0,Ease((t-.72f)/.28f));
        visual.Rotation = _baseRotation + turn;
    }

    public override void _Draw()
    {
        if (!GodotObject.IsInstanceValid(icon)) return;
        var pose = icon.GetGlobalTransformWithCanvas();
        var centre = pose * (icon.Size * .5f);
        float radius = Mathf.Min(icon.Size.X,icon.Size.Y) * pose.Scale.Length() / Mathf.Sqrt(2) * .5f;
        float flash = Mathf.Pow(1-Mathf.Clamp(_age/(_duration*.48f),0,1),2);
        for (int layer=5; layer>0; layer--)
            DrawCircle(centre,radius*(.45f+layer*.16f),new Color(Gold,flash*.11f*(1-layer/6f)));
        if (icon.Texture != null && flash > .001f)
        {
            Vector2 size = icon.Texture.GetSize();
            size *= Mathf.Min(icon.Size.X/size.X,icon.Size.Y/size.Y);
            DrawSetTransformMatrix(pose);
            DrawTextureRect(icon.Texture,new Rect2((icon.Size-size)*.5f,size),false,new Color(Hot,flash*.70f));
            DrawSetTransform(Vector2.Zero);
        }
        foreach (float impact in _impacts)
        {
            float t = Mathf.Clamp((_time-impact)/_duration,0,1), light = Mathf.Pow(1-t,2);
            DrawArc(centre,radius*(.48f+1.03f*Ease(t)),0,Mathf.Tau,40,new Color(Gold,light*.70f),1.8f,true);
            for (int i=0;i<10;i += KarenVfxQuality.ParticleStride)
            {
                float angle = i*Mathf.Tau/10+.18f;
                var direction = new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                var p = centre+direction*radius*(.60f+Mathf.Sin(t*Mathf.Pi*.5f)*(1.1f+i%3*.08f));
                DrawLine(p-direction*radius*.12f,p,new Color(Gold,light*.75f),1.6f,true);
                DrawCircle(p,1.3f+i%3*.35f,new Color(Hot,light));
            }
        }
    }

    private static float Ease(float t) => t*t*(3-2*t);
    private void Restore()
    {
        if (!GodotObject.IsInstanceValid(visual)) return;
        visual.Scale = _baseScale; visual.Rotation = _baseRotation; visual.PivotOffset = _basePivot;
    }
    public override void _ExitTree() => Restore();
}
