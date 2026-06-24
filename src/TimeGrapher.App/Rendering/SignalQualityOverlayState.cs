using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class SignalQualityOverlayState
{
    public const int FadeStartCleanCount = 10;
    public const int HideCleanCount = 100;

    private SignalQualityFlags _lastWarning;
    private int _cleanCount;

    public bool Update(SignalQualityFlags quality, out string text, out byte alpha)
    {
        if (quality != SignalQualityFlags.None)
        {
            _lastWarning = quality;
            _cleanCount = 0;
            text = SignalQualityText.Overlay(quality);
            alpha = byte.MaxValue;
            return text.Length > 0;
        }

        if (_lastWarning == SignalQualityFlags.None)
        {
            text = string.Empty;
            alpha = 0;
            return false;
        }

        _cleanCount++;
        if (_cleanCount >= HideCleanCount)
        {
            _lastWarning = SignalQualityFlags.None;
            text = string.Empty;
            alpha = 0;
            return false;
        }

        text = SignalQualityText.Overlay(_lastWarning);
        alpha = FadeAlpha(_cleanCount);
        return text.Length > 0;
    }

    public static uint WithAlpha(uint color, byte alpha) => (color & 0x00FFFFFFu) | ((uint)alpha << 24);

    public void Reset()
    {
        _lastWarning = SignalQualityFlags.None;
        _cleanCount = 0;
    }

    private static byte FadeAlpha(int cleanCount)
    {
        if (cleanCount <= FadeStartCleanCount)
        {
            return byte.MaxValue;
        }

        double fadeProgress = (cleanCount - FadeStartCleanCount) /
            (double)(HideCleanCount - FadeStartCleanCount);
        double opacity = Math.Clamp(1.0 - fadeProgress, 0.0, 1.0);
        return (byte)Math.Round(byte.MaxValue * opacity);
    }
}