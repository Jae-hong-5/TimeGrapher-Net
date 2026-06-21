using System.Collections.Generic;

using TimeGrapher.App.Services;

namespace TimeGrapher.App.Views;

public partial class MainWindow
{
    private sealed class MainWindowSelectionOperations : IMainWindowSelectionOperations
    {
        private readonly MainWindow _owner;
        private readonly AudioSelectionState _state;

        public MainWindowSelectionOperations(MainWindow owner, AudioSelectionState state)
        {
            _owner = owner;
            _state = state;
        }

        public IReadOnlyList<int> InputDeviceNumbers => _state.InputDeviceNumbers;

        public int AvailableSampleRateCount => _state.AvailableSampleRateCount;

        public int GetAvailableSampleRate(int index)
        {
            return _state.GetAvailableSampleRate(index);
        }

        public void PopulateSampleRates(int deviceNumber)
        {
            // Still a View method (async device/rate probe) — relocated in the next unit.
            _owner.PopulateSampleRates(deviceNumber);
        }

        public void SetCurrentSampleRate(int sampleRate)
        {
            _state.CurrentSampleRate = sampleRate;
        }

        public void SetAudioInputVolume(float normalizedVolume)
        {
            _owner.mRunSessionController.SetLiveInputVolume(normalizedVolume);
        }
    }
}
