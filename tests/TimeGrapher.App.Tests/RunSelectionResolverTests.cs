using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Detection;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class RunSelectionResolverTests
{
    private static readonly int[] AveragingPeriods = { 2, 4, 8, 10, 12, 20, 20, 30 };

    [Fact]
    public void DefaultIndicesResolveByMeaningfulValues()
    {
        MainWindowViewModel vm = CreateViewModel();
        RunSelectionResolver resolver = CreateResolver(vm);

        Assert.Equal(RunSelectionResolver.DefaultAveragingPeriodSeconds, AveragingPeriods[resolver.DefaultAveragingPeriodIndex]);
        Assert.Equal(RunSelectionResolver.DefaultSimulationBph, BphCatalog.ManualBph[resolver.DefaultSimulationBphIndex]);
    }

    [Fact]
    public void AnalysisSelectionValidatesSelectedIndices()
    {
        MainWindowViewModel vm = CreateViewModel();
        RunSelectionResolver resolver = CreateResolver(vm);

        vm.SelectedAveragingPeriodIndex = 5;
        vm.SelectedBphIndex = 0;

        AnalysisSelection auto = resolver.GetAnalysisSelection();

        Assert.True(auto.AutoBph);
        Assert.Equal(20, auto.AveragingPeriod);
        Assert.Equal(0, auto.ManualBph);

        vm.SelectedBphIndex = 6;

        AnalysisSelection manual = resolver.GetAnalysisSelection();

        Assert.False(manual.AutoBph);
        Assert.Equal(BphCatalog.ManualAutoBph[6], manual.ManualBph);

        vm.SelectedAveragingPeriodIndex = -1;

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => resolver.GetAnalysisSelection());
        Assert.Equal("No valid averaging period is selected.", ex.Message);
    }

    [Fact]
    public void SimulationSelectionUsesOnlyAdvertisedSampleRates()
    {
        MainWindowViewModel vm = CreateViewModel();
        RunSelectionResolver resolver = CreateResolver(vm);
        int[] availableSampleRates = { 48000, 96000, 192000, 0, 0 };

        vm.SelectedSimBphIndex = resolver.DefaultSimulationBphIndex;
        vm.SelectedSampleRateIndex = 1;

        SimulationSelection selection = resolver.GetSimulationSelection(availableSampleRates, availableSampleRateCount: 2);

        Assert.Equal(RunSelectionResolver.DefaultSimulationBph, selection.Bph);
        Assert.Equal(96000, selection.SampleRate);

        vm.SelectedSampleRateIndex = 2;

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            resolver.GetSimulationSelection(availableSampleRates, availableSampleRateCount: 2));
        Assert.Equal("No valid sample rate is selected.", ex.Message);
    }

    [Fact]
    public void SelectedSampleRateRequiresAnAdvertisedRate()
    {
        MainWindowViewModel vm = CreateViewModel();
        RunSelectionResolver resolver = CreateResolver(vm);
        int[] availableSampleRates = { 48000, 96000, 0 };

        vm.SelectedSampleRateIndex = 1;
        Assert.Equal(96000, resolver.GetSelectedSampleRate(availableSampleRates, availableSampleRateCount: 2));
        Assert.True(resolver.TryGetSelectedSampleRate(availableSampleRates, availableSampleRateCount: 2, out int validRate));
        Assert.Equal(96000, validRate);

        vm.SelectedSampleRateIndex = -1;
        InvalidOperationException negativeIndex = Assert.Throws<InvalidOperationException>(
            () => resolver.GetSelectedSampleRate(availableSampleRates, availableSampleRateCount: 2));
        Assert.Equal("No valid sample rate is selected.", negativeIndex.Message);
        Assert.False(resolver.TryGetSelectedSampleRate(availableSampleRates, availableSampleRateCount: 2, out _));

        vm.SelectedSampleRateIndex = 0;
        InvalidOperationException emptyAdvertisedRates = Assert.Throws<InvalidOperationException>(
            () => resolver.GetSelectedSampleRate(availableSampleRates, availableSampleRateCount: 0));
        Assert.Equal("No valid sample rate is selected.", emptyAdvertisedRates.Message);
        Assert.False(resolver.TryGetSelectedSampleRate(availableSampleRates, availableSampleRateCount: 0, out _));
    }

    private static RunSelectionResolver CreateResolver(MainWindowViewModel vm)
    {
        return new RunSelectionResolver(
            vm,
            AveragingPeriods,
            BphCatalog.ManualAutoBph,
            BphCatalog.ManualBph);
    }

    private static MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel();
    }
}
