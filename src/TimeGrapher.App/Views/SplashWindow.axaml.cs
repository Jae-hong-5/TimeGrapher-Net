using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace TimeGrapher.App.Views;

public partial class SplashWindow : Window
{
    private const int FrameCount = 74;
    private const int FramesPerSecond = 30;
    // Bounded read-ahead: ~11 MB of decoded 640x360 frames instead of ~65 MB for all 74.
    private const int DecodeAheadFrames = 12;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1.0 / FramesPerSecond);
    // Tick faster than the frame rate: Windows quantizes dispatcher timers up to the
    // next 15.625 ms system tick, so a 33 ms interval actually fires every ~47 ms and
    // skips every other frame. ~15.6 ms ticks catch each 33.3 ms frame boundary within
    // half a frame; ShowFrame no-ops when the frame is unchanged.
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan PlaybackDuration = TimeSpan.FromSeconds((double)FrameCount / FramesPerSecond);

    private readonly Bitmap?[] mFrames = new Bitmap?[FrameCount];
    private readonly object mFrameLock = new();
    private readonly DispatcherTimer mTimer;
    private readonly Stopwatch mPlaybackClock = new();
    private Thread? mDecodeThread;
    private bool mDecodeStopRequested;
    private int mDisplayedFrameNumber;
    private bool mCompleted;

    public event EventHandler? PlaybackCompleted;

    public SplashWindow()
    {
        InitializeComponent();

        // Decode only the first frame up front so the window paints immediately;
        // the rest stream in on a background thread inside a bounded window.
        mFrames[0] = LoadFrame(1);

        mTimer = new DispatcherTimer { Interval = TickInterval };
        mTimer.Tick += OnTimerTick;

        Opened += OnOpened;
        Closed += OnClosed;

        ShowFrame(1);

        mDecodeThread = new Thread(DecodeRemainingFrames)
        {
            Name = "SplashFrameDecode",
            IsBackground = true,
        };
        mDecodeThread.Start();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        mPlaybackClock.Restart();
        mTimer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (mPlaybackClock.Elapsed >= PlaybackDuration)
        {
            ShowFrame(FrameCount);
            CompletePlayback();
            return;
        }

        ShowFrame(GetFrameNumberForElapsed(mPlaybackClock.Elapsed));
    }

    internal static int GetFrameNumberForElapsed(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return 1;
        }

        long frameNumber = elapsed.Ticks / FrameInterval.Ticks + 1;
        return (int)Math.Clamp(frameNumber, 1, FrameCount);
    }

    private void ShowFrame(int requestedFrameNumber)
    {
        Bitmap? bitmap = null;
        lock (mFrameLock)
        {
            // Show the newest decoded frame at or before the requested one; if the
            // decoder is briefly behind, keep displaying the current frame.
            int frameToShow = 0;
            for (int frameNumber = requestedFrameNumber; frameNumber > mDisplayedFrameNumber; frameNumber--)
            {
                if (mFrames[frameNumber - 1] != null)
                {
                    frameToShow = frameNumber;
                    break;
                }
            }

            if (frameToShow == 0)
            {
                return;
            }

            bitmap = mFrames[frameToShow - 1];
            for (int frameNumber = Math.Max(mDisplayedFrameNumber, 1); frameNumber < frameToShow; frameNumber++)
            {
                mFrames[frameNumber - 1]?.Dispose();
                mFrames[frameNumber - 1] = null;
            }

            mDisplayedFrameNumber = frameToShow;
            Monitor.PulseAll(mFrameLock);
        }

        SplashImage.Source = bitmap;
    }

    private void DecodeRemainingFrames()
    {
        for (int frameNumber = 2; frameNumber <= FrameCount; frameNumber++)
        {
            lock (mFrameLock)
            {
                while (!mDecodeStopRequested && frameNumber - DecodeWindowBase() > DecodeAheadFrames)
                {
                    Monitor.Wait(mFrameLock, TimeSpan.FromMilliseconds(250));
                }

                if (mDecodeStopRequested)
                {
                    return;
                }

                ReclaimSkippedFrames();
            }

            Bitmap frame;
            try
            {
                frame = LoadFrame(frameNumber);
            }
            catch
            {
                // A missing/corrupt frame keeps the last decoded frame on screen;
                // playback completion stays time-driven.
                return;
            }

            lock (mFrameLock)
            {
                if (mDecodeStopRequested)
                {
                    frame.Dispose();
                    return;
                }

                if (frameNumber <= mDisplayedFrameNumber)
                {
                    // Playback already moved past this frame; skip ahead to it.
                    frame.Dispose();
                    frameNumber = mDisplayedFrameNumber;
                    continue;
                }

                mFrames[frameNumber - 1] = frame;
            }
        }
    }

    /// <summary>
    /// Anchor the decode-ahead window to the playback clock, not just the displayed
    /// frame: while the UI thread is stalled (e.g. MainWindow construction behind the
    /// splash) the displayed frame stops advancing, and a display-anchored decoder
    /// would stall too, forcing a juddery capped catch-up after the stall. Following
    /// the clock keeps the post-stall target frame already decoded so the animation
    /// jumps straight back onto the timeline. (Reading a running Stopwatch from the
    /// decode thread is a benign race — a slightly stale value only shrinks the window.)
    /// </summary>
    private int DecodeWindowBase()
    {
        return Math.Max(mDisplayedFrameNumber, GetFrameNumberForElapsed(mPlaybackClock.Elapsed));
    }

    /// <summary>
    /// While the UI thread is stalled the clock-anchored decoder keeps producing, but
    /// frames between the frozen displayed frame and the clock will never be shown —
    /// the next ShowFrame jumps straight to the clock frame. Reclaim them here so a
    /// long stall cannot accumulate undisplayed bitmaps (keeps the ~12-frame memory
    /// bound). Must be called under mFrameLock; never touches the displayed frame.
    /// </summary>
    private void ReclaimSkippedFrames()
    {
        int clockFrameNumber = GetFrameNumberForElapsed(mPlaybackClock.Elapsed);
        for (int frameNumber = mDisplayedFrameNumber + 1; frameNumber < clockFrameNumber; frameNumber++)
        {
            mFrames[frameNumber - 1]?.Dispose();
            mFrames[frameNumber - 1] = null;
        }
    }

    private static Bitmap LoadFrame(int frameNumber)
    {
        var uri = new Uri($"avares://TimeGrapher.App/Assets/Splash/splash_{frameNumber:0000}.png");
        using Stream stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    }

    private void CompletePlayback()
    {
        if (mCompleted)
        {
            return;
        }

        mCompleted = true;
        mTimer.Stop();
        PlaybackCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        mTimer.Stop();
        mTimer.Tick -= OnTimerTick;

        lock (mFrameLock)
        {
            mDecodeStopRequested = true;
            Monitor.PulseAll(mFrameLock);
        }

        mDecodeThread?.Join(TimeSpan.FromSeconds(1));
        mDecodeThread = null;

        SplashImage.Source = null;
        lock (mFrameLock)
        {
            for (int i = 0; i < mFrames.Length; i++)
            {
                mFrames[i]?.Dispose();
                mFrames[i] = null;
            }
        }
    }
}
