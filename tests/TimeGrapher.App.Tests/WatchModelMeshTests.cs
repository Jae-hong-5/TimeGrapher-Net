using System.IO;
using System.Numerics;
using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Covers the GLB reader and the mesh it builds against the bundled watch model,
/// whose structure is fixed by <c>watch_model_round/validation.json</c> (1818
/// vertices, 3540 triangles / 10620 indices), plus the flat-colour/bounds
/// derivation on a synthetic primitive.
/// </summary>
public sealed class WatchModelMeshTests
{
    private const int VertexCount = 1818;
    private const int TriangleCount = 3540;
    private const int IndexCount = TriangleCount * 3;

    [Fact]
    public void ReadsBundledModelTopology()
    {
        GlbPrimitive primitive = GlbMeshLoader.Load(File.OpenRead(LocateModel()));

        Assert.Equal(VertexCount, primitive.Positions.Length);
        Assert.Equal(VertexCount, primitive.Colors.Length);
        Assert.Equal(IndexCount, primitive.Indices.Length);
        Assert.All(primitive.Indices, index => Assert.InRange(index, 0, VertexCount - 1));
    }

    [Fact]
    public void BuildsOneFlatColourPerTriangleAndAFiniteBoundingRadius()
    {
        WatchModelMesh mesh = WatchModelMesh.FromPrimitive(GlbMeshLoader.Load(File.OpenRead(LocateModel())));

        Assert.Equal(TriangleCount, mesh.TriangleCount);
        Assert.Equal(TriangleCount, mesh.TriangleColors.Length);
        // The model spans ≈4.6 cm; the rotation pivot is the case centre, so the
        // bounding radius is a little over the case half-width (crown included).
        Assert.InRange(mesh.BoundingRadius, 0.02f, 0.04f);
    }

    [Fact]
    public void FlatTriangleColourIsTheMeanOfItsVertexColours()
    {
        var primitive = new GlbPrimitive(
            new[] { Vector3.Zero, new Vector3(1, 0, 0), new Vector3(0, 2, 0) },
            new[]
            {
                new Vector4(0.2f, 0.4f, 0.6f, 1f),
                new Vector4(0.8f, 0.4f, 0.0f, 1f),
                new Vector4(0.2f, 0.4f, 0.6f, 1f),
            },
            new[] { 0, 1, 2 });

        WatchModelMesh mesh = WatchModelMesh.FromPrimitive(primitive);

        Assert.Equal(1, mesh.TriangleCount);
        Assert.Equal(0.4f, mesh.TriangleColors[0].X, 5); // (0.2 + 0.8 + 0.2) / 3
        Assert.Equal(0.4f, mesh.TriangleColors[0].Y, 5);
        Assert.Equal(0.4f, mesh.TriangleColors[0].Z, 5); // (0.6 + 0.0 + 0.6) / 3
        Assert.Equal(2f, mesh.BoundingRadius, 5);         // |(0,2,0)| is the farthest vertex
    }

    /// <summary>
    /// Walks up from the test binary to the committed app asset (the same GLB the
    /// app bundles via <c>avares://</c>), so the test depends only on tracked files.
    /// </summary>
    private static string LocateModel()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(
                dir.FullName, "src", "TimeGrapher.App", "Assets", "Model", "watch_model_round_vertexcolor.glb");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate the bundled watch model asset from the test directory.");
    }
}
