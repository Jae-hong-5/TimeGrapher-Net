using Avalonia;
using TimeGrapher.App;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Starts the Avalonia platform once per test process for tests that need real
/// layout or rendering. <c>SetupWithoutStarting</c> may only be called once, so
/// the guard is shared (keyed off <see cref="Application.Current"/>) rather than
/// per-class — two classes each tracking their own flag would double-initialise
/// depending on run order.
/// </summary>
internal static class HeadlessPlatform
{
    private static readonly object Gate = new();

    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (Application.Current is null)
            {
                Program.BuildAvaloniaApp().SetupWithoutStarting();
            }
        }
    }
}
