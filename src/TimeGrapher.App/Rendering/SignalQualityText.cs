using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal static class SignalQualityText
{
    public static string Summary(SignalQualityFlags quality)
    {
        if ((quality & SignalQualityFlags.NoSignal) != 0)
        {
            return "No signal";
        }

        if ((quality & SignalQualityFlags.ClippedSignal) != 0)
        {
            return "Clipping";
        }

        if ((quality & SignalQualityFlags.PossibleFalseC) != 0)
        {
            return "Possible false C";
        }

        if ((quality & SignalQualityFlags.CTimingUnstable) != 0)
        {
            return "C timing unstable";
        }

        if ((quality & SignalQualityFlags.NoisySignal) != 0)
        {
            return "Noisy signal";
        }

        if ((quality & SignalQualityFlags.WeakSignal) != 0)
        {
            return "Weak signal";
        }

        return string.Empty;
    }

    public static string Guidance(SignalQualityFlags quality) => quality switch
    {
        _ when (quality & SignalQualityFlags.NoSignal) != 0 => "No signal detected. Reposition the watch or check the microphone.",
        _ when (quality & SignalQualityFlags.ClippedSignal) != 0 => "Signal clipping detected. Reduce gain before trusting the reading.",
        _ when (quality & SignalQualityFlags.PossibleFalseC) != 0 => "Possible false C marker. Check Beat Noise and reduce handling noise.",
        _ when (quality & SignalQualityFlags.CTimingUnstable) != 0 => "C timing is unstable. Keep measuring or inspect the beat waveform.",
        _ when (quality & SignalQualityFlags.NoisySignal) != 0 => "Signal looks noisy. Reduce ambient or handling noise.",
        _ when (quality & SignalQualityFlags.WeakSignal) != 0 => "Weak signal. Reposition the watch or increase input gain.",
        _ => string.Empty,
    };
}
