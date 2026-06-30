using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Analysis.Quality;
using TimeGrapher.Core.AudioIo;

namespace TimeGrapher.App;

internal sealed record AnalysisRunSettings(
    int SampleRate,
    double LiftAngle,
    int AveragingPeriod,
    bool UseCOnset,
    bool AutoBph,
    int ManualBph,
    double HpfCutoffHz,
    int SoundImageWidth,
    int SoundImageHeight,
    int ScopeSnapshotPointBudget,
    int AnalysisBlockSize,
    bool WeakAOnsetRescue = true,
    // Match the app-wide default (AppSettings / SettingsWindowSettings) so a caller that
    // omits this argument gets the same WeakAOnsetRescueStrengthStep the production run
    // path supplies, rather than a divergent StandardStep.
    int WeakAOnsetRescueStrengthStep = WeakAOnsetRescueStrengthPolicy.MinStep,
    bool SpuriousBeatRejection = true)
{
    // Calibrated run-parameter policy values. Tests deliberately keep the bare
    // literals (1.25/1.0/0.75 / 0.35) as tripwires: changing a constant here
    // trips a test and forces a review of the policy value.
    private const double SpuriousBeatRejectionGateFraction = 0.35;

    public AnalysisWorker.Config ToWorkerConfig(
        ulong sessionId, ISampleWriter? sampleWriter, ISignalQualityClassifier? qualityClassifier = null)
    {
        return new AnalysisWorker.Config
        {
            SampleRate = SampleRate,
            LiftAngle = LiftAngle,
            AveragingPeriod = AveragingPeriod,
            UseCOnset = UseCOnset,
            SessionId = sessionId,
            AutoBph = AutoBph,
            ManualBph = ManualBph,
            HpfCutoffHz = HpfCutoffHz,
            PhaseGuideOnsetRescueScale = WeakAOnsetRescue
                ? WeakAOnsetRescueStrengthPolicy.ScaleFromStep(WeakAOnsetRescueStrengthStep)
                : 0.0,
            // ~0.35 rejects weak between-beat noise during acquisition so it cannot alias the BPH to 2x.
            AcquisitionPeakGateFraction = SpuriousBeatRejection ? SpuriousBeatRejectionGateFraction : 0.0,
            SoundImageWidth = SoundImageWidth,
            SoundImageHeight = SoundImageHeight,
            ScopeSnapshotPointBudget = ScopeSnapshotPointBudget,
            AnalysisBlockSize = AnalysisBlockSize,
            // Sound print background follows the scope background (single source: App.axaml ScopeBgColor).
            SoundImageBackgroundColor = PlotThemePalette.Current.ScopeBg,
            // Spectrogram colormap is one shared viridis LUT for both themes; only
            // the empty (no-input) background follows the theme.
            SpectrogramLightColormap = PlotThemePalette.Current.IsLight,
            SampleWriter = sampleWriter,
            // Advisory signal-quality classifier (window-level). Null leaves the
            // window path off; the composition root decides which Strategy to inject
            // (heuristic fallback, or the ONNX model once available). The per-beat
            // geometry rules in BeatSegmentCapture are unaffected either way.
            QualityClassifier = qualityClassifier,
        };
    }
}
