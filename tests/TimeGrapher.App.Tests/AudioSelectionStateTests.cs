using TimeGrapher.App.Services;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// AudioSelectionState holds the input-device / sample-rate selection state lifted out of the
/// MainWindow. These lock the behavior that used to be inline: the device list, the current sample
/// rate default, and the fixed 5-entry sample-rate buffer with its cap.
/// </summary>
public sealed class AudioSelectionStateTests
{
    [Fact]
    public void CurrentSampleRate_DefaultsToStartupRate()
    {
        Assert.Equal(48000, new AudioSelectionState().CurrentSampleRate);
    }

    [Fact]
    public void InputDevices_AddAndClear()
    {
        var state = new AudioSelectionState();

        state.AddInputDevice(3);
        state.AddInputDevice(-1);

        Assert.Equal(new[] { 3, -1 }, state.InputDeviceNumbers);

        state.ClearInputDevices();

        Assert.Empty(state.InputDeviceNumbers);
    }

    [Fact]
    public void SampleRates_AddTrackCountAndIndex()
    {
        var state = new AudioSelectionState();

        Assert.True(state.TryAddSampleRate(44100));
        Assert.True(state.TryAddSampleRate(48000));

        Assert.Equal(2, state.AvailableSampleRateCount);
        Assert.Equal(44100, state.GetAvailableSampleRate(0));
        Assert.Equal(48000, state.GetAvailableSampleRate(1));
    }

    [Fact]
    public void AvailableSampleRates_ExposesFixedBufferPairedWithCount()
    {
        var state = new AudioSelectionState();
        state.TryAddSampleRate(44100);
        state.TryAddSampleRate(48000);

        // The slice is a fixed 5-entry buffer; the resolver reads only the first
        // AvailableSampleRateCount entries (so callers must pair the two).
        Assert.Equal(5, state.AvailableSampleRates.Count);
        Assert.Equal(44100, state.AvailableSampleRates[0]);
        Assert.Equal(48000, state.AvailableSampleRates[1]);
        Assert.Equal(2, state.AvailableSampleRateCount);
    }

    [Fact]
    public void SampleRates_CapAtBufferSize()
    {
        var state = new AudioSelectionState();

        for (int i = 0; i < 5; i++)
        {
            Assert.True(state.TryAddSampleRate(8000 + i));
        }

        // The fixed buffer holds five entries; the sixth is rejected (mirrors the old int[5] cap).
        Assert.False(state.TryAddSampleRate(96000));
        Assert.Equal(5, state.AvailableSampleRateCount);
    }

    [Fact]
    public void ResetSampleRates_ClearsCount()
    {
        var state = new AudioSelectionState();
        state.TryAddSampleRate(44100);

        state.ResetSampleRates();

        Assert.Equal(0, state.AvailableSampleRateCount);
        Assert.True(state.TryAddSampleRate(48000));
        Assert.Equal(48000, state.GetAvailableSampleRate(0));
    }
}
