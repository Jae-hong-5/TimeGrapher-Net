using System.ComponentModel;
using TimeGrapher.App.ViewModels;

namespace TimeGrapher.App.Services;

internal sealed class MainWindowSelectionCoordinator
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IMainWindowSelectionOperations _operations;
    private readonly MainWindowSelectionOptions _options;
    private int _suppressDepth;

    public MainWindowSelectionCoordinator(
        MainWindowViewModel viewModel,
        IMainWindowSelectionOperations operations,
        MainWindowSelectionOptions options)
    {
        _viewModel = viewModel;
        _operations = operations;
        _options = options;
    }

    public int CurrentInputDeviceNumber
    {
        get
        {
            int index = _viewModel.SelectedInputDeviceIndex;
            return index >= 0 && index < _operations.InputDeviceNumbers.Count
                ? _operations.InputDeviceNumbers[index]
                : -1;
        }
    }

    public string CurrentInputDeviceText => ItemText(_viewModel.InputDeviceNames, _viewModel.SelectedInputDeviceIndex);

    public RunCommandMode CurrentMode
    {
        get
        {
            string source = CurrentInputDeviceText;
            if (source == _options.PlaybackSourceName)
            {
                return RunCommandMode.Playback;
            }

            if (source == _options.SimulationSourceName)
            {
                return RunCommandMode.Simulation;
            }

            return string.IsNullOrEmpty(source) ? RunCommandMode.Unknown : RunCommandMode.Live;
        }
    }

    private bool IsSuppressed => _suppressDepth > 0;

    public IDisposable SuppressEvents()
    {
        _suppressDepth++;
        return new SuppressionScope(this);
    }

    public void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.SelectedInputDeviceIndex):
                OnSelectedInputDeviceChanged();
                break;
            case nameof(MainWindowViewModel.SelectedSampleRateIndex):
                OnSelectedSampleRateChanged();
                break;
            case nameof(MainWindowViewModel.Gain):
                OnGainChanged();
                break;
        }
    }

    public void SetSelectedInputDeviceIndex(int index, bool forceChanged = false)
    {
        if (_viewModel.SelectedInputDeviceIndex == index)
        {
            if (forceChanged)
            {
                OnSelectedInputDeviceChanged();
            }
            return;
        }

        _viewModel.SelectedInputDeviceIndex = index;
    }

    public void SetSelectedSampleRateIndex(int index, bool forceChanged = false)
    {
        if (_viewModel.SelectedSampleRateIndex == index)
        {
            if (forceChanged)
            {
                OnSelectedSampleRateChanged();
            }
            return;
        }

        _viewModel.SelectedSampleRateIndex = index;
    }

    public bool SetAudioRate(int rate)
    {
        for (int i = 0; i < _operations.AvailableSampleRateCount; i++)
        {
            if (_operations.GetAvailableSampleRate(i) == rate)
            {
                SetSelectedSampleRateIndex(i);
                return true;
            }
        }

        return false;
    }

    public bool SetAudioDevice(string name)
    {
        int index = FindText(_viewModel.InputDeviceNames, name, matchContains: false);
        if (index == -1)
        {
            return false;
        }

        SetSelectedInputDeviceIndex(index);
        return true;
    }

    private void OnSelectedInputDeviceChanged()
    {
        if (IsSuppressed)
        {
            return;
        }

        RunCommandMode mode = CurrentMode;
        bool isLive = mode == RunCommandMode.Live;
        int deviceNumber = isLive ? CurrentInputDeviceNumber : -1;

        _viewModel.SetModeAllowsSampleRate(RunCommandModePolicies.AllowsSelectableSampleRate(mode));
        _viewModel.SetModeAllowsGain(RunCommandModePolicies.AllowsGain(mode));
        _viewModel.SetModeAllowsSimulationParameters(RunCommandModePolicies.AllowsSimulationParameters(mode));
        _operations.PopulateSampleRates(deviceNumber);
    }

    private void OnSelectedSampleRateChanged()
    {
        if (IsSuppressed)
        {
            return;
        }

        int index = _viewModel.SelectedSampleRateIndex;
        if (index < 0 || index >= _operations.AvailableSampleRateCount)
        {
            return;
        }

        int sampleRate = _operations.GetAvailableSampleRate(index);
        _operations.SetCurrentSampleRate(sampleRate);
    }

    private void OnGainChanged()
    {
        _operations.SetAudioInputVolume((float)(_viewModel.Gain / 1000.0));
    }

    internal static string ItemText(IReadOnlyList<string> items, int index)
    {
        return index >= 0 && index < items.Count ? items[index] : "";
    }

    internal static int FindText(IReadOnlyList<string> items, string text, bool matchContains)
    {
        for (int i = 0; i < items.Count; i++)
        {
            string itemText = ItemText(items, i);
            if (matchContains)
            {
                if (itemText.Contains(text, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            else if (itemText == text)
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class SuppressionScope : IDisposable
    {
        private MainWindowSelectionCoordinator? _owner;

        public SuppressionScope(MainWindowSelectionCoordinator owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            MainWindowSelectionCoordinator? owner = _owner;
            if (owner == null)
            {
                return;
            }

            owner._suppressDepth--;
            _owner = null;
        }
    }
}
