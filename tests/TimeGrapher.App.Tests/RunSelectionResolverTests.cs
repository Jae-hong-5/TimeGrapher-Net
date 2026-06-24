using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Detection;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class RunSelectionResolverTests
{
    [Fact]
    public void DefaultSimulationIndexResolvesByMeaningfulValue()
    {
        MainWindowViewModel vm = CreateViewModel();
        RunSelectionResolver resolver = CreateResolver(vm);

        Assert.Equal(RunSelectionResolver.DefaultSimulationBph, BphCatalog.ManualBph[resolver.DefaultSimulationBphIndex]);
    }

    [Fact]
    public void AnalysisSelectionUsesNumericAveragingPeriod()
    {
        MainWindowViewModel vm = CreateViewModel();
        RunSelectionResolver resolver = CreateResolver(vm);

        vm.AveragingPeriod = 17m;
        vm.SelectedBphIndex = 0;

        AnalysisSelection auto = resolver.GetAnalysisSelection();

        Assert.True(auto.AutoBph);
        Assert.Equal(17, auto.AveragingPeriod);
        Assert.Equal(0, auto.ManualBph);

        vm.SelectedBphIndex = 6;

        AnalysisSelection manual = resolver.GetAnalysisSelection();

        Assert.False(manual.AutoBph);
        Assert.Equal(BphCatalog.ManualAutoBph[6], manual.ManualBph);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    [InlineData(17.5)]
    public void AnalysisSelectionRejectsOutOfRangeOrFractionalAveragingPeriod(decimal averagingPeriod)
    {
        MainWindowViewModel vm = CreateViewModel();
        RunSelectionResolver resolver = CreateResolver(vm);

        vm.AveragingPeriod = averagingPeriod;
        vm.SelectedBphIndex = 0;

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
            BphCatalog.ManualAutoBph,
            BphCatalog.ManualBph);
    }

    private static MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel();
    }
}
