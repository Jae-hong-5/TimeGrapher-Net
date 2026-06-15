using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal readonly record struct WatchPositionDiagramPose(
    bool IsFlat,
    double CrownAngleDegrees,
    string PrimaryLabel,
    string SecondaryLabel);

internal readonly record struct WatchPositionDiagramLayout(
    double Side,
    Point Center,
    Point PrimaryLabelCenter,
    Point SecondaryLabelCenter,
    double PrimaryLabelFontSize,
    double SecondaryLabelFontSize,
    double ImageBottom);

internal sealed class WatchPositionDiagram : Control
{
    private const double ImageTopPadding = 6.0;
    private const double ImageLabelGap = 8.0;
    private const double PrimaryLabelFontSize = 20.0;
    private const double SecondaryLabelFontSize = 13.0;
    private const double SecondaryLabelOpacity = 0.9;

    public static readonly StyledProperty<WatchPosition> PositionProperty =
        AvaloniaProperty.Register<WatchPositionDiagram, WatchPosition>(
            nameof(Position),
            WatchPosition.CH);

    public WatchPosition Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    static WatchPositionDiagram()
    {
        AffectsRender<WatchPositionDiagram>(PositionProperty);
    }

    public WatchPositionDiagram()
    {
        MinWidth = 150;
        MinHeight = 120;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        WatchPositionDiagramPose pose = Pose(Position);
        var textBrush = ResourceBrush("TextPrimaryBrush", Brushes.Black);
        var borderBrush = ResourceBrush("ChromeBorderBrush", Brushes.Gray);
        var accentBrush = ResourceBrush("ChromeAccentBrush", Brushes.Firebrick);
        var panelBrush = ResourceBrush("PanelBgBrush", Brushes.WhiteSmoke);

        WatchPositionDiagramLayout layout = Layout(Bounds.Size, pose);

        if (pose.IsFlat)
        {
            DrawFlatWatch(context, layout.Center, layout.Side, pose, panelBrush, borderBrush, accentBrush, textBrush);
        }
        else
        {
            DrawDialWatch(
                context,
                layout.Center,
                layout.Side,
                pose.CrownAngleDegrees,
                panelBrush,
                borderBrush,
                accentBrush,
                textBrush);
        }

        DrawCenteredText(context, pose.PrimaryLabel, layout.PrimaryLabelFontSize, layout.PrimaryLabelCenter, accentBrush);
        DrawCenteredText(
            context,
            pose.SecondaryLabel,
            layout.SecondaryLabelFontSize,
            layout.SecondaryLabelCenter,
            textBrush,
            SecondaryLabelOpacity);
    }

    internal static WatchPositionDiagramPose Pose(WatchPosition position) => position switch
    {
        WatchPosition.CH => new(true, 0.0, "DU", "Dial up"),
        WatchPosition.CB => new(true, 0.0, "DD", "Dial down"),
        WatchPosition.P6H => new(false, 180.0, "CL", "Crown left"),
        WatchPosition.P9H => new(false, 90.0, "CD", "Crown down"),
        WatchPosition.P3H => new(false, 270.0, "CU", "Crown up"),
        WatchPosition.P12H => new(false, 0.0, "CR", "Crown right"),
        WatchPosition.P6H45 => new(false, 225.0, "CU(L)", "Crown up-left"),
        WatchPosition.P9H45 => new(false, 135.0, "CD(L)", "Crown down-left"),
        WatchPosition.P3H45 => new(false, 315.0, "CU(R)", "Crown up-right"),
        WatchPosition.P12H45 => new(false, 45.0, "CD(R)", "Crown down-right"),
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
    };

    internal static WatchPositionDiagramLayout Layout(Size size, WatchPositionDiagramPose pose)
    {
        double width = Math.Max(1.0, size.Width);
        double height = Math.Max(1.0, size.Height);
        double secondaryY = Math.Max(SecondaryLabelFontSize * 0.5, height - 12.0);
        double primaryY = Math.Max(PrimaryLabelFontSize * 0.5, secondaryY - 21.0);
        double primaryLabelTop = primaryY - PrimaryLabelFontSize * 0.5;
        double imageBottom = Math.Max(ImageTopPadding + 52.0, primaryLabelTop - ImageLabelGap);
        double imageHeight = Math.Max(52.0, imageBottom - ImageTopPadding);

        double sideByWidth = pose.IsFlat ? width / 1.42 : width * 0.54;
        double sideByHeight = pose.IsFlat ? imageHeight / 0.36 : imageHeight / 1.24;
        double side = Math.Max(52.0, Math.Min(sideByWidth, sideByHeight));
        Point center = new(width * 0.5, ImageTopPadding + imageHeight * 0.5);

        return new WatchPositionDiagramLayout(
            side,
            center,
            new Point(width * 0.5, primaryY),
            new Point(width * 0.5, secondaryY),
            PrimaryLabelFontSize,
            SecondaryLabelFontSize,
            imageBottom);
    }

    private IBrush ResourceBrush(string key, IBrush fallback)
    {
        return Application.Current?.TryGetResource(key, null, out object? value) == true && value is IBrush brush
            ? brush
            : fallback;
    }

    private static void DrawDialWatch(
        DrawingContext context,
        Point center,
        double side,
        double crownAngleDegrees,
        IBrush faceBrush,
        IBrush borderBrush,
        IBrush accentBrush,
        IBrush textBrush)
    {
        double radius = side * 0.5;
        var outline = new Pen(borderBrush, 3);
        var inner = new Pen(borderBrush, 1.3);
        context.DrawEllipse(faceBrush, outline, center, radius, radius);
        context.DrawEllipse(null, inner, center, radius * 0.82, radius * 0.82);

        DrawCrown(context, center, radius, crownAngleDegrees, accentBrush, borderBrush);

        DrawHourText(context, "12", DialPoint(center, radius * 0.55, -90.0, crownAngleDegrees), 13, textBrush, crownAngleDegrees);
        DrawHourText(context, "3", DialPoint(center, radius * 0.55, 0.0, crownAngleDegrees), 13, textBrush, crownAngleDegrees);
        DrawHourText(context, "6", DialPoint(center, radius * 0.55, 90.0, crownAngleDegrees), 13, textBrush, crownAngleDegrees);
        DrawHourText(context, "9", DialPoint(center, radius * 0.55, 180.0, crownAngleDegrees), 13, textBrush, crownAngleDegrees);

        for (int i = 0; i < 12; i++)
        {
            double angle = i * 30.0 - 90.0 + crownAngleDegrees;
            Point outer = center + Polar(radius * 0.74, angle);
            Point innerTick = center + Polar(radius * (i % 3 == 0 ? 0.63 : 0.68), angle);
            context.DrawLine(new Pen(textBrush, i % 3 == 0 ? 2.0 : 1.3), innerTick, outer);
        }

        context.DrawEllipse(accentBrush, null, center, 3, 3);
    }

    private static void DrawFlatWatch(
        DrawingContext context,
        Point center,
        double side,
        WatchPositionDiagramPose pose,
        IBrush bodyBrush,
        IBrush borderBrush,
        IBrush accentBrush,
        IBrush textBrush)
    {
        double width = side * 1.26;
        double height = side * 0.22;
        double y = center.Y - height * 0.5;
        bool dialDown = pose.PrimaryLabel == "DD";
        var body = new Rect(center.X - width * 0.5, y, width, height);
        var bodyGeometry = FlatBodyGeometry(body, dialDown);
        context.DrawGeometry(bodyBrush, new Pen(borderBrush, 2), bodyGeometry);

        double cueY = dialDown ? body.Bottom - height * 0.32 : body.Top + height * 0.32;
        context.DrawLine(new Pen(textBrush, 1.1), new Point(body.Left + 12, cueY), new Point(body.Right - 12, cueY));
        context.DrawLine(new Pen(borderBrush, 1), new Point(body.Left + 12, body.Bottom + 3), new Point(body.Right - 12, body.Bottom + 3));

        double crownWidth = side * 0.13;
        double crownHeight = height * 0.72;
        var crown = new Rect(body.Right - 1, body.Top + height * 0.14, crownWidth, crownHeight);
        context.DrawRectangle(accentBrush, new Pen(borderBrush, 1.2), crown);

        if (dialDown)
        {
            context.DrawLine(new Pen(accentBrush, 2), new Point(body.Left + 10, body.Bottom + 7), new Point(body.Right - 10, body.Bottom + 7));
        }
        else
        {
            context.DrawLine(new Pen(accentBrush, 2), new Point(body.Left + 10, body.Top - 7), new Point(body.Right - 10, body.Top - 7));
        }
    }

    private static void DrawCrown(
        DrawingContext context,
        Point center,
        double radius,
        double angleDegrees,
        IBrush accentBrush,
        IBrush borderBrush)
    {
        double radians = Math.PI * angleDegrees / 180.0;
        double ux = Math.Cos(radians);
        double uy = Math.Sin(radians);
        double tx = -uy;
        double ty = ux;

        double depth = radius * 0.24;
        double width = radius * 0.28;
        Point baseCenter = Offset(center, ux * radius * 0.93, uy * radius * 0.93);
        Point farCenter = Offset(center, ux * (radius + depth), uy * (radius + depth));

        Point baseLeft = Offset(baseCenter, -tx * width * 0.5, -ty * width * 0.5);
        Point baseRight = Offset(baseCenter, tx * width * 0.5, ty * width * 0.5);
        Point farRight = Offset(farCenter, tx * width * 0.5, ty * width * 0.5);
        Point farLeft = Offset(farCenter, -tx * width * 0.5, -ty * width * 0.5);

        var geometry = new StreamGeometry();
        using (StreamGeometryContext stream = geometry.Open())
        {
            stream.BeginFigure(baseLeft, isFilled: true);
            stream.LineTo(baseRight);
            stream.LineTo(farRight);
            stream.LineTo(farLeft);
            stream.EndFigure(isClosed: true);
        }

        var border = new Pen(borderBrush, 1.2);
        context.DrawGeometry(accentBrush, border, geometry);

        Point ridgeStart = Offset(baseCenter, tx * width * 0.28, ty * width * 0.28);
        Point ridgeEnd = Offset(farCenter, tx * width * 0.28, ty * width * 0.28);
        context.DrawLine(new Pen(borderBrush, 0.8), ridgeStart, ridgeEnd);
    }

    private static Point Polar(double radius, double angleDegrees)
    {
        double radians = Math.PI * angleDegrees / 180.0;
        return new Point(Math.Cos(radians) * radius, Math.Sin(radians) * radius);
    }

    internal static Point DialPoint(Point center, double radius, double baseAngleDegrees, double rotationDegrees)
    {
        return center + Polar(radius, baseAngleDegrees + rotationDegrees);
    }

    private static Point Offset(Point origin, double x, double y) => new(origin.X + x, origin.Y + y);

    private static StreamGeometry FlatBodyGeometry(Rect body, bool dialDown)
    {
        double inset = body.Height * 0.22;
        double curve = dialDown ? -body.Height * 0.18 : body.Height * 0.18;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext stream = geometry.Open())
        {
            stream.BeginFigure(new Point(body.Left, body.Top + inset), isFilled: true);
            stream.CubicBezierTo(
                new Point(body.Left + body.Width * 0.28, body.Top + inset + curve),
                new Point(body.Right - body.Width * 0.28, body.Top + inset + curve),
                new Point(body.Right, body.Top + inset));
            stream.LineTo(new Point(body.Right, body.Bottom - inset));
            stream.CubicBezierTo(
                new Point(body.Right - body.Width * 0.28, body.Bottom - inset + curve),
                new Point(body.Left + body.Width * 0.28, body.Bottom - inset + curve),
                new Point(body.Left, body.Bottom - inset));
            stream.EndFigure(isClosed: true);
        }

        return geometry;
    }

    internal static double HourTextRotationDegrees(WatchPositionDiagramPose pose) => pose.CrownAngleDegrees;

    private static void DrawHourText(
        DrawingContext context,
        string text,
        Point center,
        double size,
        IBrush brush,
        double rotationDegrees)
    {
        DrawCenteredText(context, text, size, center, brush, rotationDegrees: rotationDegrees);
    }

    private static void DrawCenteredText(
        DrawingContext context,
        string text,
        double size,
        Point center,
        IBrush brush,
        double opacity = 1.0,
        double rotationDegrees = 0.0)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            size,
            brush);
        IDisposable? transformState = null;
        if (Math.Abs(rotationDegrees) > 0.000001)
        {
            double radians = Math.PI * rotationDegrees / 180.0;
            Matrix matrix =
                Matrix.CreateTranslation(-center.X, -center.Y) *
                Matrix.CreateRotation(radians) *
                Matrix.CreateTranslation(center.X, center.Y);
            transformState = context.PushTransform(matrix);
        }

        using (transformState)
        using (context.PushOpacity(opacity))
        {
            context.DrawText(formatted, new Point(center.X - formatted.Width * 0.5, center.Y - formatted.Height * 0.5));
        }
    }
}
