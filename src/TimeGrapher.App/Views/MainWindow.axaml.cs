using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;

using TimeGrapher.App;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Services;
using TimeGrapher.App.Tabs;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Views;

public partial class MainWindow : Window
{
    private const int ERROR_RATE_Y_SCALE = 10;
    private const int ERROR_RATE_X_DATA_POINTS = 250;
    private const int DEFAULT_SOUND_IMAGE_WIDTH = 1019;
    private const int DEFAULT_SOUND_IMAGE_HEIGHT = 654;
    private const string APP_FONT_FAMILY = "D2Coding";
    private const string PLAYBACK_SOURCE = "Playback";
    private const string SIMULATION_SOURCE = "Simulation";

    private const string PREF_NAME_WELSHI = "Welshi USB";
    private const string PREF_NAME_CHINESE_GENERIC = "Chinese Generic USB";

    private static RunSessionStopOutcome CombineStopOutcome(RunSessionStopOutcome left, RunSessionStopOutcome right)
    {
        if (left == RunSessionStopOutcome.Stopping || right == RunSessionStopOutcome.Stopping)
        {
            return RunSessionStopOutcome.Stopping;
        }

        return RunSessionStopOutcome.Stopped;
    }

    // RenameAudioDevices[][2]: { match-substring, preferred-display-name }.
    private static readonly string[][] RenameAudioDevices =
    {
        new[] { "USB PnP Sound Device", PREF_NAME_WELSHI },
        new[] { "C-Media USB Headphone Set", PREF_NAME_CHINESE_GENERIC },
        new[] { "CM108 Audio Controller Mono", PREF_NAME_WELSHI },
        new[] { "Audio Adapter Mono", PREF_NAME_CHINESE_GENERIC },
    };

    private static readonly string[] PreferredAudioDevices =
    {
        PREF_NAME_WELSHI,
        PREF_NAME_CHINESE_GENERIC,
        "Cubilux HA-3",
        "CUBILUX CA7",
    };

    // --- Members (mirror MainWindow.h) ---
    private IRecordingWriter? mWavWriter;
    private readonly ITimeGrapherDialogService mDialogs;
    private readonly IUserErrorLog mErrorLog;
    private readonly RecordingSessionService mRecordingSessionService;
    private readonly PlaybackFileService mPlaybackFileService;
    private GraphFrameRenderer mGraphFrameRenderer = null!;
    private AnalysisFrameRouter mFrameRouter = null!;
    private AnalysisFrameRenderScheduler mFrameRenderScheduler = null!;
    private InfoTabRegistry mInfoTabRegistry = null!;
    private string mCurrentDir;
    private int mRateBeforePlaybackOrSim;
    private string mDeviceNameBeforePlaybackOrSim = "";
    private readonly AnalysisFramePresenter mFramePresenter;
    private AnalysisFrame? mLastAnalysisFrame;
    private bool mIsClosing;
    private readonly MainWindowViewModel mViewModel;
    private readonly MainWindowSelectionCoordinator mSelectionCoordinator;
    private readonly AudioSelectionState mAudioSelection = new();
    private readonly AudioDeviceController mAudioDeviceController;
    private readonly AcceptBandController mAcceptBandController;
    private readonly SamplingSettingsController mSamplingSettingsController;
    private readonly AppSettingsController mAppSettingsController;
    private readonly RunControlController mRunControlController;
    private readonly RunSelectionResolver mRunSelectionResolver;
    private readonly RunCommandService mRunCommandService;
    private readonly RunSessionController mRunSessionController;
    private readonly AnalysisPerformanceLogger? mAnalysisPerformanceLogger;
    private readonly MeasurementLogController mMeasurementLogController;

    public MainWindow()
    {
        InitializeComponent();
        ConfigurePlatformWindow();

        // The bootstrapper owns the application/service object graph; the View creates only the
        // view-model, the renderers (XAML controls) and the view adapters, then asks it to wire the
        // rest. The View ctor is no longer the composition root.
        mViewModel = MainWindowBootstrapper.CreateViewModel();

        // Renderers need the view-model and named XAML controls, so they are View-created.
        mInfoTabRegistry = InfoTabRegistry.FromCatalog(GraphicsTabWidget, PositionButtonStrip, APP_FONT_FAMILY, mViewModel);
        mGraphFrameRenderer = new GraphFrameRenderer(mInfoTabRegistry.Consumers, Results);
        mGraphFrameRenderer.ApplyTheme(CurrentPlotTheme());
        mFrameRouter = mInfoTabRegistry.CreateRouter();
        mFrameRenderScheduler = new AnalysisFrameRenderScheduler(
            action => Dispatcher.UIThread.Post(action),
            ActiveInfoTabRefreshIntervalMs,
            HandleAnalysisFrame);

        // View adapters: bound to this window / the renderer (the operations adapters are the
        // service->View edge addressed by a later unit) and the View-method run-session callbacks.
        var dialogs = new MainWindowDialogService(this);
        mDialogs = dialogs;
        mAudioDeviceController = new AudioDeviceController(
            mViewModel,
            mAudioSelection,
            new LiveAudioDeviceBackend(),
            new UiThreadDispatcher(),
            RenameDeviceName,
            SelectInputDeviceIndexAfterReload,
            PLAYBACK_SOURCE,
            SIMULATION_SOURCE,
            new AudioSelectionPreference(
                AppSettings.Current.LeftPanel.InputDeviceName,
                AppSettings.Current.LeftPanel.SampleRate));
        var adapters = new MainWindowViewAdapters(
            new MainWindowSelectionOperations(this, mAudioSelection, mAudioDeviceController),
            new RunCommandOperations(this),
            dialogs,
            new GraphAcceptBandOperations(mGraphFrameRenderer),
            new MainWindowSelectionOptions(PLAYBACK_SOURCE, SIMULATION_SOURCE));
        var runSessionCallbacks = new MainWindowRunSessionCallbacks(
            sessionId => BuildRunSettings().ToWorkerConfig(sessionId, mWavWriter),
            Reset,
            ClearPendingAnalysisFrames,
            () => mFrameRenderScheduler.ResetTiming(),
            OnAnalysisFrameReady);

        MainWindowComposition composition = MainWindowBootstrapper.Build(
            mViewModel, adapters, runSessionCallbacks, AppStartupOptions.Current);
        mSelectionCoordinator = composition.SelectionCoordinator;
        // The coordinator drives the device controller, and the controller suppresses the
        // coordinator's events while repopulating, so attach the gate once both exist.
        mAudioDeviceController.AttachSelectionEventGate(mSelectionCoordinator);
        mRunSelectionResolver = composition.RunSelectionResolver;
        mErrorLog = composition.ErrorLog;
        mRecordingSessionService = composition.RecordingSessionService;
        mPlaybackFileService = composition.PlaybackFileService;
        mRunCommandService = composition.RunCommandService;
        mMeasurementLogController = composition.MeasurementLogController;
        mRunSessionController = composition.RunSessionController;
        mRunControlController = composition.RunControlController;
        mAcceptBandController = composition.AcceptBandController;
        mSamplingSettingsController = composition.SamplingSettingsController;
        mFramePresenter = composition.AnalysisFramePresenter;
        mAnalysisPerformanceLogger = composition.AnalysisPerformanceLogger;

        // DataContext after Build, which seeds startup-derived view-model state (IsMeasurementLogEnabled).
        DataContext = mViewModel;

        // Default working directory: current dir, then ../../sample if it exists (MainWindow ctor).
        mCurrentDir = ResolveInitialPlaybackDirectory(Directory.GetCurrentDirectory());

        string appTitle = BuildAppTitle();
        Title = appTitle;
        AppTitleText.Text = appTitle;

        // Wire events (Qt auto-connected on_* slots + explicit connect()s). The accept-band and
        // run-control controllers self-subscribe to the view-model inside Build.
        mViewModel.PropertyChanged += mSelectionCoordinator.OnViewModelPropertyChanged;
        mViewModel.PropertyChanged += OnReviewCursorPropertyChanged;
        GraphicsTabWidget.SelectionChanged += OnGraphicsTabSelectionChanged;
        mViewModel.SetLongTermTabActive(ActiveInfoTabId() == InfoTabCatalog.LongTermPerfTabId);

        LoadBph();
        LoadSimBph();
        mAudioDeviceController.LoadAudioDevices();
        mGraphFrameRenderer.Initialize(BuildTabResetContext());
        mGraphFrameRenderer.SetResults(GraphFrameRenderer.PlaceholderResults);
        SetGuiStopMode();
        mAppSettingsController = new AppSettingsController(
            mViewModel,
            CaptureAppSettingsSelection,
            AppSettingsStore.Save);
        mViewModel.AttachSettingsWindowResetRunner(mAppSettingsController);

        Closed += OnWindowClosed;
    }

    internal static string ResolveInitialPlaybackDirectory(string currentDirectory)
    {
        string resolved = currentDirectory;
        try
        {
            string samples = Path.GetFullPath(Path.Combine(resolved, "..", "..", "sample"));
            if (Directory.Exists(samples)) resolved = samples;
        }
        catch { /* keep current dir */ }

        return resolved;
    }

    private void ConfigurePlatformWindow()
    {
        if (OperatingSystem.IsWindows())
        {
            SystemDecorations = SystemDecorations.None;
            CanResize = false;
            MaximizeWindowButton.IsVisible = true;
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            SystemDecorations = SystemDecorations.None;
            CanResize = false;
            MaximizeWindowButton.IsVisible = false;
            WindowState = WindowState.Normal;
            return;
        }

        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        MaximizeWindowButton.IsVisible = false;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeWindowButtonClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnThemeToggleButtonClick(object? sender, RoutedEventArgs e)
    {
        Avalonia.Application? application = Avalonia.Application.Current;
        if (application == null)
        {
            return;
        }

        ThemeVariant nextTheme = application.RequestedThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        application.RequestedThemeVariant = nextTheme;
        PlotThemePalette nextPalette = PlotThemeFor(nextTheme);
        mGraphFrameRenderer.ApplyTheme(nextPalette);
        mRunSessionController.SetSoundBackgroundColor(nextPalette.ScopeBg);
        mRunSessionController.SetSpectrogramColormap(nextPalette.IsLight);
    }

    private static PlotThemePalette CurrentPlotTheme()
    {
        ThemeVariant requestedTheme = Avalonia.Application.Current?.RequestedThemeVariant ?? ThemeVariant.Light;
        return PlotThemeFor(requestedTheme);
    }

    private static PlotThemePalette PlotThemeFor(ThemeVariant theme)
    {
        return PlotThemePalette.FromResources(theme);
    }

    private void OnHelpTitleBarButtonClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://lgcmu2026-team5.github.io/TimeGrapher-Net/manual/",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // No registered URL handler (common on a bare Linux/Raspberry Pi
            // desktop) would otherwise throw out of the click handler — surface it
            // instead of letting it crash the UI.
            Console.Error.WriteLine("Opening the manual URL failed: " + ex.Message);
            ReportUserErrorStatus(UserErrorMessages.ManualOpenFailed, ex.ToString());
        }
    }

    private void OnSettingsTitleBarButtonClick(object? sender, RoutedEventArgs e)
    {
        // Static run options moved out of the tab strip into this popup; it shares
        // the MainWindow view-model so toggles reach the same run-settings flow.
        var settingsWindow = new SettingsWindow { DataContext = mViewModel };
        _ = settingsWindow.ShowDialog(this);
    }

    private void OnMaximizeWindowButtonClick(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseWindowButtonClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    // AnalysisFrameReady fires on the analysis thread; marshal to UI thread.
    private void OnAnalysisFrameReady(AnalysisFrame frame)
    {
        mFrameRenderScheduler.Enqueue(frame);
    }

    private void ClearPendingAnalysisFrames()
    {
        mFrameRenderScheduler.Reset();
        mLastAnalysisFrame = null;
    }

    private void HandleAnalysisFrame(AnalysisFrame frame, ulong droppedFrames)
    {
        if (frame.SessionId != mRunSessionController.AnalysisSessionId)
        {
            return;
        }

        mLastAnalysisFrame = frame;
        mGraphFrameRenderer.UpdateResults(frame);
        mFrameRouter.Route(frame, ActiveInfoTabId(), BuildTabRenderContext(frame));

        // Display leg of the latency evidence: stamped after the frame rendered.
        long displayTicks = System.Diagnostics.Stopwatch.GetTimestamp();

        // The view-model-facing updates (awaiting-beat-sync, review maximum, latency readout, run
        // status + its log/console side effects) are owned by the frame presenter. Run them before
        // the displayed-frame observers so the view-model still reflects this frame even if an
        // observer were to fail.
        mFramePresenter.Present(frame, droppedFrames, displayTicks, FrameSampleRate(frame));

        mAnalysisPerformanceLogger?.ObserveDisplayed(frame, displayTicks);
        mMeasurementLogController.ObserveDisplayed(frame);
    }

    private void Reset()
    {
        mGraphFrameRenderer.Reset(BuildTabResetContext());
        mInfoTabRegistry.ResetViews.ResetAll();
        mFramePresenter.Reset();
    }

    private void ReportUserErrorStatus(string message, string detail)
    {
        mViewModel.SetWarningStatus(message);
        mErrorLog.Write(message, detail);
    }

    private Task ShowUserErrorAsync(string message, string detail)
    {
        ReportUserErrorStatus(message, detail);
        return mDialogs.ShowErrorAsync(UserErrorMessages.DialogTitle, message);
    }

    // --- Event handlers (Qt on_* slots) ---

    // Thin View-side rendering reaction to the view-model's review cursor. Rendering is a View
    // concern, so the view-model owns the cursor state (clamping, slider, readout) and the View
    // only re-renders the kept last frame when the cursor moves while review is active — it does
    // not call back into the View from the view-model. The gate reads the view-model's own
    // "review active" flag (IsReviewBarEnabled) rather than re-deriving it from RunState. The
    // input workers are gated and the analysis drained during pause, so no live frame would
    // otherwise carry the moved cursor to the active tab.
    private void OnReviewCursorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.ReviewCursorTimeS) ||
            !mViewModel.IsReviewBarEnabled)
        {
            return;
        }

        AnalysisFrame? frame = mLastAnalysisFrame;
        if (frame == null || frame.SessionId != mRunSessionController.AnalysisSessionId)
        {
            return;
        }

        // Cursor clear can happen while RunState is still Paused: stop/reset
        // clears pause scrubbing, and leaving Long-Term clears its in-tab scrub
        // state. Fan the cursor-less render out once so consumers that rendered
        // a scrubbed context clear before later tab switches reuse the kept frame.
        if (mViewModel.ReviewCursorTimeS == null)
        {
            mFrameRouter.RenderToAll(frame, BuildTabRenderContext(frame));
            return;
        }

        mFrameRouter.Route(frame, ActiveInfoTabId(), BuildTabRenderContext(frame));
    }

    private void OnGraphicsTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // ResetTiming (not Reset) so a pending coalesced frame — and its merged
        // transient signals — survives the tab switch and renders right after.
        mFrameRenderScheduler.ResetTiming();

        string activeTab = ActiveInfoTabId();
        mViewModel.SetLongTermTabActive(activeTab == InfoTabCatalog.LongTermPerfTabId);

        AnalysisFrame? frame = mLastAnalysisFrame;
        if (frame != null && frame.SessionId == mRunSessionController.AnalysisSessionId)
        {
            mGraphFrameRenderer.UpdateResults(frame);
            mFrameRouter.Route(frame, activeTab, BuildTabRenderContext(frame));
        }
    }

    // --- Helpers ---

    internal static double ParseDouble(string? text)
    {
        // QString::toDouble returns 0.0 on failure. NumberStyles.Float matches
        // its C-locale grammar; Any would also take group separators ("0,5" ->
        // 5.0) and parenthesized negation ("(500)" -> -500).
        if (string.IsNullOrEmpty(text)) return 0.0;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;
    }

    private AnalysisRunSettings BuildRunSettings()
    {
        AnalysisSelection selection = mRunSelectionResolver.GetAnalysisSelection();
        return new AnalysisRunSettings(
            SampleRate: mAudioSelection.CurrentSampleRate,
            LiftAngle: (double)mViewModel.LiftAngle,
            AveragingPeriod: selection.AveragingPeriod,
            UseCOnset: mViewModel.UseCOnset,
            AutoBph: selection.AutoBph,
            ManualBph: selection.ManualBph,
            HpfCutoffHz: ParseDouble(mViewModel.HighPassCutoffText),
            SoundImageWidth: EffectivePixelWidth(SoundImageControl(), DEFAULT_SOUND_IMAGE_WIDTH),
            SoundImageHeight: EffectivePixelHeight(SoundImageControl(), DEFAULT_SOUND_IMAGE_HEIGHT),
            ScopeSnapshotPointBudget: InfoTabCatalog.ScopeTargetPointBudget,
            WeakAOnsetRescue: mViewModel.WeakAOnsetRescue,
            SpuriousBeatRejection: mViewModel.SpuriousBeatRejection,
            // Normalize at the boundary so an out-of-range/off-step view-model value can
            // never size the detector input block (the controller already snaps on edit).
            AnalysisBlockSize: SamplingSettings.NormalizeAnalysisBlockSize(mViewModel.AnalysisBlockSize));
    }

    private AppSettingsSelection CaptureAppSettingsSelection()
    {
        return new AppSettingsSelection(
            EmptyToNull(CurrentInputDeviceText()),
            mAudioSelection.CurrentSampleRate,
            SelectedCatalogValue(BphCatalog.ManualAutoBph, mViewModel.SelectedBphIndex, AppSettings.Current.LeftPanel.Bph),
            SelectedCatalogValue(BphCatalog.ManualBph, mViewModel.SelectedSimBphIndex, AppSettings.Current.LeftPanel.SimulationBph));
    }

    private static int SelectedCatalogValue(IReadOnlyList<int> values, int index, int fallback)
    {
        return index >= 0 && index < values.Count ? values[index] : fallback;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;

    private Control SoundImageControl()
    {
        return mInfoTabRegistry.SoundImageControl is Control control ? control : GraphicsTabWidget;
    }

    private AnalysisTabResetContext BuildTabResetContext()
    {
        return new AnalysisTabResetContext(
            SampleRate: mAudioSelection.CurrentSampleRate,
            RateErrorYScale: ERROR_RATE_Y_SCALE,
            RateDataPoints: ERROR_RATE_X_DATA_POINTS,
            ActivePosition: (WatchPosition)mViewModel.SelectedPositionIndex);
    }

    private AnalysisTabRenderContext BuildTabRenderContext(AnalysisFrame frame)
    {
        return new AnalysisTabRenderContext(
            SampleRate: FrameSampleRate(frame),
            ReviewCursorTimeS: mViewModel.ReviewCursorTimeS);
    }

    /// <summary>
    /// Rate the frame's analysis run was configured with. Falls back to the current
    /// UI rate for frames that predate the SampleRate field. Using the frame's own
    /// rate keeps the final playback/sim frames correct after the device rate has
    /// already been restored.
    /// </summary>
    private int FrameSampleRate(AnalysisFrame frame)
    {
        return frame.SampleRate > 0 ? frame.SampleRate : mAudioSelection.CurrentSampleRate;
    }

    // "TimeGrapher v{Major}.{Minor}.{Build}" from the assembly version (set in Directory.Build.props).
    private static string BuildAppTitle()
    {
        System.Version? v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "TimeGrapher" : $"TimeGrapher v{v.Major}.{v.Minor}.{v.Build}";
    }

    private string ActiveInfoTabId()
    {
        if (GraphicsTabWidget.SelectedItem is TabItem { Tag: string tabId } &&
            InfoTabCatalog.TryGet(tabId, out _))
        {
            return tabId;
        }

        return InfoTabCatalog.RateScopeTabId;
    }

    private int ActiveInfoTabRefreshIntervalMs()
    {
        return InfoTabCatalog.Get(ActiveInfoTabId()).RefreshIntervalMs;
    }

    private static int EffectivePixelWidth(Control control, int fallback)
    {
        double value = control.Bounds.Width > 0 ? control.Bounds.Width : control.Width;
        return Math.Max(1, (int)Math.Round(double.IsNaN(value) || value <= 0 ? fallback : value));
    }

    private static int EffectivePixelHeight(Control control, int fallback)
    {
        double value = control.Bounds.Height > 0 ? control.Bounds.Height : control.Height;
        return Math.Max(1, (int)Math.Round(double.IsNaN(value) || value <= 0 ? fallback : value));
    }
}
