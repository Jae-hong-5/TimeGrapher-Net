using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// A compact per-position "rate range" lane: the measured min–max range (blue bar)
/// and the mean (a dot, red when the mean falls outside the accept band) over a
/// shared rate axis with the accept band shaded. Drawn directly (the radar-control
/// pattern) from <see cref="PlotThemePalette"/> so it tracks the App.axaml theme;
/// all values come from the existing <see cref="Core.Shared.PositionSummary"/>
/// stats, so no new analysis data is needed.
/// </summary>
internal sealed class RateRangeLaneControl : Control
{
    private const double AxisPaddingFraction = 0.08;
    private const double MinAxisSpan = 1.0;
    private const byte AcceptBandFillAlpha = 42;

    private readonly bool _hasValue;
    private readonly double _min;
    private readonly double _mean;
    private readonly double _max;

    public RateRangeLaneControl(bool hasValue, double min, double mean, double max)
    {
        _hasValue = hasValue;
        _min = min;
        _mean = mean;
        _max = max;
        Height = 26;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        Margin = new Thickness(8, 2, 8, 2);
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 2.0)
        {
            return;
        }

        PlotThemePalette palette = PlotThemePalette.Current;
        const double laneH = 8.0;
        double cy = h / 2.0;
        (double axisMin, double axisMax) = AxisRange(_hasValue, _min, _mean, _max);

        // Track.
        context.DrawRectangle(Solid(TrackFillArgb(palette)), null, new Rect(0, cy - laneH / 2, w, laneH));

        // Accept band (shared single source).
        double bandLo = X(VarioGaugePolicy.RateAcceptMinSPerDay, w, axisMin, axisMax);
        double bandHi = X(VarioGaugePolicy.RateAcceptMaxSPerDay, w, axisMin, axisMax);
        context.DrawRectangle(
            Solid(AcceptBandFillArgb(palette)), null,
            new Rect(bandLo, cy - laneH / 2 - 2, Math.Max(1.0, bandHi - bandLo), laneH + 4));

        // Zero line.
        double zero = X(0.0, w, axisMin, axisMax);
        context.DrawLine(
            new Pen(Solid(0xFFB9BEC1), 1),
            new Point(zero, cy - laneH / 2 - 3),
            new Point(zero, cy + laneH / 2 + 3));

        if (!_hasValue)
        {
            return;
        }

        // Measured min–max range.
        double lo = X(_min, w, axisMin, axisMax);
        double hi = X(_max, w, axisMin, axisMax);
        context.DrawRectangle(
            Solid(palette.VarioMinMax), null,
            new Rect(lo, cy - laneH / 2, Math.Max(2.0, hi - lo), laneH));

        // Mean dot — red only when the mean is out of the accept band.
        bool outOfBand = _mean < VarioGaugePolicy.RateAcceptMinSPerDay ||
            _mean > VarioGaugePolicy.RateAcceptMaxSPerDay;
        IBrush dot = outOfBand ? Solid(palette.VarioBad) : Solid(palette.TextPrimary);
        double mx = X(_mean, w, axisMin, axisMax);
        context.DrawEllipse(dot, new Pen(Brushes.White, 2), new Point(mx, cy), 6, 6);
    }

    internal static (double Min, double Max) AxisRange(bool hasValue, double min, double mean, double max)
    {
        double axisMin = Math.Min(0.0, VarioGaugePolicy.RateAcceptMinSPerDay);
        double axisMax = Math.Max(0.0, VarioGaugePolicy.RateAcceptMaxSPerDay);

        if (hasValue)
        {
            axisMin = Math.Min(axisMin, Math.Min(Math.Min(min, mean), max));
            axisMax = Math.Max(axisMax, Math.Max(Math.Max(min, mean), max));
        }

        double span = Math.Max(axisMax - axisMin, MinAxisSpan);
        double padding = span * AxisPaddingFraction;
        return (axisMin - padding, axisMax + padding);
    }

    private static double X(double value, double width, double axisMin, double axisMax) =>
        (Math.Clamp(value, axisMin, axisMax) - axisMin) / (axisMax - axisMin) * width;

    internal static uint AcceptBandFillArgb(PlotThemePalette palette) =>
        WithAlpha(palette.TraceTick, AcceptBandFillAlpha);

    internal static uint TrackFillArgb(PlotThemePalette palette) => palette.ChromeBorder;

    private static IBrush Solid(uint argb) => new SolidColorBrush(Color.FromUInt32(argb));

    private static uint WithAlpha(uint argb, byte alpha) => (argb & 0x00FFFFFF) | ((uint)alpha << 24);
}
