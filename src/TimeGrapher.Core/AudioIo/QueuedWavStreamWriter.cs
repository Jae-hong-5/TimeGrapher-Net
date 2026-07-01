using System.Collections.Concurrent;
using System.Buffers;

namespace TimeGrapher.Core.AudioIo;

/// <summary>
/// Bounded asynchronous WAV writer used by live analysis. File I/O stays off the
/// analysis thread; full queues drop recording blocks instead of stalling analysis.
/// </summary>
public sealed class QueuedWavStreamWriter : ISampleWriter
{
    private const int DefaultQueueCapacity = 128;
    // Close() joins the writer thread on the caller (the UI thread during Stop), so this bounds
    // how long a stop blocks while the recording drains. The producer is stopped before Close, so
    // a healthy disk drains the <=128-block queue in milliseconds; a slow SD card that cannot
    // finish within this budget drops the recording tail (reported via DroppedBlocks) rather than
    // freezing the UI for seconds.
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly int _queueCapacity;
    private readonly object _stateLock = new();
    private BlockingCollection<QueuedSampleBlock>? _queue;
    private WavStreamWriter? _inner;
    private Thread? _thread;
    private volatile bool _writerFailed;
    private ulong _droppedBlocks;

    private sealed class QueuedSampleBlock
    {
        public float[] Buffer = Array.Empty<float>();
        public int Length;
    }

    public QueuedWavStreamWriter(int queueCapacity = DefaultQueueCapacity)
    {
        _queueCapacity = Math.Max(1, queueCapacity);
    }

    public ulong DroppedBlocks => _droppedBlocks;
    public bool IsOpen => _inner?.IsOpen == true && _queue != null;

    public bool Open(string filePath, int sampleRate, int channels)
    {
        lock (_stateLock)
        {
            if (_inner != null)
            {
                return false;
            }

            var inner = new WavStreamWriter();
            if (!inner.Open(filePath, sampleRate, channels))
            {
                inner.Dispose();
                return false;
            }

            _writerFailed = false;
            _droppedBlocks = 0;
            _inner = inner;
            _queue = new BlockingCollection<QueuedSampleBlock>(boundedCapacity: _queueCapacity);
            _thread = new Thread(WriterLoop)
            {
                Name = "WavWriter",
                IsBackground = true,
                Priority = ThreadPriority.Normal,
            };
            _thread.Start();
            return true;
        }
    }

    public bool Write(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return true;
        }

        BlockingCollection<QueuedSampleBlock>? queue = _queue;
        if (queue == null || queue.IsAddingCompleted || _writerFailed)
        {
            return false;
        }

        float[] buffer = ArrayPool<float>.Shared.Rent(samples.Length);
        samples.CopyTo(buffer);
        var block = new QueuedSampleBlock
        {
            Buffer = buffer,
            Length = samples.Length,
        };

        try
        {
            if (queue.TryAdd(block))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // InvalidOperationException: adding was completed (writer failure / Close).
            // ObjectDisposedException: Close already disposed the queue while this
            // Write was in flight. Either way the block cannot be queued; fall
            // through to return the rented buffer and count it as dropped rather
            // than letting an unhandled exception kill the analysis worker thread.
        }

        ArrayPool<float>.Shared.Return(buffer);
        Interlocked.Increment(ref _droppedBlocks);
        return false;
    }

    public bool Close()
    {
        BlockingCollection<QueuedSampleBlock>? queue;
        Thread? thread;
        WavStreamWriter? inner;

        lock (_stateLock)
        {
            queue = _queue;
            thread = _thread;
            inner = _inner;
        }

        if (queue == null || inner == null)
        {
            return true;
        }

        queue.CompleteAdding();
        bool joined = thread == null || thread.Join(CloseTimeout);
        if (!joined)
        {
            Console.Error.WriteLine("QueuedWavStreamWriter: writer thread did not stop before timeout");
            return false;
        }

        lock (_stateLock)
        {
            if (ReferenceEquals(_queue, queue))
            {
                _queue = null;
                _thread = null;
                _inner = null;
            }
        }

        // Return any blocks still queued (e.g. enqueued just before the writer
        // thread observed CompleteAdding) so their rented buffers are not leaked
        // when the queue is disposed.
        DrainQueuedBlocks(queue);
        bool closed = inner.Close();
        queue.Dispose();
        inner.Dispose();
        return joined && closed && !_writerFailed;
    }

    public void Dispose()
    {
        Close();
    }

    private void WriterLoop()
    {
        BlockingCollection<QueuedSampleBlock>? queue;
        WavStreamWriter? inner;
        lock (_stateLock)
        {
            queue = _queue;
            inner = _inner;
        }

        if (queue == null || inner == null)
        {
            return;
        }

        try
        {
            foreach (QueuedSampleBlock block in queue.GetConsumingEnumerable())
            {
                try
                {
                    if (!_writerFailed && !inner.Write(block.Buffer.AsSpan(0, block.Length)))
                    {
                        _writerFailed = true;
                    }
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(block.Buffer);
                }

                if (_writerFailed)
                {
                    // Stop accepting new blocks BEFORE draining so a producer that
                    // already passed the IsAddingCompleted check cannot TryAdd a
                    // rented buffer after the drain finishes (which would leak it,
                    // since no consumer remains and Close only disposes the queue).
                    queue.CompleteAdding();
                    DrainQueuedBlocks(queue);
                    return;
                }
            }
        }
        catch (InvalidOperationException)
        {
            _writerFailed = true;
        }
    }

    private static void DrainQueuedBlocks(BlockingCollection<QueuedSampleBlock> queue)
    {
        while (queue.TryTake(out QueuedSampleBlock? block))
        {
            ArrayPool<float>.Shared.Return(block.Buffer);
        }
    }
}
