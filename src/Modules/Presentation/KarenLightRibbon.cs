using System;
using Godot;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

/// <summary>A short, tapered strip on an existing draw layer. Head may pass 1 to retract its tail.</summary>
internal static class KarenLightRibbon
{
    // Shared visual resources are prepared during the run loading screen as well as on demand.
    // Each draw call copies these arrays synchronously. Reuse buffers on the calling
    // thread; callers on other threads do not share mutable geometry.
    private sealed class DrawBuffers
    {
        internal readonly Vector2[] Samples = new Vector2[49], Normals = new Vector2[49], Outer = new Vector2[8], Core = new Vector2[8];
        internal readonly KarenVfxCanvasBatch Batch = new(1536);
    }
    [ThreadStatic] private static DrawBuffers? _drawBuffers;
    private static DrawBuffers Buffers => _drawBuffers ??= new DrawBuffers();
    internal static void PrepareResources()
    {
        _ = Buffers;
        _glow ??= new GradientTexture2D {
        Width=96,Height=96,Fill=GradientTexture2D.FillEnum.Radial,FillFrom=new(.5f,.5f),FillTo=new(1,.5f),
        Gradient=new Gradient { Offsets=[0,.18f,.55f,1],Colors=[Colors.White,new(1,1,1,.65f),new(1,1,1,.12f),Colors.Transparent] } };
    }
    internal static Godot.Resource?[] PreparedResources => [_glow];

    private static GradientTexture2D? _glow;
    private static GradientTexture2D Glow { get { PrepareResources(); return _glow!; } }

    public static Vector2 Curve(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        float v = 1 - t;
        return a * (v * v * v) + b * (3 * v * v * t) + c * (3 * v * t * t) + d * (t * t * t);
    }

    public static void Draw(Node2D layer, Func<float, Vector2> path, float head, float length, float width, Color color, float alpha)
    {
        float first = Mathf.Clamp(head - length, 0, 1), last = Mathf.Clamp(head, 0, 1);
        if (alpha <= 0 || last - first < .001f) return;
        int segments = 48 / KarenVfxQuality.ParticleStride;
        var buffers = Buffers;
        var points = buffers.Samples; var normals = buffers.Normals; var batch = buffers.Batch;
        batch.Clear();
        for (int i = 0; i <= segments; i++)
        {
            float t = Mathf.Lerp(first, last, i / (float)segments);
            points[i] = path(t);
            normals[i] = (path(Mathf.Min(1, t + .001f)) - path(Mathf.Max(0, t - .001f))).Orthogonal().Normalized();
        }
        for (int i = 0; i < segments; i++)
        {
            float u = i / (float)segments, v = (i + 1f) / segments;
            var a = points[i]; var b = points[i + 1];
            var na = normals[i]; var nb = normals[i + 1];
            float wa = width * Mathf.Sin(u * Mathf.Pi), wb = width * Mathf.Sin(v * Mathf.Pi);
            var halo = new Color(color, alpha * .055f);
            // Ordered triangles retain halo/core/line layering at every segment.
            batch.Strip(a-na*wa*2.3f, a+na*wa*2.3f, b+nb*wb*2.3f, b-nb*wb*2.3f, halo, halo);
            batch.Strip(a-na*wa, a+na*wa, b+nb*wb, b-nb*wb,
                new Color(color, alpha * (.25f + .42f * u)), new Color(color, alpha * (.25f + .42f * v)));
            batch.Line(a, b, new Color(color.Lerp(Colors.White, .70f), alpha * (.12f + v * .42f)), Mathf.Max(.7f, width * .13f));
        }
        batch.Flush(layer);
    }

    /// <summary>Four sharp silk-light points with a white core, without an opaque sprite box.</summary>
    public static void Star(Node2D layer,Vector2 point,float radius,Color color,float alpha,float angle=0)
    {
        if(alpha<=0 || radius<=0)return;
        layer.DrawTextureRect(Glow,new Rect2(point-Vector2.One*radius*2,Vector2.One*radius*4),false,new Color(color,alpha*.42f));
        var buffers=Buffers;var outer=buffers.Outer;var core=buffers.Core;
        for(int i=0;i<8;i++){
            var direction=Vector2.FromAngle(-Mathf.Pi*.5f+i*Mathf.Pi*.25f+angle);
            float r=i%2==0?radius:radius*.15f;
            outer[i]=point+direction*r;core[i]=point+direction*r*.58f;
        }
        layer.DrawColoredPolygon(outer,new Color(color,alpha));
        layer.DrawColoredPolygon(core,new Color(Colors.White,alpha));
    }
}
