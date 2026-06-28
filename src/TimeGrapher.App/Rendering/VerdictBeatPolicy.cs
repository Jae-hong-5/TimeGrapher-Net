using System;

namespace TimeGrapher.App.Rendering;

internal static class VerdictBeatPolicy
{
    public const int DefaultMinimumBeats = 30;
    public const int MinimumBeatsFloor = 1;
    public const int MinimumBeatsCeiling = 999;
    public const int MinimumBeatsStep = 1;

    public static long MinimumBeats => AppSettings.Current.SettingsWindow.VerdictMinimumBeats;

    public static bool IsValidMinimumBeats(int value) =>
        value >= MinimumBeatsFloor &&
        value <= MinimumBeatsCeiling &&
        value % MinimumBeatsStep == 0;

    public static int NormalizeMinimumBeats(decimal value) =>
        NormalizeMinimumBeats((int)Math.Round(value, MidpointRounding.AwayFromZero));

    public static int NormalizeMinimumBeats(int value) =>
        Math.Clamp(value, MinimumBeatsFloor, MinimumBeatsCeiling);
}
