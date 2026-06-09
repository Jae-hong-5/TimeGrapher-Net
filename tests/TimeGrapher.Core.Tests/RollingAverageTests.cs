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
