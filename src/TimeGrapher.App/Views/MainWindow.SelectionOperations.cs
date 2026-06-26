using System.Collections.Generic;

using TimeGrapher.App.Services;

namespace TimeGrapher.App.Views;

public partial class MainWindow
{
    private sealed class MainWindowSelectionOperations : IMainWindowSelectionOperations
    {
        private readonly MainWindow _owner;
        private readonly AudioSelectionState _state;
        private readonly AudioDeviceController _deviceController;

        public MainWindowSelectionOperations(
            MainWindow owner,
            AudioSelectionState state,
            AudioDeviceController deviceController)
        {
            _owner = owner;
            _state = state;
            _deviceController = deviceController;
        }

        public IReadOnlyList<int> InputDeviceNumbers => _state.InputDeviceNumbers;

        public int AvailableSampleRateCount => _state.AvailableSampleRateCount;

        public int GetAvailableSampleRate(int index)
        {
            return _state.GetAvailableSampleRate(index);
        }

        public void PopulateSampleRates(int deviceNumber)
        {
            _deviceController.PopulateSampleRates(deviceNumber);
        }

        public void SetCurrentSampleRate(int sampleRate)
        {
            _state.CurrentSampleRate = sampleRate;
        }

        public void SetAudioInputVolume(float normalizedVolume)
        {
            _owner.mRunSessionController.SetLiveInputVolume(normalizedVolume);
        }

        public void SetLiveSimulationParameters(
            double rateErrorSPerDay,
            double beatErrorMs,
            double watchAmplitudeDegrees,
            double noiseScale,
            double aClusterLevelScale,
            double bClusterLevelScale,
            double cClusterLevelScale)
        {
            _owner.mRunSessionController.SetLiveSimulationParameters(
                rateErrorSPerDay,
                beatErrorMs,
                watchAmplitudeDegrees,
                noiseScale,
                aClusterLevelScale,
                bClusterLevelScale,
                cClusterLevelScale);
        }
    }
}
