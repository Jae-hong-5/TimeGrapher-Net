using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AColor = Avalonia.Media.Color;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Draws the Watch Health radar — an eight-position octagon of the vertical
/// (hanging) positions, plus a bottom strip of horizontal (flat) CH/CB gauges —
/// with the CPU vector pipeline, the same "software render, no GPU" choice as the
/// Positions 3D model, so it runs identically on Windows and the Raspberry Pi.
/// Values are not stored here: the renderer hands in a finished
/// <see cref="WatchHealthRadarModel"/> whose radii and gauge positions are already
/// normalised to 0..1. All colors are read from the App.axaml theme via
/// <see cref="PlotThemePalette"/>, so editing the theme recolors it too.
/// </summary>
internal sealed class WatchHealthRadarControl : Control
{
    private const double LabelGap = 34.0;
    private const double EdgeMargin = 20.0;
    private const double LabelFontSize = PlotThemeHelper.GraphLabelFontSize;
    // Bottom band reserved for the horizontal (flat) CH/CB gauges; the octagon
    // fills the region above it.
    private const double HorizontalStripHeight = 78.0;
    private static readonly double[] RingFractions = { 0.25, 0.5, 0.75 };

    private readonly Typeface _typeface;
    private WatchHealthRadarModel? _model;

    public WatchHealthRadarControl(string fontFamily)
    {
        _typeface = new Typeface(new FontFamily(fontFamily));
        // Re-read the palette and repaint when the theme variant flips.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public void SetModel(WatchHealthRadarModel? model)
    {
        _model = model;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 40 || bounds.Height < 40)
        {
            return;
        }

        PlotThemePalette palette = PlotThemePalette.Current;
        context.FillRectangle(Fill(palette.ScopeBg), bounds);

        // Reserve a bottom strip for the horizontal (flat) CH/CB gauges; the octagon
        // fills the region above it. Drop the strip when the control is too short.
        double stripHeight = bounds.Height >= 220.0 ? HorizontalStripHeight : 0.0;
        double radarHeight = bounds.Height - stripHeight;

        double cx = bounds.Width / 2.0;
        double cy = radarHeight / 2.0;
        double radius = Math.Max(
            12.0,
            Math.Min(bounds.Width, radarHeight) / 2.0 - LabelGap - EdgeMargin);

        var gridPen = new Pen(Fill(palette.ScopeGrid), 1.0);
        foreach (double fraction in RingFractions)
        {
            DrawPolygon(context, cx, cy, radius * fraction, null, gridPen);
        }

        DrawPolygon(context, cx, cy, radius, null, new Pen(Fill(palette.ChromeBorder), 1.5));

        for (int i = 0; i < AxisCount; i++)
        {
            (double x, double y) = Vertex(cx, cy, radius, i, 1.0);
            context.DrawLine(gridPen, new Point(cx, cy), new Point(x, y));
        }

        WatchHealthRadarModel? model = _model;
        if (model is null)
        {
            return;
        }

        DrawBand(context, model, palette, cx, cy, radius);
        DrawMeasuredPolygon(context, model, palette, cx, cy, radius);
        DrawLabels(context, model, palette, cx, cy, radius);
        DrawScaleTicks(context, model, palette, cx, cy, radius);

        if (stripHeight > 0.0)
        {
            DrawHorizontalStrip(
                context, model, palette,
                new Rect(bounds.X, radarHeight, bounds.Width, stripHeight));
        }

        if (model.MeasuredCount == 0)
        {
            FormattedText message = Text(
                "Measure positions to build the health radar", LabelFontSize, Fill(palette.TextPrimary));
            context.DrawText(message, new Point(cx - message.Width / 2.0, cy + radius * 0.45));
        }
    }

    // Horizontal (flat) CH/CB gauges: the Positions-lane visual language
    // (RateRangeLaneControl) — a track with the shared accept band shaded, a
    // min–max range bar, and the mean dot (red when out of band) — for the selected
    // metric, mapped onto the same 0..1 scale the octagon uses.
    private void DrawHorizontalStrip(
        DrawingContext context, WatchHealthRadarModel model, PlotThemePalette palette, Rect strip)
    {
        double left = strip.X + EdgeMargin;
        double right = strip.Right - EdgeMargin;
        if (right - left < 40.0)
        {
            return;
        }

        context.DrawLine(
            new Pen(Fill(palette.ChromeBorder), 1.0),
            new Point(left, strip.Y + 6.0), new Point(right, strip.Y + 6.0));
        FormattedText header = Text("HORIZONTAL (FLAT)", LabelFontSize, Fill(palette.TraceGhost));
        context.DrawText(header, new Point(left, strip.Y + 12.0));

        const double labelWidth = 40.0;
        const double valueWidth = 56.0;
        const double rowHeight = 24.0;
        double trackX = left + labelWidth;
        double trackWidth = Math.Max(20.0, right - valueWidth - trackX);
        double firstRowY = strip.Y + 40.0;

        for (int i = 0; i < model.Horizontal.Count; i++)
        {
            double rowCy = firstRowY + i * rowHeight;
            DrawHorizontalGauge(
                context, model, palette, model.Horizontal[i], left, trackX, trackWidth, rowCy);
        }
    }

    private void DrawHorizontalGauge(
        DrawingContext context, WatchHealthRadarModel model, PlotThemePalette palette,
        HealthHorizontalRow row, double labelX, double trackX, double trackWidth, double cy)
    {
        const double laneHeight = 8.0;

        FormattedText label = Text(row.Label, LabelFontSize, Fill(palette.TextPrimary));
        context.DrawText(label, new Point(labelX, cy - label.Height / 2.0));

        context.FillRectangle(
            Fill(palette.ChromeBorder), new Rect(trackX, cy - laneHeight / 2.0, trackWidth, laneHeight));

        if (model.HasBand && model.BandOuterFraction > model.BandInnerFraction)
        {
            double bandX = trackX + model.BandInnerFraction * trackWidth;
            double bandW = (model.BandOuterFraction - model.BandInnerFraction) * trackWidth;
            context.FillRectangle(
                Fill(WithAlpha(palette.VarioAcceptBand, 0x33)),
                new Rect(bandX, cy - laneHeight / 2.0 - 2.0, Math.Max(1.0, bandW), laneHeight + 4.0));
        }

        double valueX = trackX + trackWidth + 10.0;
        if (!row.HasValue)
        {
            FormattedText dash = Text("—", LabelFontSize, Fill(palette.TraceGhost));
            context.DrawText(dash, new Point(valueX, cy - dash.Height / 2.0));
            return;
        }

        double lo = trackX + row.MinFraction * trackWidth;
        double hi = trackX + row.MaxFraction * trackWidth;
        context.FillRectangle(
            Fill(palette.VarioMinMax),
            new Rect(lo, cy - laneHeight / 2.0, Math.Max(2.0, hi - lo), laneHeight));

        double meanX = trackX + row.MeanFraction * trackWidth;
        IImmutableBrush meanBrush = Fill(row.OutOfBand ? palette.VarioBad : palette.TextPrimary);
        context.DrawEllipse(meanBrush, new Pen(Fill(0xFFFFFFFFu), 1.5), new Point(meanX, cy), 5.0, 5.0);

        FormattedText value = Text(row.ValueText, LabelFontSize, Fill(palette.TextPrimary));
        context.DrawText(value, new Point(valueX, cy - value.Height / 2.0));
    }

    private void DrawBand(
        DrawingContext context, WatchHealthRadarModel model, PlotThemePalette palette,
        double cx, double cy, double radius)
    {
        if (!model.HasBand || model.BandOuterFraction <= model.BandInnerFraction)
        {
            return;
        }

        var ring = new StreamGeometry();
        using (StreamGeometryContext ctx = ring.Open())
        {
            ctx.SetFillRule(FillRule.EvenOdd);
            AppendPolygon(ctx, cx, cy, radius * model.BandOuterFraction);
            if (model.BandInnerFraction > 0.0)
            {
                AppendPolygon(ctx, cx, cy, radius * model.BandInnerFraction);
            }
        }

        context.DrawGeometry(Fill(WithAlpha(palette.VarioAcceptBand, 0x24)), null, ring);

        var edge = new Pen(Fill(palette.VarioAcceptBandEdge), 1.0) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
        DrawPolygon(context, cx, cy, radius * model.BandOuterFraction, null, edge);
        if (model.BandInnerFraction > 0.01)
        {
            DrawPolygon(context, cx, cy, radius * model.BandInnerFraction, null, edge);
        }
    }

    private void DrawMeasuredPolygon(
        DrawingContext context, WatchHealthRadarModel model, PlotThemePalette palette,
        double cx, double cy, double radius)
    {
        uint measured = palette.VarioBad;
        var points = new List<Point>(AxisCount);
        for (int i = 0; i < model.Axes.Count; i++)
        {
            if (model.Axes[i].HasValue)
            {
                (double x, double y) = Vertex(cx, cy, radius, i, model.Axes[i].RadiusFraction);
                points.Add(new Point(x, y));
            }
        }

        var stroke = new Pen(Fill(measured), 2.5);
        if (points.Count >= 3)
        {
            var polygon = new StreamGeometry();
            using (StreamGeometryContext ctx = polygon.Open())
            {
                ctx.BeginFigure(points[0], true);
                for (int i = 1; i < points.Count; i++)
                {
                    ctx.LineTo(points[i]);
                }

                ctx.EndFigure(true);
            }

            context.DrawGeometry(Fill(WithAlpha(measured, 0x28)), stroke, polygon);
        }
        else if (points.Count == 2)
        {
            context.DrawLine(stroke, points[0], points[1]);
        }

        IImmutableBrush dot = Fill(measured);
        foreach (Point point in points)
        {
            context.DrawEllipse(dot, null, point, 4.0, 4.0);
        }
    }

    private void DrawLabels(
        DrawingContext context, WatchHealthRadarModel model, PlotThemePalette palette,
        double cx, double cy, double radius)
    {
        IImmutableBrush labelBrush = Fill(palette.TextPrimary);
        IImmutableBrush valueBrush = Fill(palette.VarioBad);
        IImmutableBrush mutedBrush = Fill(palette.TraceGhost);

        for (int i = 0; i < model.Axes.Count; i++)
        {
            RadarAxis axis = model.Axes[i];
            (double lx, double ly) = Vertex(cx, cy, radius + LabelGap, i, 1.0);

            FormattedText label = Text(axis.Label, LabelFontSize, labelBrush);
            FormattedText value = Text(axis.ValueText, LabelFontSize, axis.HasValue ? valueBrush : mutedBrush);
            context.DrawText(label, new Point(lx - label.Width / 2.0, ly - 18.0));
            context.DrawText(value, new Point(lx - value.Width / 2.0, ly - 1.0));
        }
    }

    private void DrawScaleTicks(
        DrawingContext context, WatchHealthRadarModel model, PlotThemePalette palette,
        double cx, double cy, double radius)
    {
        IImmutableBrush tickBrush = Fill(palette.TraceGhost);
        for (int t = 0; t < model.ScaleTicks.Count; t++)
        {
            double fraction = (t + 1) / 3.0;
            (double tx, double ty) = Vertex(cx, cy, radius * fraction, 0, 1.0);
            FormattedText tick = Text(model.ScaleTicks[t], LabelFontSize, tickBrush);
            context.DrawText(tick, new Point(tx + 4.0, ty - tick.Height / 2.0));
        }
    }

    private const int AxisCount = 8;

    private static (double X, double Y) Vertex(double cx, double cy, double radius, int index, double fraction)
    {
        double theta = index * Math.PI / 4.0; // 45° steps, clockwise from the top
        double rr = radius * fraction;
        return (cx + rr * Math.Sin(theta), cy - rr * Math.Cos(theta));
    }

    private static void AppendPolygon(StreamGeometryContext ctx, double cx, double cy, double radius)
    {
        (double x0, double y0) = Vertex(cx, cy, radius, 0, 1.0);
        ctx.BeginFigure(new Point(x0, y0), true);
        for (int i = 1; i < AxisCount; i++)
        {
            (double x, double y) = Vertex(cx, cy, radius, i, 1.0);
            ctx.LineTo(new Point(x, y));
        }

        ctx.EndFigure(true);
    }

    private static void DrawPolygon(DrawingContext context, double cx, double cy, double radius, IImmutableBrush? fill, IPen? pen)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            AppendPolygon(ctx, cx, cy, radius);
        }

        context.DrawGeometry(fill, pen, geometry);
    }

    private FormattedText Text(string text, double size, IBrush brush) => new(
        text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, size, brush);

    private static IImmutableBrush Fill(uint argb) => new ImmutableSolidColorBrush(AColor.FromUInt32(argb));

    private static uint WithAlpha(uint argb, byte alpha) => (argb & 0x00FFFFFFu) | ((uint)alpha << 24);
}
