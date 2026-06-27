using System.Numerics;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// A small software renderer for the watch model: it rotates the mesh by the
/// requested orientation, projects it through a fixed perspective camera, and
/// fills the triangles into a BGRA pixel buffer with a depth buffer for correct
/// occlusion and flat two-sided Lambert shading. It depends only on
/// <see cref="System.Numerics"/> and the raw byte buffer, so it carries no
/// Avalonia/GPU dependency and runs identically on every target (including the
/// Raspberry Pi) and in headless contexts.
///
/// The model is tiny (≈3.5k triangles) and the viewport is small, so a CPU
/// rasterizer is well within budget; an instance reuses its scratch buffers
/// across frames to keep the per-frame animation allocation-free.
/// </summary>
internal sealed class WatchModelRasterizer
{
    // Camera and lighting are fixed (no user view control by design). The camera
    // looks at the rotation pivot from slightly above the dial-up axis so that
    // edge-on vertical orientations still show depth instead of a razor line.
    private const float FieldOfViewDegrees = 26f;
    private const float CameraElevationDegrees = 16f;
    private const float CameraAzimuthDegrees = 0f;
    private const float FitMargin = 1.0f; // fraction of the limiting half-view the model sphere fills
    internal const float ModelScale = 1.0f;
    private const float Ambient = 0.34f;

    private static readonly Vector3 LightDirection =
        Vector3.Normalize(new Vector3(-0.35f, 0.6f, 0.85f));

    private float[] _depth = Array.Empty<float>();
    private Vector3[] _screen = Array.Empty<Vector3>(); // x, y (pixels); z = view-space depth
    private bool[] _valid = Array.Empty<bool>();
    private int _vertexCapacity;

    /// <summary>
    /// Renders <paramref name="mesh"/> at <paramref name="orientation"/> into
    /// <paramref name="bgra"/> (premultiplied BGRA, <c>width*height*4</c> bytes).
    /// Uncovered pixels are left fully transparent so the panel shows through.
    /// </summary>
    public void Render(WatchModelMesh mesh, Quaternion orientation, byte[] bgra, int width, int height)
    {
        EnsureBuffers(width, height, mesh.Positions.Length);
        Array.Clear(bgra, 0, width * height * 4);
        Array.Fill(_depth, float.NegativeInfinity, 0, width * height);

        Matrix4x4 rotation = Matrix4x4.CreateFromQuaternion(orientation);

        float elevation = CameraElevationDegrees * (MathF.PI / 180f);
        float azimuth = CameraAzimuthDegrees * (MathF.PI / 180f);
        float aspect = width / (float)height;
        float fovY = FieldOfViewDegrees * (MathF.PI / 180f);
        float distance = CameraDistance(mesh.BoundingRadius * ModelScale, fovY, aspect);
        var eye = new Vector3(
            distance * MathF.Sin(azimuth) * MathF.Cos(elevation),
            distance * MathF.Sin(elevation),
            distance * MathF.Cos(azimuth) * MathF.Cos(elevation));

        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(fovY, aspect, 0.001f, distance * 4f);
        Matrix4x4 viewProjection = Matrix4x4.Multiply(view, projection);

        Vector3[] positions = mesh.Positions;
        for (int i = 0; i < positions.Length; i++)
        {
            var world = new Vector4(Vector3.Transform(positions[i] * ModelScale, rotation), 1f);
            Vector4 clip = Vector4.Transform(world, viewProjection);
            if (clip.W <= 1e-6f)
            {
                _valid[i] = false;
                continue;
            }

            float invW = 1f / clip.W;
            float ndcX = clip.X * invW;
            float ndcY = clip.Y * invW;
            float viewZ = Vector4.Transform(world, view).Z; // right-handed: nearer is larger (less negative)
            _screen[i] = new Vector3(
                (ndcX * 0.5f + 0.5f) * width,
                (1f - (ndcY * 0.5f + 0.5f)) * height,
                viewZ);
            _valid[i] = true;
        }

        int[] indices = mesh.Indices;
        Vector4[] triangleColors = mesh.TriangleColors;
        for (int t = 0; t < triangleColors.Length; t++)
        {
            int i0 = indices[t * 3];
            int i1 = indices[t * 3 + 1];
            int i2 = indices[t * 3 + 2];
            if (!_valid[i0] || !_valid[i1] || !_valid[i2])
            {
                continue;
            }

            Vector3 worldCentroid = Vector3.Transform(
                (positions[i0] + positions[i1] + positions[i2]) * (ModelScale / 3f), rotation);
            Vector3 n = Vector3.Cross(
                Vector3.Transform(positions[i1] * ModelScale, rotation) -
                Vector3.Transform(positions[i0] * ModelScale, rotation),
                Vector3.Transform(positions[i2] * ModelScale, rotation) -
                Vector3.Transform(positions[i0] * ModelScale, rotation));
            float nLength = n.Length();
            if (nLength <= 1e-12f)
            {
                continue;
            }

            n /= nLength;
            Vector3 toEye = Vector3.Normalize(eye - worldCentroid);
            if (Vector3.Dot(n, toEye) < 0f)
            {
                n = -n; // two-sided shading: light whichever face points at the camera
            }

            float lambert = MathF.Max(0f, Vector3.Dot(n, LightDirection));
            float intensity = Ambient + (1f - Ambient) * lambert;
            Vector4 color = triangleColors[t];
            byte b = ToByte(color.Z * intensity);
            byte g = ToByte(color.Y * intensity);
            byte r = ToByte(color.X * intensity);

            FillTriangle(_screen[i0], _screen[i1], _screen[i2], r, g, b, bgra, width, height);
        }
    }

    private void FillTriangle(Vector3 s0, Vector3 s1, Vector3 s2, byte r, byte g, byte b, byte[] bgra, int width, int height)
    {
        float area = Edge(s0, s1, s2.X, s2.Y);
        if (MathF.Abs(area) < 1e-6f)
        {
            return;
        }

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(s0.X, MathF.Min(s1.X, s2.X))));
        int maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(s0.X, MathF.Max(s1.X, s2.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(s0.Y, MathF.Min(s1.Y, s2.Y))));
        int maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(s0.Y, MathF.Max(s1.Y, s2.Y))));
        float invArea = 1f / area;

        for (int py = minY; py <= maxY; py++)
        {
            float fy = py + 0.5f;
            for (int px = minX; px <= maxX; px++)
            {
                float fx = px + 0.5f;
                float w0 = Edge(s1, s2, fx, fy);
                float w1 = Edge(s2, s0, fx, fy);
                float w2 = Edge(s0, s1, fx, fy);
                bool inside = (w0 >= 0f && w1 >= 0f && w2 >= 0f) || (w0 <= 0f && w1 <= 0f && w2 <= 0f);
                if (!inside)
                {
                    continue;
                }

                float depth = (w0 * s0.Z + w1 * s1.Z + w2 * s2.Z) * invArea;
                int pixel = py * width + px;
                if (depth <= _depth[pixel])
                {
                    continue;
                }

                _depth[pixel] = depth;
                int o = pixel * 4;
                bgra[o] = b;
                bgra[o + 1] = g;
                bgra[o + 2] = r;
                bgra[o + 3] = 255;
            }
        }
    }

    private static float Edge(Vector3 a, Vector3 b, float px, float py) =>
        (b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X);

    /// <summary>Distance that frames the origin-centred bounding sphere within <see cref="FitMargin"/> of the limiting half-view angle.</summary>
    private static float CameraDistance(float radius, float fovY, float aspect)
    {
        float halfY = fovY * 0.5f;
        float halfX = MathF.Atan(MathF.Tan(halfY) * aspect);
        float limit = MathF.Min(halfX, halfY) * FitMargin;
        return radius / MathF.Sin(limit);
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private void EnsureBuffers(int width, int height, int vertexCount)
    {
        int pixels = width * height;
        if (_depth.Length < pixels)
        {
            _depth = new float[pixels];
        }

        if (_vertexCapacity < vertexCount)
        {
            _screen = new Vector3[vertexCount];
            _valid = new bool[vertexCount];
            _vertexCapacity = vertexCount;
        }
    }
}
