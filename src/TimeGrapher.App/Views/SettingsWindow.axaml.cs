using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TimeGrapher.App.Views;

/// <summary>
/// Standalone popup for the static run options (the gear button opens it).
/// Binds its checkboxes to the shared <c>MainWindowViewModel</c> supplied as
/// DataContext, so toggles flow to the same run-settings properties the left
/// panel and run lifecycle already observe. Custom chrome (no system
/// decorations) matches MainWindow / SplashWindow.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
