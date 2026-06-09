namespace TimeGrapher.Core.Metrics;

/// <summary>
/// Port of RollingAverage (RollingAverage.h/.cpp). Maintains a running sum over a
/// FIFO window of doubles. The original used std::deque&lt;double&gt;; here a fixed
/// circular buffer keeps the same FIFO semantics and arithmetic order (add the
/// new value, then subtract the evicted front) without allocating per Add — this
/// runs on the per-beat metrics hot path.
/// </summary>
public sealed class RollingAverage
{
    private double[] _window;
    private int _head;  // index of the oldest element
    private int _count;
    private long _maxSize; // size_t in the original
    private double _runningSum;

    public RollingAverage(long size)
    {
        _maxSize = size;
        _window = new double[CapacityFor(size)];
        _runningSum = 0.0;
    }

    private static int CapacityFor(long size)
        => size > 0 ? (int)Math.Min(size, int.MaxValue) : 0;

    public double Add(double val)
    {
        if (_maxSize == 0) return 0.0;

        _runningSum += val;

        if (_count == _maxSize)
        {
            // Full window: the new value takes the evicted front's slot, so the
            // buffer never needs the original's transient size maxSize+1.
            _runningSum -= _window[_head];
            _window[_head] = val;
            _head = (_head + 1) % _window.Length;
        }
        else
        {
            _window[(int)((_head + _count) % _window.Length)] = val;
            _count++;
        }

        return _runningSum / _count;
    }

    public int CurrentSize()
    {
        return _count;
    }

    public void Resize(long newSize)
    {
        _maxSize = newSize;

        if (_maxSize > _window.Length)
        {
            var grown = new double[CapacityFor(_maxSize)];
            for (int i = 0; i < _count; ++i)
            {
                grown[i] = _window[(_head + i) % _window.Length];
            }
            _window = grown;
            _head = 0;
        }

        while (_count > _maxSize)
        {
            _runningSum -= _window[_head];
            _head = (_head + 1) % _window.Length;
            _count--;
        }
    }

    public void Reset()
    {
        _head = 0;
        _count = 0;
        _runningSum = 0;
    }

    public double GetAverage()
    {
        if (_count == 0) return 0.0;
        return _runningSum / _count;
    }
}
