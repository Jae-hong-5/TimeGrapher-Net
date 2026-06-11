using TimeGrapher.Core.Metrics;
using Xunit;

namespace TimeGrapher.Core.Tests;

public sealed class RollingAverageTests
{
    [Fact]
    public void Add_ReturnsRunningAverageWhileFilling()
    {
        var avg = new RollingAverage(3);

        Assert.Equal(2.0, avg.Add(2.0), 10);
        Assert.Equal(3.0, avg.Add(4.0), 10); // (2+4)/2
        Assert.Equal(4.0, avg.Add(6.0), 10); // (2+4+6)/3
    }

    [Fact]
    public void Add_EvictsOldestBeyondWindow()
    {
        var avg = new RollingAverage(2);
        avg.Add(2.0);
        avg.Add(4.0);

        Assert.Equal(5.0, avg.Add(6.0), 10); // window {4,6}
        Assert.Equal(2, avg.CurrentSize());
    }

    [Fact]
    public void Resize_DropsOldestToFitNewWindow()
    {
        var avg = new RollingAverage(4);
        foreach (double v in new[] { 1.0, 2.0, 3.0, 4.0 })
        {
            avg.Add(v);
        }

        avg.Resize(2);

        Assert.Equal(2, avg.CurrentSize());
        Assert.Equal(3.5, avg.GetAverage(), 10); // {3,4}
    }

    [Fact]
    public void Resize_ShrinkStaysCorrectAfterHeadWraps()
    {
        var avg = new RollingAverage(4);
        foreach (double v in new[] { 1.0, 2.0, 3.0, 4.0 })
        {
            avg.Add(v);
        }

        avg.Resize(2); // window {3,4}

        Assert.Equal(4.5, avg.Add(5.0), 10); // {4,5}
        Assert.Equal(5.5, avg.Add(6.0), 10); // {5,6}
        Assert.Equal(6.5, avg.Add(7.0), 10); // {6,7}; pre-fix the stale slot made this 8.5
        Assert.Equal(7.5, avg.Add(8.0), 10); // {7,8}
        Assert.Equal(2, avg.CurrentSize());
    }

    [Fact]
    public void Resize_ShrinkThenResetRefillsCleanly()
    {
        var avg = new RollingAverage(8);
        for (int i = 0; i < 8; ++i)
        {
            avg.Add(100.0);
        }

        avg.Resize(6);
        avg.Reset();

        double last = 0.0;
        for (int i = 0; i < 16; ++i)
        {
            last = avg.Add(1.0);
        }

        Assert.Equal(1.0, last, 10); // pre-fix stale 100.0 slots drove this negative
        Assert.Equal(1.0, avg.GetAverage(), 10);
        Assert.Equal(6, avg.CurrentSize());
    }

    [Fact]
    public void ZeroSizedWindow_AlwaysReturnsZero()
    {
        var avg = new RollingAverage(0);

        Assert.Equal(0.0, avg.Add(99.0));
        Assert.Equal(0, avg.CurrentSize());
    }

    [Fact]
    public void Add_KeepsExactWindowAcrossManyWraps()
    {
        var avg = new RollingAverage(3);
        double last = 0.0;
        for (int i = 1; i <= 100; ++i)
        {
            last = avg.Add(i);
        }

        Assert.Equal(3, avg.CurrentSize());
        Assert.Equal(99.0, last, 10); // {98,99,100}
        Assert.Equal(99.0, avg.GetAverage(), 10);
    }

    [Fact]
    public void Resize_GrowingKeepsExistingWindowContents()
    {
        var avg = new RollingAverage(2);
        avg.Add(1.0);
        avg.Add(2.0);
        avg.Add(3.0); // window {2,3}

        avg.Resize(4);
        avg.Add(4.0); // window {2,3,4}

        Assert.Equal(3, avg.CurrentSize());
        Assert.Equal(3.0, avg.GetAverage(), 10);
    }

    [Fact]
    public void Reset_ClearsWindowAndSum()
    {
        var avg = new RollingAverage(3);
        avg.Add(5.0);

        avg.Reset();

        Assert.Equal(0, avg.CurrentSize());
        Assert.Equal(0.0, avg.GetAverage());
    }
}
