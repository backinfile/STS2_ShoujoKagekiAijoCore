using System;
using Godot;

namespace ShoujoKageki.Core.V1.Presentation;

/// <summary>Reusable, ordered triangle geometry for small animated canvas decorations.</summary>
public sealed class CanvasGeometryBatch(int initialPointCapacity = 4096)
{
    private static readonly Vector2[] CirclePoints = CreateCircle();
    private Vector2[] _points = Array.Empty<Vector2>();
    private Color[] _colors = Array.Empty<Color>();
    private int[] _indices = Array.Empty<int>();
    private Vector2[] _uvs = Array.Empty<Vector2>();
    private readonly int _minimumPoints = Math.Max(128, initialPointCapacity);
    private readonly int _minimumIndices = Math.Max(192, initialPointCapacity * 3 / 2);
    private readonly int _indexStep = Math.Max(96, initialPointCapacity * 3 / 4);
    private int _pointCount, _indexCount;
    private Transform2D _transform = Transform2D.Identity;
    private float _pixelSize = 1f;

    public static void PrepareResources() => _ = CirclePoints;
    public static Resource?[] PreparedResources => Array.Empty<Resource?>();

    private static Vector2[] CreateCircle()
    {
        var points = new Vector2[64];
        for (int i = 0; i < points.Length; i++)
            points[i] = Vector2.FromAngle(i * Mathf.Tau / points.Length);
        return points;
    }

    public void Clear(float pixelSize = 1f)
    {
        _pointCount = _indexCount = 0;
        SetTransform(Transform2D.Identity, pixelSize);
    }

    public void SetTransform(Transform2D transform, float pixelSize = 1f)
    {
        _transform = transform;
        _pixelSize = Mathf.Max(.001f, pixelSize);
    }

    private void Reserve(int points, int indices)
    {
        if (_pointCount + points > _points.Length)
        {
            int capacity = Math.Max(_minimumPoints, Math.Max(_pointCount + points, _points.Length * 2));
            Array.Resize(ref _points, capacity);
            Array.Resize(ref _colors, capacity);
        }
        if (_indexCount + indices > _indices.Length)
        {
            // Small steps keep the usual card buffers below the large-object threshold.
            int capacity = Math.Max(_minimumIndices, (_indexCount + indices + _indexStep - 1) / _indexStep * _indexStep);
            Array.Resize(ref _indices, capacity);
        }
    }

    private void Point(Vector2 point, Color color)
    {
        _points[_pointCount] = _transform * point;
        _colors[_pointCount++] = color;
    }

    private void Triangle(int a, int b, int c)
    {
        _indices[_indexCount++] = a;
        _indices[_indexCount++] = b;
        _indices[_indexCount++] = c;
    }

    public void Strip(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color start, Color end)
    {
        Reserve(4, 6);
        int first = _pointCount;
        Point(a, start); Point(b, start); Point(c, end); Point(d, end);
        Triangle(first, first + 1, first + 2);
        Triangle(first, first + 2, first + 3);
    }

    public void Circle(Vector2 center, float radius, Color color)
    {
        if (radius <= 0 || color.A <= 0) return;
        int segments = radius / _pixelSize < 8 ? 16 : radius / _pixelSize < 24 ? 32 : 64;
        segments = Math.Max(8, segments / CoreEffects.ParticleStride);
        int stride = CirclePoints.Length / segments;
        Reserve(segments + 1, segments * 3);
        int first = _pointCount;
        Point(center, color);
        for (int i = 0; i < segments; i++) Point(center + CirclePoints[i * stride] * radius, color);
        for (int i = 0; i < segments; i++) Triangle(first, first + 1 + i, first + 1 + (i + 1) % segments);
    }

    public void Star(Vector2 center, float radius, float innerRadius, Color color, float rotation = 0)
    {
        if (radius <= 0 || color.A <= 0) return;
        Reserve(9, 24);
        int first = _pointCount;
        Point(center, color);
        float cosine = Mathf.Cos(rotation), sine = Mathf.Sin(rotation);
        for (int i = 0; i < 8; i++)
        {
            var direction = CirclePoints[(48 + i * 8) % 64];
            direction = new Vector2(direction.X * cosine - direction.Y * sine,
                direction.X * sine + direction.Y * cosine);
            Point(center + direction * (i % 2 == 0 ? radius : radius * innerRadius), color);
        }
        for (int i = 0; i < 8; i++) Triangle(first, first + 1 + i, first + 1 + (i + 1) % 8);
    }

    private void Section(Vector2 point, Vector2 normal, Color color, float halfWidth)
    {
        var transparent = new Color(color, 0);
        Point(point + normal * (halfWidth + _pixelSize), transparent);
        Point(point + normal * halfWidth, color);
        Point(point - normal * halfWidth, color);
        Point(point - normal * (halfWidth + _pixelSize), transparent);
    }

    private void Join(int first, int next)
    {
        for (int band = 0; band < 3; band++)
        {
            Triangle(first + band, next + band, next + band + 1);
            Triangle(first + band, next + band + 1, first + band + 1);
        }
    }

    public void Line(Vector2 from, Vector2 to, Color color, float width)
    {
        if (color.A <= 0 || from.DistanceSquaredTo(to) < .000001f) return;
        Reserve(8, 18);
        int first = _pointCount;
        var normal = (to - from).Normalized().Orthogonal();
        Section(from, normal, color, width * .5f);
        Section(to, normal, color, width * .5f);
        Join(first, first + 4);
    }

    public void Polyline(Vector2[] points, Color color, float width, int count = -1)
    {
        if (count < 0) count = points.Length;
        if (count < 2 || color.A <= 0) return;
        Reserve(count * 4, (count - 1) * 18);
        bool closed = points[0].DistanceSquaredTo(points[count - 1]) < .000001f;
        int first = _pointCount;
        for (int i = 0; i < count; i++)
        {
            int previous = i == 0 ? closed ? count - 2 : 0 : i - 1;
            int next = i == count - 1 ? closed ? 1 : i : i + 1;
            var before = points[i] - points[previous];
            var after = points[next] - points[i];
            var normal = before.LengthSquared() < .000001f ? after.Normalized().Orthogonal()
                : after.LengthSquared() < .000001f ? before.Normalized().Orthogonal()
                : Miter(before.Normalized().Orthogonal(), after.Normalized().Orthogonal());
            Section(points[i], normal, color, width * .5f);
            if (i > 0) Join(first + (i - 1) * 4, first + i * 4);
        }
    }

    private static Vector2 Miter(Vector2 before, Vector2 after)
    {
        var normal = (before + after).Normalized();
        return normal * Mathf.Min(2, 1f / Mathf.Max(.25f, normal.Dot(after)));
    }

    public void Quad(Vector2 center, Vector2 size, float rotation, Color color)
    {
        if (color.A <= 0) return;
        Reserve(4, 6);
        if (_uvs.Length < _points.Length) Array.Resize(ref _uvs, _points.Length);
        int first = _pointCount;
        float sine = Mathf.Sin(rotation), cosine = Mathf.Cos(rotation);
        var x = new Vector2(cosine, sine) * size.X * .5f;
        var y = new Vector2(-sine, cosine) * size.Y * .5f;
        Point(center - x - y, color); _uvs[first] = Vector2.Zero;
        Point(center + x - y, color); _uvs[first + 1] = Vector2.Right;
        Point(center + x + y, color); _uvs[first + 2] = Vector2.One;
        Point(center - x + y, color); _uvs[first + 3] = Vector2.Down;
        Triangle(first, first + 1, first + 2);
        Triangle(first, first + 2, first + 3);
    }

    public void Flush(CanvasItem surface, Texture2D? texture = null)
    {
        if (_indexCount == 0) return;
        // The server copies the buffers at submission. Only populated vertices and triangles are submitted;
        // arrays remain owned by this effect and can be reused on its next frame.
        RenderingServer.CanvasItemAddTriangleArray(surface.GetCanvasItem(),
            _indices.AsSpan(0, _indexCount), _points.AsSpan(0, _pointCount), _colors.AsSpan(0, _pointCount),
            texture == null ? ReadOnlySpan<Vector2>.Empty : _uvs.AsSpan(0, _pointCount),
            ReadOnlySpan<int>.Empty, ReadOnlySpan<float>.Empty, texture?.GetRid() ?? default, _indexCount / 3);
    }
}
