using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.AudioIo;
using TimeGrapher.Core.Detection.Scoring;

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
    bool PllEventVeto,
    int AnalysisBlockSize,
    bool WeakAOnsetRescue = false)
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
            // The PLL event veto stays opt-in because it trades a small recall
            // cost for precision on impulse-contaminated streams.
            EventGate = PllEventVeto ? new PllMatchGate() : null,
            // ~1.0 removes the post-lock in-window onset hardening to catch a weak A.
            PhaseGuideOnsetRescueScale = WeakAOnsetRescue ? 1.0 : 0.0,
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
