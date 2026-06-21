using Avalonia;
using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class WatchPositionDiagramTests
{
    [Theory]
    [InlineData(WatchPosition.CH, true, 0.0, "CH", "Dial up")]
    [InlineData(WatchPosition.CB, true, 0.0, "CB", "Dial down")]
    [InlineData(WatchPosition.P12H, false, 0.0, "12H", "12 o'clock up")]
    [InlineData(WatchPosition.P3H, false, 270.0, "3H", "3 o'clock up")]
    [InlineData(WatchPosition.P9H, false, 90.0, "9H", "9 o'clock up")]
    [InlineData(WatchPosition.P6H, false, 180.0, "6H", "6 o'clock up")]
    [InlineData(WatchPosition.P3H45, false, 315.0, "1:30H", "1:30 up")]
    [InlineData(WatchPosition.P6H45, false, 225.0, "4:30H", "4:30 up")]
    [InlineData(WatchPosition.P9H45, false, 135.0, "7:30H", "7:30 up")]
    [InlineData(WatchPosition.P12H45, false, 45.0, "10:30H", "10:30 up")]
    public void PoseMatchesReferenceFigureTerminology(
        WatchPosition position,
        bool isFlat,
        double crownAngleDegrees,
        string primaryLabel,
        string secondaryLabel)
    {
        WatchPositionDiagramPose pose = WatchPositionDiagram.Pose(position);

        Assert.Equal(isFlat, pose.IsFlat);
        Assert.Equal(crownAngleDegrees, pose.CrownAngleDegrees);
        Assert.Equal(primaryLabel, pose.PrimaryLabel);
        Assert.Equal(secondaryLabel, pose.SecondaryLabel);
    }

    [Theory]
    [InlineData(WatchPosition.P9H)]
    [InlineData(WatchPosition.P3H45)]
    [InlineData(WatchPosition.P6H45)]
    [InlineData(WatchPosition.P9H45)]
    public void LayoutKeepsDownwardCrownAboveLabels(WatchPosition position)
    {
        WatchPositionDiagramPose pose = WatchPositionDiagram.Pose(position);
        WatchPositionDiagramLayout layout = WatchPositionDiagram.Layout(new Size(176, 140), pose);

        double crownBottom = layout.Center.Y + layout.Side * 0.62;
        double primaryLabelTop = layout.PrimaryLabelCenter.Y - layout.PrimaryLabelFontSize * 0.5;

        Assert.True(crownBottom <= primaryLabelTop - 8.0);
    }

    [Fact]
    public void LayoutUsesLabelSpaceForImageWhenLabelsAreHidden()
    {
        WatchPositionDiagramPose pose = WatchPositionDiagram.Pose(WatchPosition.P12H);

        WatchPositionDiagramLayout labeled = WatchPositionDiagram.Layout(new Size(176, 140), pose);
        WatchPositionDiagramLayout unlabeled = WatchPositionDiagram.Layout(new Size(176, 140), pose, showLabels: false);

        Assert.True(unlabeled.Side > labeled.Side);
        Assert.True(unlabeled.ImageBottom > labeled.ImageBottom);
    }

    [Theory]
    [InlineData(WatchPosition.P12H, 0.0, 0, -1)]
    [InlineData(WatchPosition.P3H, 270.0, -1, 0)]
    [InlineData(WatchPosition.P9H, 90.0, 1, 0)]
    [InlineData(WatchPosition.P6H, 180.0, 0, 1)]
    public void DialMarksRotateWithTheWatchBody(
        WatchPosition position,
        double rotationDegrees,
        int expectedXDirection,
        int expectedYDirection)
    {
        WatchPositionDiagramPose pose = WatchPositionDiagram.Pose(position);
        Assert.Equal(rotationDegrees, pose.CrownAngleDegrees);

        var center = new Point(100, 100);
        Point twelve = WatchPositionDiagram.DialPoint(center, 40, -90.0, pose.CrownAngleDegrees);

        AssertDirection(twelve.X - center.X, expectedXDirection);
        AssertDirection(twelve.Y - center.Y, expectedYDirection);
    }

    [Theory]
    [InlineData(WatchPosition.P12H, 0.0)]
    [InlineData(WatchPosition.P3H, 270.0)]
    [InlineData(WatchPosition.P9H, 90.0)]
    [InlineData(WatchPosition.P6H, 180.0)]
    [InlineData(WatchPosition.P3H45, 315.0)]
    public void HourNumberGlyphsUseTheSameRotationAsTheWatchBody(
        WatchPosition position,
        double rotationDegrees)
    {
        WatchPositionDiagramPose pose = WatchPositionDiagram.Pose(position);

        Assert.Equal(rotationDegrees, WatchPositionDiagram.HourTextRotationDegrees(pose));
    }

    private static void AssertDirection(double delta, int expectedDirection)
    {
        if (expectedDirection == 0)
        {
            Assert.InRange(Math.Abs(delta), 0.0, 0.000001);
            return;
        }

        Assert.Equal(expectedDirection, Math.Sign(delta));
    }
}
