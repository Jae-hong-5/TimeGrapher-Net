using System;
using Avalonia;
using Avalonia.Controls;
using TimeGrapher.App.ViewModels;
using TimeGrapher.App.Views;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// The Settings run-option controls were converted from CheckBox to ToggleSwitch. The XAML
/// attribute tests prove the binding strings exist; these headless tests prove the bindings are
/// live two-way: a view-model edit flips the switch, and flipping the switch updates the view
/// model. That exercises the actual control behaviour the conversion changed.
/// </summary>
public sealed class SettingsToggleBindingTests
{
    [Fact]
    public void SettingsToggleSwitches_BindTwoWayToTheViewModel()
    {
        HeadlessPlatform.EnsureStarted();

        var vm = new MainWindowViewModel();
        var window = new SettingsWindow
        {
            DataContext = vm,
            Width = 760,
            Height = 520,
        };

        Control content = Assert.IsAssignableFrom<Control>(window.Content);
        content.Measure(new Size(760, 520));
        content.Arrange(new Rect(0, 0, 760, 520));

        AssertTwoWay(window, "UseConsetToggleSwitch", v => vm.UseCOnset = v, () => vm.UseCOnset);
        AssertTwoWay(window, "PllEventVetoToggleSwitch", v => vm.PllEventVeto = v, () => vm.PllEventVeto);
        AssertTwoWay(window, "PauseOnPositionChangeToggleSwitch", v => vm.PauseOnPositionChange = v, () => vm.PauseOnPositionChange);
        AssertTwoWay(window, "MeasurementLogEnabledToggleSwitch", v => vm.IsMeasurementLogEnabled = v, () => vm.IsMeasurementLogEnabled);
    }

    private static void AssertTwoWay(SettingsWindow window, string toggleName, Action<bool> setSource, Func<bool> readSource)
    {
        var toggle = Assert.IsType<ToggleSwitch>(window.FindControl<Control>(toggleName));

        // source -> target: a view-model edit drives the switch.
        setSource(true);
        Assert.Equal(true, toggle.IsChecked);
        setSource(false);
        Assert.Equal(false, toggle.IsChecked);

        // target -> source: flipping the switch drives the view model.
        toggle.IsChecked = true;
        Assert.True(readSource());
        toggle.IsChecked = false;
        Assert.False(readSource());
    }
}
