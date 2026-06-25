using TimeGrapher.Core.Shared;

namespace TimeGrapher.Core.Analysis.Quality;

/// <summary>
/// The seam (an injection point, like a wall socket) between the analysis
/// pipeline and a signal-quality classifier. Core defines this contract and a
/// deterministic heuristic fallback; the trained ML.NET implementation lives in
/// a separate project and is plugged in by the App. Because the pipeline only
/// talks to this interface, the ML runtime never leaks into Core (Core depends
/// on nothing) and the pipeline stays unit-testable without loading a model.
/// </summary>
public interface ISignalQualityClassifier
{
    /// <summary>Classify a single feature window into a quality assessment.</summary>
    SignalQualityAssessment Classify(in SignalQualityFeatures features);
}
