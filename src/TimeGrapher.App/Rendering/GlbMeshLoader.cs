using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Raw triangle data read from a single glTF primitive: one vertex array with
/// optional per-vertex colours, plus a flat triangle index list (3 indices per
/// triangle). This is the loader's only output; lighting/normals and any derived
/// render state are the consumer's concern (<see cref="WatchModelMesh"/>).
/// </summary>
internal readonly record struct GlbPrimitive(
    Vector3[] Positions,
    Vector4[] Colors,
    int[] Indices);

/// <summary>
/// Minimal binary-glTF (<c>.glb</c>, glTF 2.0) reader: enough to pull a single
/// triangle mesh — <c>POSITION</c>, <c>COLOR_0</c>, and the index list — out of
/// the watch model. It is deliberately not a general glTF importer; it supports
/// only the accessor layouts the bundled model uses and fails fast (throws) on
/// anything else, matching the project's "a miss is a programming error surfaced
/// fast" convention rather than guessing a fallback.
/// </summary>
internal static class GlbMeshLoader
{
    private const uint GlbMagic = 0x46546C67; // "glTF"
    private const uint ChunkJson = 0x4E4F534A; // "JSON"
    private const uint ChunkBin = 0x004E4942;  // "BIN\0"

    public static GlbPrimitive Load(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        byte[] data = memory.ToArray();
        return Load(data);
    }

    public static GlbPrimitive Load(byte[] data)
    {
        var span = new ReadOnlySpan<byte>(data);
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(span);
        if (magic != GlbMagic)
        {
            throw new InvalidDataException("Not a binary glTF (.glb) file: bad magic.");
        }

        uint totalLength = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, 4));
        string? json = null;
        byte[]? bin = null;
        int offset = 12;
        while (offset < totalLength)
        {
            uint chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
            uint chunkType = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 4, 4));
            ReadOnlySpan<byte> body = span.Slice(offset + 8, (int)chunkLength);
            if (chunkType == ChunkJson)
            {
                json = Encoding.UTF8.GetString(body);
            }
            else if (chunkType == ChunkBin)
            {
                bin = body.ToArray();
            }

            offset += 8 + (int)chunkLength;
        }

        if (json is null || bin is null)
        {
            throw new InvalidDataException("Binary glTF is missing its JSON or BIN chunk.");
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement accessors = root.GetProperty("accessors");
        JsonElement bufferViews = root.GetProperty("bufferViews");

        JsonElement primitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
        JsonElement attributes = primitive.GetProperty("attributes");

        Vector3[] positions = ReadVector3(accessors, bufferViews, bin, attributes.GetProperty("POSITION").GetInt32());
        Vector4[] colors = ReadColors(accessors, bufferViews, bin, attributes.GetProperty("COLOR_0").GetInt32());
        int[] indices = ReadIndices(accessors, bufferViews, bin, primitive.GetProperty("indices").GetInt32());

        return new GlbPrimitive(positions, colors, indices);
    }

    private readonly record struct AccessorView(
        int ComponentType,
        int Count,
        int BaseOffset,
        int Stride,
        int ComponentCount,
        bool Normalized);

    private static AccessorView Resolve(JsonElement accessors, JsonElement bufferViews, int accessorIndex)
    {
        JsonElement accessor = accessors[accessorIndex];
        int componentType = accessor.GetProperty("componentType").GetInt32();
        int count = accessor.GetProperty("count").GetInt32();
        int componentCount = ComponentCount(accessor.GetProperty("type").GetString());
        bool normalized = accessor.TryGetProperty("normalized", out JsonElement n) && n.GetBoolean();
        int accessorOffset = accessor.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0;

        JsonElement bufferView = bufferViews[accessor.GetProperty("bufferView").GetInt32()];
        int viewOffset = bufferView.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0;
        int componentSize = ComponentSize(componentType);
        int elementSize = componentSize * componentCount;
        int stride = bufferView.TryGetProperty("byteStride", out JsonElement bs) ? bs.GetInt32() : elementSize;

        return new AccessorView(componentType, count, viewOffset + accessorOffset, stride, componentCount, normalized);
    }

    private static Vector3[] ReadVector3(JsonElement accessors, JsonElement bufferViews, byte[] bin, int accessorIndex)
    {
        AccessorView view = Resolve(accessors, bufferViews, accessorIndex);
        if (view.ComponentType != 5126 || view.ComponentCount != 3)
        {
            throw new NotSupportedException("POSITION accessor must be float VEC3.");
        }

        var span = new ReadOnlySpan<byte>(bin);
        var result = new Vector3[view.Count];
        for (int i = 0; i < view.Count; i++)
        {
            int o = view.BaseOffset + i * view.Stride;
            result[i] = new Vector3(ReadFloat(span, o), ReadFloat(span, o + 4), ReadFloat(span, o + 8));
        }

        return result;
    }

    private static Vector4[] ReadColors(JsonElement accessors, JsonElement bufferViews, byte[] bin, int accessorIndex)
    {
        AccessorView view = Resolve(accessors, bufferViews, accessorIndex);
        var span = new ReadOnlySpan<byte>(bin);
        var result = new Vector4[view.Count];
        for (int i = 0; i < view.Count; i++)
        {
            int o = view.BaseOffset + i * view.Stride;
            float r = ReadComponentAsUnit(span, o, view.ComponentType, view.Normalized, 0);
            float g = ReadComponentAsUnit(span, o, view.ComponentType, view.Normalized, 1);
            float b = ReadComponentAsUnit(span, o, view.ComponentType, view.Normalized, 2);
            float a = view.ComponentCount >= 4
                ? ReadComponentAsUnit(span, o, view.ComponentType, view.Normalized, 3)
                : 1f;
            result[i] = new Vector4(r, g, b, a);
        }

        return result;
    }

    private static int[] ReadIndices(JsonElement accessors, JsonElement bufferViews, byte[] bin, int accessorIndex)
    {
        AccessorView view = Resolve(accessors, bufferViews, accessorIndex);
        var span = new ReadOnlySpan<byte>(bin);
        var result = new int[view.Count];
        for (int i = 0; i < view.Count; i++)
        {
            int o = view.BaseOffset + i * view.Stride;
            result[i] = view.ComponentType switch
            {
                5121 => span[o],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(o, 2)),
                5125 => (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(o, 4)),
                _ => throw new NotSupportedException($"Unsupported index component type {view.ComponentType}."),
            };
        }

        return result;
    }

    private static float ReadComponentAsUnit(ReadOnlySpan<byte> span, int elementOffset, int componentType, bool normalized, int component)
    {
        int size = ComponentSize(componentType);
        int o = elementOffset + component * size;
        return componentType switch
        {
            5126 => ReadFloat(span, o),
            5121 => normalized ? span[o] / 255f : span[o],
            5123 => normalized
                ? BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(o, 2)) / 65535f
                : BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(o, 2)),
            _ => throw new NotSupportedException($"Unsupported colour component type {componentType}."),
        };
    }

    private static float ReadFloat(ReadOnlySpan<byte> span, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(span.Slice(offset, 4));

    private static int ComponentSize(int componentType) => componentType switch
    {
        5120 or 5121 => 1,
        5122 or 5123 => 2,
        5125 or 5126 => 4,
        _ => throw new NotSupportedException($"Unsupported component type {componentType}."),
    };

    private static int ComponentCount(string? accessorType) => accessorType switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        _ => throw new NotSupportedException($"Unsupported accessor type '{accessorType}'."),
    };
}
