namespace TimeGrapher.Core.Metrics;

/// <summary>
/// Fixed-capacity wrapping rate-error trace: a verbatim port of the tic/toc
/// rate-error buffers <see cref="WatchMetrics"/> keeps (its AddOrOverwrite ring),
/// reproduced here so each watch test position can own its own trace. X is the
/// ring slot index (0..capacity-1); while filling, X grows 0,1,2,…; once full,
/// new values overwrite the oldest slot's Y in place and the wrap index advances,
/// so the scatter refreshes cyclically without moving in X — the same display the
/// global ring produces, but isolated per position.
/// </summary>
internal sealed class RateErrorRing
{
    private readonly List<double> _x = new();
    private readonly List<double> _y = new();
    private readonly int _capacity;
    private int _index;

    public RateErrorRing(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
        }

        _capacity = capacity;
    }

    public int Count => _y.Count;

    /// <summary>
    /// Adds a value at the next ring slot. Mirrors <see cref="WatchMetrics"/>'s
    /// AddOrOverwrite: appends X=slot index and Y while filling, then overwrites
    /// Y[index] in place and advances the wrap index, leaving X fixed at the slot
    /// positions so the trace refreshes cyclically.
    /// </summary>
    public void AddOrOverwrite(double value)
    {
        if (_y.Count < _capacity)
        {
            _y.Add(value);
            _x.Add(_index);
            _index = (_index + 1) % _capacity;
        }
        else
        {
            _y[_index] = value;
            _index = (_index + 1) % _capacity;
        }
    }

    public void Reset()
    {
        _x.Clear();
        _y.Clear();
        _index = 0;
    }

    /// <summary>Copies the current ring contents into the given lists (cleared first).</summary>
    public void SnapshotTo(List<double> x, List<double> y)
    {
        x.Clear();
        y.Clear();
        for (int i = 0; i < _y.Count; i++)
        {
            x.Add(_x[i]);
            y.Add(_y[i]);
        }
    }
}
