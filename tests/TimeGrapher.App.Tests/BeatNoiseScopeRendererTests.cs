using System.Reflection;
using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class BeatNoiseScopeRendererTests
{
    [Fact]
    public void Scope1DefaultsToRawMinMaxInsteadOfMirroredEnvelope()
    {
        var mainPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            mainPlot, new AvaPlot(), new AvaPlot(), new TextBlock(), new TextBlock());
        renderer.CreateGraphs();

        var segment = new BeatSegment
        {
            Samples = new float[] { 9.0f, 0.1f, 0.1f, 0.1f },
            RawValid = true,
            RawMin = new float[] { 0.0f, -0.6f, 0.0f, 0.0f },
            RawMax = new float[] { 0.8f, 0.1f, 0.0f, 0.0f },
            MsPerPoint = 0.25,
            AOffsetMs = 0.0,
        };
        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[] { segment },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        AxisLimits limits = mainPlot.Plot.Axes.GetLimits();
        Assert.InRange(limits.Top, 0.87, 0.89);
        Assert.InRange(limits.Bottom, -0.89, -0.87);
    }

    [Fact]
    public void Scope1YRangeDoesNotShrinkWhileWaveformStaysInsideCurrentMargin()
    {
        var mainPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            mainPlot, new AvaPlot(), new AvaPlot(), new TextBlock(), new TextBlock());
        renderer.CreateGraphs();

        var large = new BeatSegment
        {
            Samples = new float[] { 0.1f },
            RawValid = true,
            RawMin = new float[] { -1.0f },
            RawMax = new float[] { 1.0f },
            MsPerPoint = 0.25,
        };
        renderer.RenderFrame(new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[] { large },
            },
        }, new AnalysisTabRenderContext(SampleRate: 48000));
        AxisLimits initial = mainPlot.Plot.Axes.GetLimits();

        var small = new BeatSegment
        {
            Samples = new float[] { 0.1f },
            RawValid = true,
            RawMin = new float[] { -0.25f },
            RawMax = new float[] { 0.25f },
            MsPerPoint = 0.25,
        };
        renderer.RenderFrame(new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 2,
                Segments = new[] { small },
            },
        }, new AnalysisTabRenderContext(SampleRate: 48000));
        AxisLimits afterSmall = mainPlot.Plot.Axes.GetLimits();

        Assert.Equal(initial.Bottom, afterSmall.Bottom);
        Assert.Equal(initial.Top, afterSmall.Top);
    }

    [Fact]
    public void Scope1YRangeExpandsWhenWaveformExceedsCurrentMargin()
    {
        var mainPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            mainPlot, new AvaPlot(), new AvaPlot(), new TextBlock(), new TextBlock());
        renderer.CreateGraphs();

        var small = new BeatSegment
        {
            Samples = new float[] { 0.1f },
            RawValid = true,
            RawMin = new float[] { -0.25f },
            RawMax = new float[] { 0.25f },
            MsPerPoint = 0.25,
        };
        renderer.RenderFrame(new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[] { small },
            },
        }, new AnalysisTabRenderContext(SampleRate: 48000));
        AxisLimits initial = mainPlot.Plot.Axes.GetLimits();

        var large = new BeatSegment
        {
            Samples = new float[] { 0.1f },
            RawValid = true,
            RawMin = new float[] { -1.0f },
            RawMax = new float[] { 1.0f },
            MsPerPoint = 0.25,
        };
        renderer.RenderFrame(new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 2,
                Segments = new[] { large },
            },
        }, new AnalysisTabRenderContext(SampleRate: 48000));
        AxisLimits afterLarge = mainPlot.Plot.Axes.GetLimits();

        Assert.True(afterLarge.Bottom < initial.Bottom);
        Assert.True(afterLarge.Top > initial.Top);
        Assert.InRange(afterLarge.Bottom, -1.11, -1.09);
        Assert.InRange(afterLarge.Top, 1.09, 1.11);
    }

    [Fact]
    public void AbsoluteToggleOnShowsRectifiedEnvelopeForReadability()
    {
        var mainPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            mainPlot, new AvaPlot(), new AvaPlot(), new TextBlock(), new TextBlock());
        renderer.CreateGraphs();
        renderer.SetAbsoluteValue(true);

        var segment = new BeatSegment
        {
            Samples = new float[] { 9.0f, 0.1f, 0.1f, 0.1f },
            RawValid = true,
            RawMin = new float[] { 0.0f, -0.6f, 0.0f, 0.0f },
            RawMax = new float[] { 0.8f, 0.1f, 0.0f, 0.0f },
            MsPerPoint = 0.25,
            AOffsetMs = 0.0,
        };
        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[] { segment },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        AxisLimits limits = mainPlot.Plot.Axes.GetLimits();
        Assert.InRange(limits.Top, 9.89, 9.91);
        Assert.InRange(limits.Bottom, -0.20, -0.19);
    }

    [Fact]
    public void StripGraphCreatesDividerLinesBetweenEightSlots()
    {
        var stripPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            new AvaPlot(), stripPlot, new AvaPlot(), new TextBlock(), new TextBlock());

        renderer.CreateGraphs();

        VerticalLine[] dividers = stripPlot.Plot.GetPlottables<VerticalLine>()
            .Where(line => line.X > 0.0 && line.X < BeatNoiseScopeLogic.StripCount)
            .OrderBy(line => line.X)
            .ToArray();

        Assert.Equal(BeatNoiseScopeLogic.StripCount - 1, dividers.Length);
        Assert.Equal(Enumerable.Range(1, BeatNoiseScopeLogic.StripCount - 1).Select(i => (double)i), dividers.Select(line => line.X));
        Assert.All(dividers, line => Assert.True(line.LineWidth >= 2));
        Assert.All(dividers, line => Assert.True(line.LineColor.Alpha > 0.5));
    }

    [Fact]
    public void StripRenderVariesPointCountByViewMode()
    {
        var stripPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            new AvaPlot(), stripPlot, new AvaPlot(), new TextBlock(), new TextBlock());
        renderer.CreateGraphs();

        var segment = new BeatSegment
        {
            Samples = new float[160],
            MsPerPoint = 0.25,
            StartTimeS = 0.0,
        };
        var snapshot = new BeatSegmentsSnapshot
        {
            Version = 1,
            Segments = new[] { segment },
        };
        var frame = new AnalysisFrame { BeatSegments = snapshot };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        renderer.SetViewMode(BeatNoiseScopeViewMode.AverageAndStrip);
        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        Assert.Equal(BeatNoiseScopeViewMode.AverageAndStrip, renderer.ViewMode);
    }

    [Fact]
    public void EnvelopeModeShowsRedDottedCMarkersOnMainAndStrip()
    {
        var mainPlot = new AvaPlot();
        var stripPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            mainPlot, stripPlot, new AvaPlot(), new TextBlock(), new TextBlock());
        renderer.CreateGraphs();

        var segment = new BeatSegment
        {
            Samples = Enumerable.Repeat(0.5f, 80).ToArray(),
            MsPerPoint = 0.25,
            CPeakValid = true,
            CPeakOffsetMs = 2.0,
        };
        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[] { segment },
                Markers = new[]
                {
                    new BeatNoiseMarker { TimeS = segment.CPeakOffsetMs / 1000.0, Kind = BeatNoiseMarkerKind.CPeak },
                },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        VerticalLine mainCMarker = mainPlot.Plot.GetPlottables<VerticalLine>()
            .Single(line => line.IsVisible && line.X == segment.CPeakOffsetMs && Equals(line.LinePattern, LinePattern.Dotted));
        Assert.Equal(LinePattern.Dotted, mainCMarker.LinePattern);

        VerticalLine stripCMarker = stripPlot.Plot.GetPlottables<VerticalLine>()
            .Single(line => line.IsVisible && line.X > 7.0 && line.X < 8.0 && Equals(line.LinePattern, LinePattern.Dotted));
        Assert.Equal(LinePattern.Dotted, stripCMarker.LinePattern);
    }

    [Fact]
    public void UseCOnsetFalseShowsOnlyCPeakMarkerWhenBothCOffsetsExist()
    {
        var mainPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            mainPlot, new AvaPlot(), new AvaPlot(), new TextBlock(), new TextBlock(), useCOnset: false);
        renderer.CreateGraphs();

        var segment = new BeatSegment
        {
            Samples = Enumerable.Repeat(0.5f, 80).ToArray(),
            MsPerPoint = 0.25,
            PeakValue = 0.5f,
            CPeakValid = true,
            CPeakOffsetMs = 12.0,
            COnsetValid = true,
            COnsetOffsetMs = 9.0,
        };
        renderer.RenderFrame(new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[] { segment },
                Markers = new[]
                {
                    new BeatNoiseMarker { TimeS = segment.CPeakOffsetMs / 1000.0, Kind = BeatNoiseMarkerKind.CPeak },
                },
            },
        }, new AnalysisTabRenderContext(SampleRate: 48000));

        double[] cMarkerXs = mainPlot.Plot.GetPlottables<VerticalLine>()
            .Where(line => line.IsVisible && Equals(line.LinePattern, LinePattern.Dotted))
            .Select(line => line.X)
            .ToArray();

        Assert.Equal(new[] { 12.0 }, cMarkerXs);
    }

    [Fact]
    public void UseCOnsetTrueShowsOnlyCOnsetMarkerWhenBothCOffsetsExist()
    {
        var mainPlot = new AvaPlot();
        var stripPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            mainPlot, stripPlot, new AvaPlot(), new TextBlock(), new TextBlock(), useCOnset: true);
        renderer.CreateGraphs();

        var segment = new BeatSegment
        {
            Samples = Enumerable.Repeat(0.5f, 80).ToArray(),
            MsPerPoint = 0.25,
            PeakValue = 0.5f,
            CPeakValid = true,
            CPeakOffsetMs = 12.0,
            COnsetValid = true,
            COnsetOffsetMs = 9.0,
        };
        renderer.RenderFrame(new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[] { segment },
                Markers = new[]
                {
                    new BeatNoiseMarker { TimeS = segment.CPeakOffsetMs / 1000.0, Kind = BeatNoiseMarkerKind.CPeak },
                },
            },
        }, new AnalysisTabRenderContext(SampleRate: 48000));

        double[] mainCMarkerXs = mainPlot.Plot.GetPlottables<VerticalLine>()
            .Where(line => line.IsVisible && Equals(line.LinePattern, LinePattern.Dotted))
            .Select(line => line.X)
            .ToArray();
        Assert.Equal(new[] { 9.0 }, mainCMarkerXs);

        VerticalLine stripCMarker = stripPlot.Plot.GetPlottables<VerticalLine>()
            .Single(line => line.IsVisible && Equals(line.LinePattern, LinePattern.Dotted));
        Assert.InRange(stripCMarker.X, 7.4, 7.5);
    }

    [Fact]
    public void MainScopeLegendNamesAAndCMarkerStyles()
    {
        var mainPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            mainPlot, new AvaPlot(), new AvaPlot(), new TextBlock(), new TextBlock());

        renderer.CreateGraphs();

        Scatter[] legendEntries = mainPlot.Plot.GetPlottables<Scatter>()
            .Where(scatter => !string.IsNullOrEmpty(scatter.LegendText))
            .ToArray();
        Assert.Contains(legendEntries, scatter => scatter.LegendText == "A marker" && Equals(scatter.LinePattern, LinePattern.Dashed));
        Assert.Contains(legendEntries, scatter => scatter.LegendText == "C marker" && Equals(scatter.LinePattern, LinePattern.Dotted));
    }

    [Fact]
    public void AverageModeStripSelectionShowsSelectedEnvelopePairAndTogglesBackToAverage()
    {
        var averagePlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            new AvaPlot(), new AvaPlot(), averagePlot, new TextBlock(), new TextBlock());
        renderer.CreateGraphs();

        var first = new BeatSegment
        {
            Samples = new float[] { 0.0f, 1.0f, 0.4f, 0.2f },
            MsPerPoint = 0.25,
            StartTimeS = 0.0,
        };
        var second = new BeatSegment
        {
            Samples = new float[] { 0.0f, 0.2f, 0.8f, 0.1f },
            MsPerPoint = 0.25,
            StartTimeS = 0.5,
        };
        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[] { first, second },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));
        renderer.SetViewMode(BeatNoiseScopeViewMode.AverageAndStrip);

        Assert.InRange(averagePlot.Plot.Axes.GetLimits().Top, 2.34, 2.36);

        renderer.SelectStripAtFraction(6.5 / BeatNoiseScopeLogic.StripCount);

        Assert.InRange(averagePlot.Plot.Axes.GetLimits().Top, 2.34, 2.36);
        Assert.Equal(new[] { 1.2, 2.2, 1.6, 1.4 }, LaneValues(renderer, "_lane1Y"));
        Assert.Equal(new[] { 0.0, 0.2, 0.8, 0.1 }, LaneValues(renderer, "_lane2Y"));

        renderer.SelectStripAtFraction(7.5 / BeatNoiseScopeLogic.StripCount);

        Assert.InRange(averagePlot.Plot.Axes.GetLimits().Top, 2.34, 2.36);
        Assert.Empty(LaneValues(renderer, "_lane1Y"));
        Assert.Empty(LaneValues(renderer, "_lane2Y"));
    }

    [Fact]
    public void AverageModeStripSelectionStartsPairAtTicPhaseBoundary()
    {
        var averagePlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            new AvaPlot(), new AvaPlot(), averagePlot, new TextBlock(), new TextBlock());
        renderer.CreateGraphs();

        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[]
                {
                    new BeatSegment { Samples = new float[] { 0.25f }, MsPerPoint = 0.25, IsTic = false },
                    new BeatSegment { Samples = new float[] { 1.0f }, MsPerPoint = 0.25, IsTic = true },
                    new BeatSegment { Samples = new float[] { 0.5f }, MsPerPoint = 0.25, IsTic = false },
                    new BeatSegment { Samples = new float[] { 0.75f }, MsPerPoint = 0.25, IsTic = true },
                },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));
        renderer.SetViewMode(BeatNoiseScopeViewMode.AverageAndStrip);

        renderer.SelectStripAtFraction(4.5 / BeatNoiseScopeLogic.StripCount);

        Assert.Equal(new[] { 2.2 }, LaneValues(renderer, "_lane1Y"));
        Assert.Equal(new[] { 0.5 }, LaneValues(renderer, "_lane2Y"));
    }

    private static double[] LaneValues(BeatNoiseScopeRenderer renderer, string fieldName)
    {
        var values = (List<double>)typeof(BeatNoiseScopeRenderer)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(renderer)!;
        return values.Select(value => Math.Round(value, 3)).ToArray();
    }

    [Fact]
    public void MainScopeShowsWeakSignalWhenCMarkerIsMissing()
    {
        var mainPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            mainPlot, new AvaPlot(), new AvaPlot(), new TextBlock(), new TextBlock());
        renderer.CreateGraphs();

        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[]
                {
                    new BeatSegment
                    {
                        Samples = new float[] { 0.2f, 0.5f },
                        MsPerPoint = 0.25,
                        AOffsetMs = 0.0,
                        CPeakValid = false,
                    },
                },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        Text weakSignal = mainPlot.Plot.GetPlottables<Text>()
            .Single(text => text.LabelText == "WEAK SIGNAL");
        Assert.True(weakSignal.IsVisible);
    }

    [Fact]
    public void MainScopeHidesWeakSignalWhenCMarkerIsValid()
    {
        var mainPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            mainPlot, new AvaPlot(), new AvaPlot(), new TextBlock(), new TextBlock());
        renderer.CreateGraphs();

        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[]
                {
                    new BeatSegment
                    {
                        Samples = new float[] { 0.2f, 0.5f },
                        MsPerPoint = 0.25,
                        AOffsetMs = 0.0,
                        PeakValue = 0.5f,
                        CPeakValid = true,
                        CPeakOffsetMs = 8.0,
                    },
                },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        Text weakSignal = mainPlot.Plot.GetPlottables<Text>()
            .Single(text => text.LabelText == "WEAK SIGNAL");
        Assert.False(weakSignal.IsVisible);
    }

    [Fact]
    public void MainScopeShowsWeakSignalWhenCMarkerIsTooCloseToA()
    {
        var mainPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            mainPlot, new AvaPlot(), new AvaPlot(), new TextBlock(), new TextBlock());
        renderer.CreateGraphs();

        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[]
                {
                    new BeatSegment
                    {
                        Samples = new float[] { 0.2f, 0.5f },
                        MsPerPoint = 0.25,
                        AOffsetMs = 5.0,
                        PeakValue = 0.5f,
                        CPeakValid = true,
                        CPeakOffsetMs = 6.5,
                    },
                },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        Text weakSignal = mainPlot.Plot.GetPlottables<Text>()
            .Single(text => text.LabelText == "WEAK SIGNAL");
        Assert.True(weakSignal.IsVisible);
    }

    [Fact]
    public void MainScopeShowsWeakSignalWhenPeakIsTooLow()
    {
        var mainPlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            mainPlot, new AvaPlot(), new AvaPlot(), new TextBlock(), new TextBlock());
        renderer.CreateGraphs();

        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[]
                {
                    new BeatSegment
                    {
                        Samples = new float[] { 0.02f, 0.04f },
                        MsPerPoint = 0.25,
                        AOffsetMs = 5.0,
                        PeakValue = 0.08f,
                        CPeakValid = true,
                        CPeakOffsetMs = 40.0,
                    },
                },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        Text weakSignal = mainPlot.Plot.GetPlottables<Text>()
            .Single(text => text.LabelText == "WEAK SIGNAL");
        Assert.True(weakSignal.IsVisible);
    }

    [Fact]
    public void AverageEnvelopeRendersMilestoneSnapshots()
    {
        var averagePlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            new AvaPlot(), new AvaPlot(), averagePlot, new TextBlock(), new TextBlock());
        renderer.CreateGraphs();
        renderer.SetViewMode(BeatNoiseScopeViewMode.AverageAndStrip);

        var snapshot = new BeatSegmentsSnapshot
        {
            Version = 1,
            Average = new BeatNoiseAverageSnapshot
            {
                SigmaEnabled = true,
                IntervalsPerLane = 50,
                Lane1Count = 20,
                Lane2Count = 20,
                Lane1 = new float[] { 2.0f },
                Lane2 = new float[] { 1.0f },
                MsPerPoint = 0.25,
                Milestones = new[]
                {
                    new BeatNoiseAverageMilestone
                    {
                        IntervalCount = 10,
                        Lane1 = new float[] { 1.0f },
                        Lane2 = new float[] { 0.5f },
                    },
                    new BeatNoiseAverageMilestone
                    {
                        IntervalCount = 20,
                        Lane1 = new float[] { 1.5f },
                        Lane2 = new float[] { 0.75f },
                    },
                },
            }
        };

        renderer.RenderFrame(new AnalysisFrame { BeatSegments = snapshot }, new AnalysisTabRenderContext(48000));

        Scatter[] visibleScatters = averagePlot.Plot.GetPlottables<Scatter>()
            .Where(scatter => scatter.IsVisible)
            .ToArray();
        Assert.Equal(6, visibleScatters.Length);
        Assert.Contains(visibleScatters, scatter => scatter.LegendText == "10 avg Trace 1");
        Assert.Contains(visibleScatters, scatter => scatter.LegendText == "20 avg Trace 2");

        var lane1Milestones = (List<double>[])typeof(BeatNoiseScopeRenderer)
            .GetField("_lane1MilestoneY", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(renderer)!;
        Assert.Equal(1.7, Math.Round(lane1Milestones[0][0], 3));
        Assert.Equal(1.95, Math.Round(lane1Milestones[1][0], 3));
    }
}
