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
/// Draws the Watch Health radar (six-position hexagon) with the CPU vector
/// pipeline, the same "software render, no GPU" choice as the Positions 3D model,
/// so it runs identically on Windows and the Raspberry Pi. Values are not stored
/// here: the renderer hands in a finished <see cref="WatchHealthRadarModel"/> whose
/// radii are already normalised to 0..1. All colors are read from the App.axaml
/// theme via <see cref="PlotThemePalette"/>, so editing the theme recolors it too.
/// </summary>
internal sealed class WatchHealthRadarControl : Control
{
    private const double LabelGap = 26.0;
    private const double EdgeMargin = 20.0;
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

        double cx = bounds.Width / 2.0;
        double cy = bounds.Height / 2.0;
        double radius = Math.Max(
            12.0,
            Math.Min(bounds.Width, bounds.Height) / 2.0 - LabelGap - EdgeMargin);

        var gridPen = new Pen(Fill(palette.ScopeGrid), 1.0);
        foreach (double fraction in RingFractions)
        {
            DrawHexagon(context, cx, cy, radius * fraction, null, gridPen);
        }

        DrawHexagon(context, cx, cy, radius, null, new Pen(Fill(palette.ChromeBorder), 1.5));

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

        if (model.MeasuredCount == 0)
        {
            FormattedText message = Text(
                "Measure positions to build the health radar", 13.0, Fill(palette.TextPrimary));
            context.DrawText(message, new Point(cx - message.Width / 2.0, cy + radius * 0.45));
        }
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
            AppendHexagon(ctx, cx, cy, radius * model.BandOuterFraction);
            if (model.BandInnerFraction > 0.0)
            {
                AppendHexagon(ctx, cx, cy, radius * model.BandInnerFraction);
            }
        }

        context.DrawGeometry(Fill(WithAlpha(palette.VarioAcceptBand, 0x24)), null, ring);

        var edge = new Pen(Fill(palette.VarioAcceptBandEdge), 1.0) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
        DrawHexagon(context, cx, cy, radius * model.BandOuterFraction, null, edge);
        if (model.BandInnerFraction > 0.01)
        {
            DrawHexagon(context, cx, cy, radius * model.BandInnerFraction, null, edge);
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

            FormattedText label = Text(axis.Label, 12.0, labelBrush);
            FormattedText value = Text(axis.ValueText, 12.0, axis.HasValue ? valueBrush : mutedBrush);
            context.DrawText(label, new Point(lx - label.Width / 2.0, ly - 15.0));
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
            FormattedText tick = Text(model.ScaleTicks[t], 9.0, tickBrush);
            context.DrawText(tick, new Point(tx + 4.0, ty - tick.Height / 2.0));
        }
    }

    private const int AxisCount = 6;

    private static (double X, double Y) Vertex(double cx, double cy, double radius, int index, double fraction)
    {
        double theta = index * Math.PI / 3.0; // 60° steps, clockwise from the top
        double rr = radius * fraction;
        return (cx + rr * Math.Sin(theta), cy - rr * Math.Cos(theta));
    }

    private static void AppendHexagon(StreamGeometryContext ctx, double cx, double cy, double radius)
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

    private static void DrawHexagon(DrawingContext context, double cx, double cy, double radius, IImmutableBrush? fill, IPen? pen)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            AppendHexagon(ctx, cx, cy, radius);
        }

        context.DrawGeometry(fill, pen, geometry);
    }

    private FormattedText Text(string text, double size, IBrush brush) => new(
        text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, size, brush);

    private static IImmutableBrush Fill(uint argb) => new ImmutableSolidColorBrush(AColor.FromUInt32(argb));

    private static uint WithAlpha(uint argb, byte alpha) => (argb & 0x00FFFFFFu) | ((uint)alpha << 24);
}
