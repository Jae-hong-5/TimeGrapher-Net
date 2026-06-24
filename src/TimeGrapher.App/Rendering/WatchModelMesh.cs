using System.Numerics;
using Avalonia.Platform;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// The watch model prepared for software rendering: the shared vertex array plus
/// a flat per-triangle colour (the model is authored with uniform per-part
/// vertex colours, so the three corner colours of any triangle are equal and
/// their mean is exact). Face normals are not stored — they are derived from the
/// rotated world-space corners each frame, which is correct because the model is
/// transformed only by a rigid rotation about the origin.
/// </summary>
internal sealed class WatchModelMesh
{
    /// <summary>Avalonia resource URI of the bundled vertex-colour watch model.</summary>
    public const string AssetUri = "avares://TimeGrapher.App/Assets/Model/watch_model_round_vertexcolor.glb";

    private static WatchModelMesh? _shared;

    private WatchModelMesh(Vector3[] positions, int[] indices, Vector4[] triangleColors, float boundingRadius)
    {
        Positions = positions;
        Indices = indices;
        TriangleColors = triangleColors;
        BoundingRadius = boundingRadius;
    }

    /// <summary>Model-space vertex positions (metres), shared by the index list.</summary>
    public Vector3[] Positions { get; }

    /// <summary>Flat triangle list: three entries per triangle, indexing <see cref="Positions"/>.</summary>
    public int[] Indices { get; }

    /// <summary>One RGBA colour per triangle (<c>Indices.Length / 3</c> entries).</summary>
    public Vector4[] TriangleColors { get; }

    /// <summary>Largest vertex distance from the origin; the rotation pivot, so it is orientation-invariant and frames every position identically.</summary>
    public float BoundingRadius { get; }

    public int TriangleCount => Indices.Length / 3;

    /// <summary>The bundled watch model, parsed once on first use (UI thread).</summary>
    public static WatchModelMesh Shared => _shared ??= LoadShared();

    private static WatchModelMesh LoadShared()
    {
        using Stream stream = AssetLoader.Open(new Uri(AssetUri));
        return FromPrimitive(GlbMeshLoader.Load(stream));
    }

    public static WatchModelMesh FromPrimitive(GlbPrimitive primitive)
    {
        Vector3[] positions = primitive.Positions;
        int[] indices = primitive.Indices;
        Vector4[] vertexColors = primitive.Colors;

        int triangleCount = indices.Length / 3;
        var triangleColors = new Vector4[triangleCount];
        for (int t = 0; t < triangleCount; t++)
        {
            Vector4 sum = vertexColors[indices[t * 3]]
                + vertexColors[indices[t * 3 + 1]]
                + vertexColors[indices[t * 3 + 2]];
            triangleColors[t] = sum / 3f;
        }

        float boundingRadius = 0f;
        foreach (Vector3 position in positions)
        {
            boundingRadius = Math.Max(boundingRadius, position.Length());
        }

        return new WatchModelMesh(positions, indices, triangleColors, boundingRadius);
    }
}
