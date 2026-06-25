using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Analysis;
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
    bool SpuriousBeatRejection = true)
{
    public AnalysisWorker.Config ToWorkerConfig(ulong sessionId, ISampleWriter? sampleWriter)
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
            // ~1.0 removes the post-lock in-window onset hardening to catch a weak A.
            PhaseGuideOnsetRescueScale = WeakAOnsetRescue ? 1.0 : 0.0,
            // ~0.35 rejects weak between-beat noise during acquisition so it cannot alias the BPH to 2x.
            AcquisitionPeakGateFraction = SpuriousBeatRejection ? 0.35 : 0.0,
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
        };
    }
}
