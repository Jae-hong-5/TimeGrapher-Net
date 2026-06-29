using Xunit;

namespace TimeGrapher.Platform.WindowsAudio.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that skips off Windows instead of silently passing.
/// The Windows audio worker's TryStop/CaptureEnded tests exercise platform-only
/// behaviour, so on a non-Windows host they report SKIPPED rather than returning
/// early as a green pass.
/// </summary>
internal sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows-only";
        }
    }
}
