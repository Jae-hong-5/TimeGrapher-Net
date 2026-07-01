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
    // a healthy disk drains the <=128-block queue in milliseconds. If a slow SD card cannot finish
    // within this budget, Close() returns false (the stop could not be confirmed in time) but the
    // writer thread keeps draining in the background and finalizes the WAV header when it finishes
    // (see WriterLoop), so the recording on disk stays a valid, correctly-sized file rather than
    // one left with placeholder sizes - it is not silently truncated or corrupted.
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly int _queueCapacity;
    private readonly object _stateLock = new();
    private BlockingCollection<QueuedSampleBlock>? _queue;
    private WavStreamWriter? _inner;
    private Thread? _thread;
    private volatile bool _writerFailed;
    // Set by the writer thread once it has finalized the WAV header (patched the RIFF/data
    // sizes); its value is the success of that patch. Close() reports it once the thread joins.
    private volatile bool _finalizeOk;
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

            var queue = new BlockingCollection<QueuedSampleBlock>(boundedCapacity: _queueCapacity);
            _writerFailed = false;
            _finalizeOk = false;
            _droppedBlocks = 0;
            _inner = inner;
            _queue = queue;
            // Hand the queue and writer to the loop directly rather than re-reading the
            // fields: Close() detaches the fields when a stop begins, and a fast Close right
            // after Open must not race the loop into reading a null writer and skipping the
            // header finalize.
            _thread = new Thread(() => WriterLoop(queue, inner))
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

        lock (_stateLock)
        {
            queue = _queue;
            thread = _thread;
        }

        if (queue == null)
        {
            return true;
        }

        // Stop accepting new writes (idempotent across retries) so the writer thread drains
        // to end-of-input, then finalizes the header and disposes the queue in its finally.
        queue.CompleteAdding();
        bool joined = thread == null || thread.Join(CloseTimeout);
        if (!joined)
        {
            // The disk is slow: the writer thread is still draining and will finalize the
            // header when it finishes. Keep the writer state intact so IsOpen still reports
            // open and the caller can RETRY Close (MainWindow.AudioCloseCheck treats a false
            // return with IsOpen==true as retryable, not terminal). The recording on disk is
            // not corrupted - finalization just has not been confirmed within the budget yet.
            Console.Error.WriteLine("QueuedWavStreamWriter: writer thread still draining; retry Close to confirm finalization");
            return false;
        }

        // The thread has exited: it finalized the header (result in _finalizeOk). Detach the
        // fields so IsOpen reports closed and a new session can Open, then dispose the queue -
        // only now that the thread has provably stopped consuming it. BlockingCollection.Dispose
        // is idempotent, so a double Close is safe.
        lock (_stateLock)
        {
            if (ReferenceEquals(_queue, queue))
            {
                _queue = null;
                _thread = null;
                _inner = null;
            }
        }

        queue.Dispose();
        return _finalizeOk && !_writerFailed;
    }

    public void Dispose()
    {
        Close();
    }

    private void WriterLoop(BlockingCollection<QueuedSampleBlock> queue, WavStreamWriter inner)
    {
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
                    // since no consumer remains once this loop exits).
                    queue.CompleteAdding();
                    DrainQueuedBlocks(queue);
                    break;
                }
            }
        }
        catch (InvalidOperationException)
        {
            _writerFailed = true;
        }
        finally
        {
            // Finalize the WAV header (patch the RIFF/data sizes) for whatever was written and
            // release the file here, in the writer thread, so the recording is always a valid
            // file even when Close() timed out waiting to join on a slow disk: the drain then
            // finishes in the background and finalization still happens here. Record whether the
            // header patch succeeded so a joined Close() reports a genuine finalization failure
            // rather than a false success. The queue is NOT disposed here - Close() disposes it
            // once, after it has joined this thread, so a Close retry can never call
            // CompleteAdding on a queue this thread already disposed (ObjectDisposedException).
            _finalizeOk = inner.Close();
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
