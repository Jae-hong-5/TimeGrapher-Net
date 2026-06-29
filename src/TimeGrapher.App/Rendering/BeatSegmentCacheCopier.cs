using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Shared deep-copy of a <see cref="BeatSegmentsSnapshot"/> into UI-owned storage,
/// used by the renderers that cache the latest snapshot for later interaction
/// (BeatNoiseScopeRenderer, WaveformCompareRenderer). The segment envelope/raw
/// arrays reference a <see cref="TimeGrapher.Core.Analysis.BeatSegmentCapture"/>
/// pool buffer that analysis recycles, so a cached re-render (interaction handlers,
/// paused review, ghost overlay) must not read them after the pool rotates.
/// Scalar fields, markers, and the average snapshot are immutable and shared as-is.
/// This is safety-critical: the copy semantics must stay byte-identical to what the
/// renderers fed before this helper existed.
/// </summary>
internal static class BeatSegmentCacheCopier
{
    public static BeatSegmentsSnapshot CopyForCache(BeatSegmentsSnapshot snapshot)
    {
        IReadOnlyList<BeatSegment> segments = snapshot.Segments;
        if (segments.Count == 0)
        {
            return snapshot;
        }

        var owned = new BeatSegment[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            BeatSegment s = segments[i];
            owned[i] = new BeatSegment
            {
                Samples = s.Samples.ToArray(),
                RawValid = s.RawValid,
                RawMin = s.RawMin.ToArray(),
                RawMax = s.RawMax.ToArray(),
                MsPerPoint = s.MsPerPoint,
                StartTimeS = s.StartTimeS,
                IsTic = s.IsTic,
                AOffsetMs = s.AOffsetMs,
                PeakValue = s.PeakValue,
                CPeakValid = s.CPeakValid,
                CPeakOffsetMs = s.CPeakOffsetMs,
                COnsetValid = s.COnsetValid,
                COnsetOffsetMs = s.COnsetOffsetMs,
                Quality = s.Quality,
            };
        }

        return new BeatSegmentsSnapshot
        {
            Version = snapshot.Version,
            Segments = owned,
            Markers = snapshot.Markers,
            LiftAngleDeg = snapshot.LiftAngleDeg,
            Average = snapshot.Average,
            Quality = snapshot.Quality,
        };
    }
}
