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
            Height = 600,
        };

        Control content = Assert.IsAssignableFrom<Control>(window.Content);
        content.Measure(new Size(760, 600));
        content.Arrange(new Rect(0, 0, 760, 600));

        AssertTwoWay(window, "UseConsetToggleSwitch", v => vm.UseCOnset = v, () => vm.UseCOnset);
        AssertTwoWay(window, "WeakAOnsetRescueToggleSwitch", v => vm.WeakAOnsetRescue = v, () => vm.WeakAOnsetRescue);
        AssertSliderTwoWay(window, "WeakAOnsetRescueStrengthSlider", v => vm.WeakAOnsetRescueStrengthStep = v, () => vm.WeakAOnsetRescueStrengthStep);
        AssertTwoWay(window, "SpuriousBeatRejectionToggleSwitch", v => vm.SpuriousBeatRejection = v, () => vm.SpuriousBeatRejection);
        AssertTwoWay(window, "PauseOnPositionChangeToggleSwitch", v => vm.PauseOnPositionChange = v, () => vm.PauseOnPositionChange);
        AssertTwoWay(window, "MeasurementLogEnabledToggleSwitch", v => vm.IsMeasurementLogEnabled = v, () => vm.IsMeasurementLogEnabled);
    }

    [Fact]
    public void ResetSettingsButton_IsLeftOfCloseButtonAndUsesViewModelCommand()
    {
        HeadlessPlatform.EnsureStarted();

        var runner = new RecordingSettingsWindowResetRunner();
        var vm = new MainWindowViewModel();
        vm.AttachSettingsWindowResetRunner(runner);
        var window = new SettingsWindow
        {
            DataContext = vm,
            Width = 760,
            Height = 600,
        };

        Control content = Assert.IsAssignableFrom<Control>(window.Content);
        content.Measure(new Size(760, 600));
        content.Arrange(new Rect(0, 0, 760, 600));

        var reset = Assert.IsType<Button>(window.FindControl<Control>("ResetSettingsButton"));
        var close = Assert.IsType<Button>(window.FindControl<Control>("CloseSettingsButton"));
        var parent = Assert.IsType<StackPanel>(reset.Parent);

        Assert.Same(parent, close.Parent);
        Assert.True(parent.Children.IndexOf(reset) < parent.Children.IndexOf(close));
        Assert.Equal("Reset Defaults", reset.Content);
        Assert.Contains("TitleBarTextButton", reset.Classes);
        Assert.DoesNotContain("TitleBarIconButton", reset.Classes);
        Assert.True(reset.MinWidth >= 124);
        Assert.Same(vm.ResetSettingsWindowCommand, reset.Command);
        vm.ResetSettingsWindowCommand.Execute(null);
        Assert.Equal(1, runner.ResetCount);
        vm.SetRunning();
        Assert.False(vm.ResetSettingsWindowCommand.CanExecute(null));
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

    private static void AssertSliderTwoWay(SettingsWindow window, string sliderName, Action<int> setSource, Func<int> readSource)
    {
        var slider = Assert.IsType<Slider>(window.FindControl<Control>(sliderName));

        setSource(0);
        Assert.Equal(0.0, slider.Value);
        setSource(10);
        Assert.Equal(10.0, slider.Value);

        slider.Value = 3;
        Assert.Equal(3, readSource());
    }

    private sealed class RecordingSettingsWindowResetRunner : ISettingsWindowResetRunner
    {
        public int ResetCount { get; private set; }

        public void ResetSettingsWindow() => ResetCount++;
    }
}
