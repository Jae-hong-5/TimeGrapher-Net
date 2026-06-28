using System.Collections.Generic;

using TimeGrapher.App.Services;

namespace TimeGrapher.App.Views;

public partial class MainWindow
{
    private sealed class MainWindowSelectionOperations : IMainWindowSelectionOperations
    {
        private readonly AudioSelectionState _state;
        private readonly AudioDeviceController _deviceController;
        private IRunSessionLiveAdjustments? _liveAdjustments;

        public MainWindowSelectionOperations(
            AudioSelectionState state,
            AudioDeviceController deviceController)
        {
            _state = state;
            _deviceController = deviceController;
        }

        // The run-session controller is built by the bootstrapper, which itself takes this
        // adapter as input, so the live-adjustment seam is late-attached once Build returns
        // (the same construction-cycle break as ISelectionEventGate). This replaces the former
        // _owner (window) back-reference.
        public void AttachRunSessionLiveAdjustments(IRunSessionLiveAdjustments liveAdjustments)
        {
            _liveAdjustments = liveAdjustments;
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
            _liveAdjustments!.SetLiveInputVolume(normalizedVolume);
        }

        public void SetLiveSimulationParameters(
            double rateErrorSPerDay,
            double beatErrorMs,
            double watchAmplitudeDegrees,
            double aClusterLevelScale,
            double bClusterLevelScale,
            double cClusterLevelScale)
        {
            _liveAdjustments!.SetLiveSimulationParameters(
                rateErrorSPerDay,
                beatErrorMs,
                watchAmplitudeDegrees,
                aClusterLevelScale,
                bClusterLevelScale,
                cClusterLevelScale);
        }
    }
}
