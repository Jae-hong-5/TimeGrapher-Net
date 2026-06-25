using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.ViewModels;

internal enum RunUiState
{
    Stopped,
    Starting,
    Running,
    Paused,
    Stopping,
    StopFailed,
}

internal sealed class MainWindowViewModel : INotifyPropertyChanged
{
    /// <summary>Review-bar step button increment (stream seconds).</summary>
    public const double ReviewStepS = 1.0;

    private readonly AsyncRelayCommand _playPauseCommand;
    private readonly RelayCommand _stopCommand;
    private readonly RelayCommand _resetCommand;
    private readonly RelayCommand _reviewStepBackCommand;
    private readonly RelayCommand _reviewStepForwardCommand;
    private readonly RelayCommand _reviewLiveCommand;
    private IRunCommandRunner? _runner;
    private RunUiState _runState = RunUiState.Stopped;
    private bool _modeAllowsSampleRate = true;
    private bool _modeAllowsGain = true;
    // Simulation is not the default source (the app opens on Live), so the
    // simulation knobs start disabled until the Simulation source is selected.
    private bool _modeAllowsSimulationParams;
    private string _statusText = "";
    private string _latencyText = "";
    private bool _isAwaitingBeatSync;
    private double _gain = 100.0;
    // Sampling parameters read at run start; seeded from SamplingSettings on startup. The
    // literal defaults mirror SamplingSettings.Default (kept here rather than referencing
    // Core, like the band fields above), so an un-seeded view-model still builds sane runs.
    // decimal (not int) to match Avalonia NumericUpDown.Value (decimal?), like the band
    // inputs; the controller snaps these to an in-range step multiple on each edit.
    private decimal _analysisBlockSize = 4096m;
    private decimal _captureBufferMs = 20m;
    private int _selectedInputDeviceIndex = -1;
    private int _selectedSampleRateIndex = -1;
    private decimal _averagingPeriod = 20m;
    private int _selectedBphIndex;
    private decimal _liftAngle = 52m;
    private int _selectedSimBphIndex = -1;
    private decimal _simErrorRate;
    private decimal _simAmplitude = 300m;
    private decimal _simBeatError;
    private bool _realistic = true;
    private string _highPassCutoffText = "200";
    private bool _useCOnset;
    private bool _weakAOnsetRescue;
    private bool _pauseOnPositionChange;
    private int _sweepMultiple = SweepFrameProjector.DefaultSweepMultiple;
    private int _selectedPositionIndex; // 0 = WatchPosition.CH (dial up)
    private bool _sigmaAveraging;
    private bool _isMeasurementLogEnabled;
    // Accept-band ("normal" range) limits the user edits in Settings. Decimal to
    // bind cleanly to the NumericUpDown controls (the LiftAngle pattern); seeded
    // from the persisted AcceptBandSettings on startup and applied live by the
    // window. Literal defaults match AcceptBandSettings.Default.
    private decimal _rateAcceptMin = -10m;
    private decimal _rateAcceptMax = 10m;
    private decimal _amplitudeAcceptMin = 270m;
    private decimal _amplitudeAcceptMax = 300m;
    private decimal _beatErrorAcceptMag = 0.6m;
    private double? _reviewCursorTimeS;
    private double _reviewMaximumS;
    private double _reviewMinimumS;
    private double _reviewSliderLeftPad;
    private double _reviewSliderRightPad;
    private string _reviewMetricsText = "";
    private bool _isLongTermTabActive;

    public MainWindowViewModel()
    {
        _playPauseCommand = new AsyncRelayCommand(ExecutePlayPauseAsync, () => IsPlayPauseEnabled);
        _stopCommand = new RelayCommand(() => _runner?.StopRunWithoutReset(), () => IsStopEnabled);
        _resetCommand = new RelayCommand(() => _runner?.Reset(), () => IsResetEnabled);
        _reviewStepBackCommand = new RelayCommand(() => StepReviewCursor(-ReviewStepS), () => IsReviewBarEnabled);
        _reviewStepForwardCommand = new RelayCommand(() => StepReviewCursor(ReviewStepS), () => IsReviewBarEnabled);
        _reviewLiveCommand = new RelayCommand(() => ReviewCursorTimeS = null, () => IsReviewBarEnabled);
    }

    /// <summary>
    /// Attaches the run-command runner the play/pause and reset commands invoke. Called once
    /// after the run-command service (which needs this view-model) is constructed, so the
    /// command bodies live here instead of being passed in from the View as delegates.
    /// </summary>
    public void AttachRunCommandRunner(IRunCommandRunner runner) => _runner = runner;

    // The play/pause button is one control: a stopped run starts, an active run toggles
    // pause/resume. The runner's state machine decides what each call actually does.
    private async Task ExecutePlayPauseAsync()
    {
        if (_runner is null)
        {
            return;
        }

        if (_runState == RunUiState.Stopped)
        {
            await _runner.StartAsync();
            return;
        }

        _runner.TogglePause();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand PlayPauseCommand => _playPauseCommand;
    public ICommand StopCommand => _stopCommand;
    public ICommand ResetCommand => _resetCommand;
    public ICommand ReviewStepBackCommand => _reviewStepBackCommand;
    public ICommand ReviewStepForwardCommand => _reviewStepForwardCommand;
    public ICommand ReviewLiveCommand => _reviewLiveCommand;

    public ObservableCollection<string> InputDeviceNames { get; } = new();
    public ObservableCollection<string> SampleRateLabels { get; } = new();
    public ObservableCollection<string> BphLabels { get; } = new();
    public ObservableCollection<string> SimBphLabels { get; } = new();

    public RunUiState RunState => _runState;

    public bool AreRunParametersEnabled => _runState == RunUiState.Stopped;

    public bool IsPlayPauseEnabled => _runState is RunUiState.Stopped or RunUiState.Running or RunUiState.Paused;

    public bool IsStopEnabled => _runState is RunUiState.Running or RunUiState.Paused or RunUiState.Stopping or RunUiState.StopFailed;

    public bool IsResetEnabled => _runState is RunUiState.Stopped or RunUiState.Paused or RunUiState.Stopping or RunUiState.StopFailed;

    public bool IsSampleRateEnabled => AreRunParametersEnabled && _modeAllowsSampleRate;

    // Simulation parameters configure the simulated source and only apply before a
    // run starts, so Live/Playback (and any active run) leave them disabled.
    public bool AreSimulationParametersEnabled => AreRunParametersEnabled && _modeAllowsSimulationParams;

    // Gain is a live knob (both platform workers forward SetVolume mid-capture,
    // matching the Qt original's slider), so it is gated by mode only, not by
    // run state.
    public bool IsGainEnabled => _modeAllowsGain;

    public string PlayPauseButtonText => _runState switch
    {
        RunUiState.Stopped => "Start",
        RunUiState.Paused => "Resume",
        _ => "Pause",
    };

    public bool IsPlayPauseButtonShowingPause => _runState == RunUiState.Running;

    public bool IsPlayPauseButtonShowingPlay => !IsPlayPauseButtonShowingPause;

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>True while a run is active but the detector has not yet locked the beat.</summary>
    public bool IsAwaitingBeatSync
    {
        get => _isAwaitingBeatSync;
        set => SetProperty(ref _isAwaitingBeatSync, value);
    }

    /// <summary>Latency / missed-beat readout shown on the right of the status bar.</summary>
    public string LatencyText
    {
        get => _latencyText;
        set => SetProperty(ref _latencyText, value);
    }

    public double Gain
    {
        get => _gain;
        set => SetProperty(ref _gain, value);
    }

    public int SelectedInputDeviceIndex
    {
        get => _selectedInputDeviceIndex;
        set => SetProperty(ref _selectedInputDeviceIndex, value);
    }

    public int SelectedSampleRateIndex
    {
        get => _selectedSampleRateIndex;
        set => SetProperty(ref _selectedSampleRateIndex, value);
    }

    public decimal AveragingPeriod
    {
        get => _averagingPeriod;
        set => SetProperty(ref _averagingPeriod, value);
    }

    public int SelectedBphIndex
    {
        get => _selectedBphIndex;
        set => SetProperty(ref _selectedBphIndex, value);
    }

    public decimal LiftAngle
    {
        get => _liftAngle;
        set => SetProperty(ref _liftAngle, value);
    }

    public int SelectedSimBphIndex
    {
        get => _selectedSimBphIndex;
        set => SetProperty(ref _selectedSimBphIndex, value);
    }

    public decimal SimErrorRate
    {
        get => _simErrorRate;
        set => SetProperty(ref _simErrorRate, value);
    }

    public decimal SimAmplitude
    {
        get => _simAmplitude;
        set => SetProperty(ref _simAmplitude, value);
    }

    public decimal SimBeatError
    {
        get => _simBeatError;
        set => SetProperty(ref _simBeatError, value);
    }

    public bool Realistic
    {
        get => _realistic;
        set => SetProperty(ref _realistic, value);
    }

    public string HighPassCutoffText
    {
        get => _highPassCutoffText;
        set => SetProperty(ref _highPassCutoffText, value);
    }

    public bool UseCOnset
    {
        get => _useCOnset;
        set => SetProperty(ref _useCOnset, value);
    }

    /// <summary>
    /// Opt-in post-lock weak-A onset rescue (a run parameter): lowers the onset
    /// trigger inside the phase-guide window so a weak A just below the trigger
    /// is caught instead of latching B. Off by default.
    /// </summary>
    public bool WeakAOnsetRescue
    {
        get => _weakAOnsetRescue;
        set => SetProperty(ref _weakAOnsetRescue, value);
    }

    public bool PauseOnPositionChange
    {
        get => _pauseOnPositionChange;
        set => SetProperty(ref _pauseOnPositionChange, value);
    }

    /// <summary>True when a watch-position change should auto-pause the run: the operator
    /// enabled the option and a run is active. Read by the run-control controller after a
    /// position edit; pure view-model state, so it is unit-testable without the window.</summary>
    public bool ShouldPauseOnPositionChange => _pauseOnPositionChange && _runState == RunUiState.Running;

    /// <summary>Scope Sweep window length as a multiple of the beat period (1x / 2x / 3x).</summary>
    public int SweepMultiple
    {
        get => _sweepMultiple;
        set => SetProperty(ref _sweepMultiple, value);
    }

    /// <summary>Beat Noise Scope 2 Σ averaging on/off (forwarded to the analysis worker).</summary>
    public bool SigmaAveraging
    {
        get => _sigmaAveraging;
        set => SetProperty(ref _sigmaAveraging, value);
    }

    public bool IsMeasurementLogEnabled
    {
        get => _isMeasurementLogEnabled;
        set => SetProperty(ref _isMeasurementLogEnabled, value);
    }

    /// <summary>Error Rate normal-band lower limit (s/d); applied live to every graph.</summary>
    public decimal RateAcceptMin
    {
        get => _rateAcceptMin;
        set => SetProperty(ref _rateAcceptMin, value);
    }

    /// <summary>Error Rate normal-band upper limit (s/d); applied live to every graph.</summary>
    public decimal RateAcceptMax
    {
        get => _rateAcceptMax;
        set => SetProperty(ref _rateAcceptMax, value);
    }

    /// <summary>Amplitude normal-band lower limit (°); applied live to every graph.</summary>
    public decimal AmplitudeAcceptMin
    {
        get => _amplitudeAcceptMin;
        set => SetProperty(ref _amplitudeAcceptMin, value);
    }

    /// <summary>Amplitude normal-band upper limit (°); applied live to every graph.</summary>
    public decimal AmplitudeAcceptMax
    {
        get => _amplitudeAcceptMax;
        set => SetProperty(ref _amplitudeAcceptMax, value);
    }

    /// <summary>Beat Error normal-band magnitude (± ms, symmetric about zero); applied live to every graph.</summary>
    public decimal BeatErrorAcceptMag
    {
        get => _beatErrorAcceptMag;
        set => SetProperty(ref _beatErrorAcceptMag, value);
    }

    /// <summary>Analysis block size in samples (the detector input window); read at run start, so a run restart applies a change.</summary>
    public decimal AnalysisBlockSize
    {
        get => _analysisBlockSize;
        set => SetProperty(ref _analysisBlockSize, value);
    }

    /// <summary>Audio capture buffer length in milliseconds; read at run start, so a run restart applies a change.</summary>
    public decimal CaptureBufferMs
    {
        get => _captureBufferMs;
        set => SetProperty(ref _captureBufferMs, value);
    }

    /// <summary>Active watch position as a <see cref="WatchPosition"/> ordinal (0 = CH, dial up).</summary>
    public int SelectedPositionIndex
    {
        get => _selectedPositionIndex;
        set
        {
            if (SetProperty(ref _selectedPositionIndex, value))
            {
                OnPropertyChanged(nameof(PositionLabel));
            }
        }
    }

    /// <summary>Always-visible status-bar indicator of the active watch position ("POS CH").</summary>
    public string PositionLabel => "POS " + ((WatchPosition)_selectedPositionIndex).ShortName();

    /// <summary>
    /// Pause-and-review scrub cursor (stream seconds), the
    /// AnalysisTabRenderContext.ReviewCursorTimeS contract: null = live (no
    /// cursor). Values clamp into the captured 0..<see cref="ReviewMaximumS"/>
    /// range. MainWindow re-renders the kept last frame when this moves while
    /// paused, so scrubbing inspects the recorded data without touching it.
    /// </summary>
    public double? ReviewCursorTimeS
    {
        get => _reviewCursorTimeS;
        set
        {
            double min = Math.Min(_reviewMinimumS, _reviewMaximumS);
            double? clamped = value is double timeS ? Math.Clamp(timeS, min, _reviewMaximumS) : null;
            if (SetProperty(ref _reviewCursorTimeS, clamped))
            {
                OnPropertyChanged(nameof(ReviewSliderValueS));
                OnPropertyChanged(nameof(ReviewReadoutText));
            }
        }
    }

    /// <summary>Latest captured stream time (s); the review slider's Maximum.</summary>
    public double ReviewMaximumS => _reviewMaximumS;

    /// <summary>First data point time (s); the review slider's Minimum (graph data start).</summary>
    public double ReviewMinimumS => _reviewMinimumS;

    /// <summary>
    /// Slider surface of the cursor: live (null cursor) reads as the latest
    /// captured time. Echo writes of the current effective value are ignored so
    /// the slider re-applying its value (e.g. when its Maximum binding moves)
    /// never enters review mode by itself.
    /// </summary>
    public double ReviewSliderValueS
    {
        get => _reviewCursorTimeS ?? _reviewMaximumS;
        set
        {
            if (IsReviewBarEnabled && value != (_reviewCursorTimeS ?? _reviewMaximumS))
            {
                ReviewCursorTimeS = value;
            }
        }
    }

    /// <summary>Review-bar readout: "REVIEW 83.0 s (01:23) / 12:34" while scrubbed, "LIVE 12:34" otherwise.
    /// The seconds value matches the graph X-axis so the user can correlate slider position to graph.</summary>
    public string ReviewReadoutText => _reviewCursorTimeS is double timeS
        ? $"REVIEW {timeS:F1} s ({FormatStreamTime(timeS)}) / {FormatStreamTime(_reviewMaximumS)}"
        : "LIVE " + FormatStreamTime(_reviewMaximumS);

    public string ReviewMetricsText => _reviewMetricsText;

    public bool IsReviewBarEnabled => _runState == RunUiState.Paused;

    public void SetLongTermTabActive(bool active)
    {
        if (_isLongTermTabActive == active) return;
        _isLongTermTabActive = active;
        if (!active)
        {
            // Review scrubbing is a Long-Term-only feature; leaving the tab returns
            // every other tab to live so a now-hidden cursor can't keep driving them.
            ReviewCursorTimeS = null;
        }
    }

    /// <summary>
    /// Left/right pixel padding for the review slider so it aligns with the
    /// active graph's X-axis data area. Updated by the renderer after layout.
    /// The slider thumb half-width (3px for 6px thumb) is subtracted so the
    /// track center (the position indicator) starts at the data area edge.
    /// </summary>
    private const double SliderThumbHalfWidth = 3.0;

    /// <summary>Left review-slider margin (px): the alignment pad minus the thumb half-width, clamped
    /// to ≥ 0. UI-neutral so the view-model holds no layout type; the View assembles the slider's
    /// layout thickness (0 top, 2 bottom) from this and <see cref="ReviewSliderRightMargin"/> via a converter.</summary>
    public double ReviewSliderLeftMargin => Math.Max(0, _reviewSliderLeftPad - SliderThumbHalfWidth);

    /// <summary>Right review-slider margin (px): the alignment pad minus the thumb half-width, clamped to ≥ 0.</summary>
    public double ReviewSliderRightMargin => Math.Max(0, _reviewSliderRightPad - SliderThumbHalfWidth);

    public void UpdateReviewSliderAlignment(double leftPad, double rightPad, double dataMinTimeS)
    {
        bool marginChanged = Math.Abs(leftPad - _reviewSliderLeftPad) >= 0.5 ||
                             Math.Abs(rightPad - _reviewSliderRightPad) >= 0.5;
        bool minChanged = Math.Abs(dataMinTimeS - _reviewMinimumS) >= 0.01;

        if (!marginChanged && !minChanged)
        {
            return;
        }

        if (marginChanged)
        {
            _reviewSliderLeftPad = leftPad;
            _reviewSliderRightPad = rightPad;
            OnPropertyChanged(nameof(ReviewSliderLeftMargin));
            OnPropertyChanged(nameof(ReviewSliderRightMargin));
        }

        if (minChanged)
        {
            _reviewMinimumS = dataMinTimeS;
            OnPropertyChanged(nameof(ReviewMinimumS));
        }
    }

    public void UpdateReviewMetricsText(string text)
    {
        if (text == _reviewMetricsText)
        {
            return;
        }

        _reviewMetricsText = text;
        OnPropertyChanged(nameof(ReviewMetricsText));
    }

    /// <summary>
    /// Grows the captured review range to the newest rendered stream time.
    /// Monotonic within a session: late or history-less frames never shrink the
    /// scrub range; only <see cref="ResetReview"/> (a new session) clears it.
    /// </summary>
    public void UpdateReviewMaximum(double latestTimeS)
    {
        if (latestTimeS <= _reviewMaximumS)
        {
            return;
        }

        _reviewMaximumS = latestTimeS;
        OnPropertyChanged(nameof(ReviewMaximumS));
        OnPropertyChanged(nameof(ReviewSliderValueS));
        OnPropertyChanged(nameof(ReviewReadoutText));
    }

    /// <summary>Clears the scrub cursor and captured range for a new measurement session.</summary>
    public void ResetReview()
    {
        ReviewCursorTimeS = null;
        UpdateReviewMetricsText("");
        if (_reviewMaximumS != 0.0 || _reviewMinimumS != 0.0)
        {
            _reviewMaximumS = 0.0;
            _reviewMinimumS = 0.0;
            OnPropertyChanged(nameof(ReviewMaximumS));
            OnPropertyChanged(nameof(ReviewMinimumS));
            OnPropertyChanged(nameof(ReviewSliderValueS));
            OnPropertyChanged(nameof(ReviewReadoutText));
        }
    }

    private void StepReviewCursor(double deltaS)
    {
        // Stepping from live starts at the newest reading; the setter clamps.
        ReviewCursorTimeS = (_reviewCursorTimeS ?? _reviewMaximumS) + deltaS;
    }

    private static string FormatStreamTime(double seconds)
    {
        int total = Math.Max(0, (int)seconds);
        return $"{total / 60:00}:{total % 60:00}";
    }

    public void SetModeAllowsSampleRate(bool value)
    {
        if (_modeAllowsSampleRate == value)
        {
            return;
        }

        _modeAllowsSampleRate = value;
        OnPropertyChanged(nameof(IsSampleRateEnabled));
    }

    public void SetModeAllowsGain(bool value)
    {
        if (_modeAllowsGain == value)
        {
            return;
        }

        _modeAllowsGain = value;
        OnPropertyChanged(nameof(IsGainEnabled));
    }

    public void SetModeAllowsSimulationParameters(bool value)
    {
        if (_modeAllowsSimulationParams == value)
        {
            return;
        }

        _modeAllowsSimulationParams = value;
        OnPropertyChanged(nameof(AreSimulationParametersEnabled));
    }

    public void SetInputDeviceNames(IEnumerable<string> values) => ReplaceItems(InputDeviceNames, values);

    public void SetSampleRateLabels(IEnumerable<string> values) => ReplaceItems(SampleRateLabels, values);

    public void SetBphLabels(IEnumerable<string> values) => ReplaceItems(BphLabels, values);

    public void SetSimBphLabels(IEnumerable<string> values) => ReplaceItems(SimBphLabels, values);

    public void SetStarting() => SetRunState(RunUiState.Starting);

    public void SetRunning() => SetRunState(RunUiState.Running);

    public void SetPaused() => SetRunState(RunUiState.Paused);

    public void SetStopping() => SetRunState(RunUiState.Stopping);

    public void SetStopFailed() => SetRunState(RunUiState.StopFailed);

    public void SetStopped() => SetRunState(RunUiState.Stopped);

    private void SetRunState(RunUiState value)
    {
        if (_runState == value)
        {
            return;
        }

        // Leaving pause ends review mode. The cursor must clear BEFORE the
        // state mutates: MainWindow's re-route of the kept frame is gated on
        // RunState == Paused, so clearing afterwards never re-renders and a
        // stop from a scrubbed pause would leave the dotted cursor line on
        // screen (resume relied on the next live frame by luck).
        if (_runState == RunUiState.Paused)
        {
            ReviewCursorTimeS = null;
        }

        _runState = value;
        OnPropertyChanged(nameof(RunState));
        OnPropertyChanged(nameof(IsReviewBarEnabled));
        OnPropertyChanged(nameof(AreRunParametersEnabled));
        OnPropertyChanged(nameof(IsPlayPauseEnabled));
        OnPropertyChanged(nameof(IsStopEnabled));
        OnPropertyChanged(nameof(IsResetEnabled));
        OnPropertyChanged(nameof(IsSampleRateEnabled));
        OnPropertyChanged(nameof(AreSimulationParametersEnabled));
        OnPropertyChanged(nameof(PlayPauseButtonText));
        OnPropertyChanged(nameof(IsPlayPauseButtonShowingPause));
        OnPropertyChanged(nameof(IsPlayPauseButtonShowingPlay));
        _playPauseCommand.NotifyCanExecuteChanged();
        _stopCommand.NotifyCanExecuteChanged();
        _resetCommand.NotifyCanExecuteChanged();
        _reviewStepBackCommand.NotifyCanExecuteChanged();
        _reviewStepForwardCommand.NotifyCanExecuteChanged();
        _reviewLiveCommand.NotifyCanExecuteChanged();
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private static void ReplaceItems(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (string value in values)
        {
            target.Add(value);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
