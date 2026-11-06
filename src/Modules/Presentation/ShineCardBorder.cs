using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using System;
using System.Collections.Generic;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

/// <summary>Both Shine rims follow the actual frame texture, including its rounded corners.</summary>
internal sealed class ShineCardBorder(NCard card)
{
    private static readonly Dictionary<Texture2D, Vector2[]> Contours = new();
    private TextureRect? _frame;
    private Vector2[] _outline = Array.Empty<Vector2>();
    private float[] _distance = Array.Empty<float>();
    private readonly Vector2[] _segment = new Vector2[12];
    private Vector2[]? _previousContour;
    private Transform2D _previousTransform;
    private Vector2 _previousSize, _previousOffset;
    public Vector2[] Outline => _outline;
    public Vector2 TopCenter { get; private set; }
    private float Length => _distance.Length == 0 ? 0 : _distance[^1];

    internal static void Prepare(NCard card)
    {
        if (card.GetNodeOrNull<TextureRect>("%Frame")?.Texture is { } texture)
            GetContour(texture);
    }

    internal static void Prepare(Texture2D texture) => GetContour(texture);

    private static Vector2[] GetContour(Texture2D texture)
    {
        if (!Contours.TryGetValue(texture, out var contour))
            Contours[texture] = contour = ReadContour(texture);
        return contour;
    }

    public void Update(Node2D overlay)
    {
        _frame ??= card.GetNode<TextureRect>("%Frame");
        if (_frame.Texture is not {} texture) return;
        var contour = GetContour(texture);
        if (_outline.Length != contour.Length)
        {
            _outline = new Vector2[contour.Length];
            _distance = new float[contour.Length];
        }
        // Native Frame uses KeepAspectCentered. Account for its texture aspect ratio
        // and its complete transform relative to the OverlayContainer.
        Vector2 textureSize = texture.GetSize();
        float fit = Mathf.Min(_frame.Size.X / textureSize.X, _frame.Size.Y / textureSize.Y);
        Vector2 size = textureSize * fit, offset = (_frame.Size - size) * .5f;
        var transform = overlay.GetGlobalTransformWithCanvas().AffineInverse() * _frame.GetGlobalTransformWithCanvas();
        if (ReferenceEquals(contour, _previousContour) && transform.IsEqualApprox(_previousTransform)
            && size.IsEqualApprox(_previousSize) && offset.IsEqualApprox(_previousOffset)) return;
        _previousContour = contour;
        _previousTransform = transform;
        _previousSize = size;
        _previousOffset = offset;
        TopCenter = transform * (offset + new Vector2(size.X * .5f, 0));
        for (int i = 0; i < contour.Length; i++)
        {
            _outline[i] = transform * (offset + contour[i] * size);
            _distance[i] = i == 0 ? 0 : _distance[i - 1] + _outline[i - 1].DistanceTo(_outline[i]);
        }
    }

    private static Vector2[] ReadContour(Texture2D texture)
    {
        using var image = ReadImage(texture);
        if (image == null || image.IsEmpty()) return Array.Empty<Vector2>();
        int width = image.GetWidth(), height = image.GetHeight();
        // Imported card atlases decompress to RGBA8. Read their pixels once:
        // GetPixel crosses the managed/native boundary for every transparent pixel.
        // Keep the native path for other formats so their alpha semantics are unchanged.
        byte[]? rgba = image.GetFormat() == Image.Format.Rgba8 ? image.GetData() : null;
        bool Transparent(int x, int y) => rgba != null
            ? rgba[(y * width + x) * 4 + 3] < 128
            : image.GetPixel(x, y).A < .5f;
        var left = new List<Vector2>(); var right = new List<Vector2>();
        for (int y = 0; y < height; y += 2)
        {
            int a = 0, b = width - 1;
            while (a < width && Transparent(a, y)) a++;
            while (b > a && Transparent(b, y)) b--;
            if (a == width) continue;
            AddEdge(left, new Vector2(a / (float)width, (y + .5f) / height));
            AddEdge(right, new Vector2((b + 1f) / width, (y + .5f) / height));
        }
        right.Reverse(); left.AddRange(right);
        if (left.Count > 0) left.Add(left[0]);
        return left.ToArray();
    }

    private static Image? ReadImage(Texture2D texture)
    {
        // AtlasTexture.GetImage crops before decompression and fails on the game's
        // compressed atlases. Decompress the source atlas before extracting its region.
        if (texture is AtlasTexture atlas)
        {
            using var source = atlas.Atlas.GetImage();
            if (source == null || source.IsEmpty()) return null;
            if (source.IsCompressed() && source.Decompress() != Error.Ok) return null;
            return source.GetRegion(new Rect2I((Vector2I)atlas.Region.Position, (Vector2I)atlas.Region.Size));
        }
        var image = texture.GetImage();
        if (image != null && image.IsCompressed() && image.Decompress() != Error.Ok) { image.Dispose(); return null; }
        return image;
    }

    private static void AddEdge(List<Vector2> edge, Vector2 point)
    {
        // Collapse straight runs; retain the alpha contour's curved corner samples.
        if (edge.Count >= 2 && Mathf.Abs((edge[^1] - edge[^2]).Cross(point - edge[^1])) < .0000001f)
            edge[^1] = point;
        else edge.Add(point);
    }

    public Vector2 At(float fraction)
    {
        if (_outline.Length < 2 || Length <= 0) return Vector2.Zero;
        float distance = Mathf.PosMod(fraction, 1) * Length;
        int i = Array.BinarySearch(_distance, distance);
        if (i >= 0) return _outline[i];
        i = Math.Clamp(~i, 1, _outline.Length - 1);
        return _outline[i - 1].Lerp(_outline[i], (distance - _distance[i - 1]) / Mathf.Max(.001f, _distance[i] - _distance[i - 1]));
    }

    public Vector2[] Segment(float fraction, float length)
    {
        for (int i = 0; i < _segment.Length; i++)
            _segment[i] = At(fraction + i * length / (Mathf.Max(1, Length) * (_segment.Length - 1)));
        return _segment;
    }
}
