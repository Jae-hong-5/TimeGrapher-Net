using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace TimeGrapher.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ShowStartupWindow(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void ShowStartupWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Views.SplashWindow splashWindow;
        try
        {
            splashWindow = new Views.SplashWindow();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Splash startup failed: " + ex.Message);
            desktop.MainWindow = new Views.MainWindow();
            return;
        }

        Views.MainWindow? mainWindow = null;
        bool switchedToMain = false;

        void PrepareMainWindow()
        {
            mainWindow ??= new Views.MainWindow();
        }

        void SwitchToMainWindow(bool hideSplashFirst)
        {
            if (switchedToMain)
            {
                return;
            }

            switchedToMain = true;
            if (hideSplashFirst)
            {
                splashWindow.Hide();
            }

            PrepareMainWindow();
            Views.MainWindow window = mainWindow ?? throw new InvalidOperationException("Main window was not created.");
            desktop.MainWindow = window;
            window.Show();
            splashWindow.Close();
        }

        // MainWindow is built when playback completes (inside SwitchToMainWindow):
        // constructing it during playback blocks the UI thread for hundreds of ms and
        // visibly freezes the animation. The last splash frame lingering briefly while
        // the main window builds is far less noticeable than a mid-animation stall.
        splashWindow.PlaybackCompleted += (_, _) => SwitchToMainWindow(hideSplashFirst: false);
        splashWindow.PlaybackSkipped += (_, _) => SwitchToMainWindow(hideSplashFirst: true);
        desktop.MainWindow = splashWindow;
    }
}
