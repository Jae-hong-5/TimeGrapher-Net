namespace TimeGrapher.Core.Detection.Scoring;

/// <summary>
/// Single-source feature contract for envelope windows, shared between the
/// future C# inference path and the (offline, Python-side) training
/// pipeline: bucket-max decimation to a fixed point count followed by peak
/// normalization. Keeping the extractor here - next to the gate interface
/// it feeds - is what makes a later ONNX gate's features reproducible
/// bit-for-bit from the training recipe.
/// </summary>
public static class BeatWindowFeatures
{
    /// <summary>Fixed feature-vector length.</summary>
    public const int Points = 128;

    /// <summary>
    /// Fills <paramref name="features"/> (length >= <see cref="Points"/>)
    /// with the bucket-max decimated, peak-normalized window. Returns false
    /// for an empty or effectively silent window (no positive peak), in
    /// which case the feature content is unspecified.
    /// </summary>
    public static bool Extract(ReadOnlySpan<float> window, Span<float> features)
    {
        if (features.Length < Points)
        {
            throw new ArgumentException($"features must hold {Points} points", nameof(features));
        }
        if (window.IsEmpty)
        {
            return false;
        }

        features = features.Slice(0, Points);
        features.Clear();

        for (int i = 0; i < window.Length; i++)
        {
            int bucket = (int)((long)i * Points / window.Length);
            float v = window[i];
            if (v > features[bucket])
            {
                features[bucket] = v;
            }
        }

        float max = 0f;
        for (int i = 0; i < Points; i++)
        {
            if (features[i] > max)
            {
                max = features[i];
            }
        }
        if (max <= 0f)
        {
            return false;
        }

        float inv = 1f / max;
        for (int i = 0; i < Points; i++)
        {
            features[i] *= inv;
        }
        return true;
    }
}
