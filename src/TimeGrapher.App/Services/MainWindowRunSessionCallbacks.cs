using System;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Services;

/// <summary>
/// The View-method delegates the <see cref="RunSessionController"/> needs: they read control bounds
/// (run settings), reset the renderers, and marshal analysis frames, so they are inherently
/// View-coupled and supplied by the View. Grouped so <see cref="MainWindowBootstrapper.Build"/>'s
/// signature stays small.
/// </summary>
internal sealed record MainWindowRunSessionCallbacks(
    Func<ulong, AnalysisWorker.Config> CreateAnalysisConfig,
    Action ResetBeforeRun,
    Action ClearPendingFrames,
    Action ResetRenderTiming,
    Action<AnalysisFrame> OnAnalysisFrameReady);
