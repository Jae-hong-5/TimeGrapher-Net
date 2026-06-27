namespace TimeGrapher.App;

internal static class WeakAOnsetRescueStrengthPolicy
{
    public const int MinStep = 0;
    public const int StandardStep = 5;
    public const int MaxStep = 10;
    public const double SaferScale = 1.25;
    public const double StepScaleDelta = 0.05;

    public static bool IsValidStep(int step) => step >= MinStep && step <= MaxStep;

    public static int NormalizeStep(int step) => Math.Clamp(step, MinStep, MaxStep);

    public static double ScaleFromStep(int step) => SaferScale - NormalizeStep(step) * StepScaleDelta;
}
