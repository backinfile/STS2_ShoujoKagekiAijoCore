using ShoujoKageki.Core.Runtime.Modules.Presentation;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

/// <summary>Quiet, screen-owned decoration behind the native pile grid. No gameplay or card transforms.</summary>
public partial class NKarenPileViewVfx : Control
{
    private bool _promise;
    private float _age, _duration;
    private static readonly Color Gold = new("#e9c78d"), Red = new("#ce777d"), Ash = new("#a697aa");

    public static void Attach(NCardPileScreen screen, bool promise)
    {
        var decoration = new NKarenPileViewVfx
        {
            Name = "KarenPileViewVfx", _promise = promise,
            MouseFilter = MouseFilterEnum.Ignore, FocusMode = FocusModeEnum.None,
        };
        screen.AddChild(decoration);
        // Above the native dimmer, below the grid, tooltips and return button.
        screen.MoveChild(decoration, screen.GetNode("Background").GetIndex() + 1);
        decoration.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Ready()
    {
        _duration = SaveManager.Instance.PrefsSave.FastMode switch
        {
            FastModeType.Instant => 0,
            FastModeType.Fast => .32f,
            _ => .64f,
        };
        Resized += QueueRedraw;
        SetProcess(_duration > 0);
        KarenVfxQuality.Redraw(this);
    }

    public override void _Process(double delta)
    {
        _age = Mathf.Min(_age + (float)delta, _duration);
        KarenVfxQuality.Redraw(this);
        // Idle is a cached drawing, with no continuing particles, timers or per-frame work.
        if (_age >= _duration) SetProcess(false);
    }

    public override void _Draw()
    {
        if (Size.X <= 0 || Size.Y <= 0) return;
        float scale = Mathf.Min(Size.X / 1920, Size.Y / 1080);
        DrawSetTransform(new Vector2(Size.X * .5f, 0), 0, Vector2.One * scale);
        float t = _duration == 0 ? 1 : Mathf.Clamp(_age / _duration, 0, 1);
        float reveal = 1 - Mathf.Pow(1 - t, 3);
        float arrival = Mathf.Sin(t * Mathf.Pi);
        float light = Mathf.Lerp(.20f, .54f, reveal);
        // Native grid rows can begin at y=150; reserve the band below the top bar.
        var centre = new Vector2(0, 112);

        if (_promise)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                // The thread expands into the empty band above the card grid.
                var previous = centre + new Vector2(side * 28, 0);
                for (int i = 1; i <= 48; i++)
                {
                    float u = i / 48f;
                    var point = centre + new Vector2(side * (28 + u * 350 * reveal),
                        Mathf.Sin(u * Mathf.Pi) * 11 - u * 8);
                    var tint = Gold.Lerp(Red, u);
                    DrawLine(previous, point, new Color(tint, light * (1 - u * .80f)), 1.5f, true);
                    previous = point;
                }
                Star(centre + new Vector2(side * 12, 0), 9, Gold, light + arrival * .16f, false);
                // Very faint side lights stay outside the scrolling cards.
                var origin = new Vector2(side * 844, 174);
                for (int layer = 0; layer < 3; layer++)
                {
                    float width = 16 + layer * 13;
                    DrawColoredPolygon(new[] { origin, origin + new Vector2(-width, 355),
                        origin + new Vector2(width, 355) }, new Color(Red, (.007f + arrival * .009f) * reveal));
                }
            }
        }
        else
        {
            Star(centre, 14, Gold, light, true);
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 3; i++)
                {
                    float x = side * (34 + i * 30);
                    var a = centre + new Vector2(x, 5 + i * 4);
                    DrawLine(a, a + new Vector2(side * 19 * reveal, 3 + i),
                        new Color(Gold.Lerp(Ash, i / 3f), light * (.55f - i * .12f)), 1.3f, true);
                }
        }

        // One six-point opening cue; its direction distinguishes promise from spent shine.
        if (t > 0 && t < 1)
            for (int i = 0; i < 6; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float lane = i / 2;
                Vector2 point = _promise
                    ? centre + new Vector2(side * (60 + 300 * reveal + lane * 20), -5 + lane * 13)
                    : new Vector2(side * (800 + lane * 20), 205 + lane * 70 + reveal * 110);
                float alpha = Mathf.Sin(t * Mathf.Pi) * .46f;
                Star(point, 2.5f + i % 3, _promise ? Gold : Gold.Lerp(Ash, .35f), alpha, false);
            }
    }

    private void Star(Vector2 centre, float radius, Color tint, float alpha, bool hollow)
    {
        var points = new Vector2[11];
        for (int i = 0; i < 10; i++)
        {
            float angle = -Mathf.Pi * .5f + i * Mathf.Pi / 5;
            points[i] = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * (i % 2 == 0 ? 1 : .44f);
        }
        points[10] = points[0];
        if (!hollow) DrawColoredPolygon(points, new Color(tint, alpha));
        DrawPolyline(points, new Color(tint, alpha), 1.2f, true);
    }
}
