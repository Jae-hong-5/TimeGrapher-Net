using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TimeGrapher.App.Views;

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
