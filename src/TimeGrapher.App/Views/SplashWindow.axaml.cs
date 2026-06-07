using System;
using System.Diagnostics;
using System.IO;

using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace TimeGrapher.App.Views;

public partial class SplashWindow : Window
{
    private const int FrameCount = 122;
    private const int FramesPerSecond = 30;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1.0 / FramesPerSecond);
    private static readonly TimeSpan PlaybackDuration = TimeSpan.FromSeconds((double)FrameCount / FramesPerSecond);

    private readonly Bitmap[] mFrames;
    private readonly DispatcherTimer mTimer;
    private readonly Stopwatch mPlaybackClock = new();
    private int mDisplayedFrameNumber;
    private bool mCompleted;

    public event EventHandler? PlaybackCompleted;

    public SplashWindow()
    {
        InitializeComponent();
        mFrames = LoadFrames();

        mTimer = new DispatcherTimer { Interval = FrameInterval };
        mTimer.Tick += OnTimerTick;

        Opened += OnOpened;
        Closed += OnClosed;

        ShowFrame(1);
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

    private void ShowFrame(int frameNumber)
    {
        if (mDisplayedFrameNumber == frameNumber)
        {
            return;
        }

        mDisplayedFrameNumber = frameNumber;
        SplashImage.Source = mFrames[frameNumber - 1];
    }

    private static Bitmap[] LoadFrames()
    {
        var frames = new Bitmap[FrameCount];
        try
        {
            for (int frameNumber = 1; frameNumber <= FrameCount; frameNumber++)
            {
                frames[frameNumber - 1] = LoadFrame(frameNumber);
            }
        }
        catch
        {
            foreach (Bitmap? frame in frames)
            {
                frame?.Dispose();
            }

            throw;
        }

        return frames;
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
        SplashImage.Source = null;
        foreach (Bitmap frame in mFrames)
        {
            frame.Dispose();
        }
    }
}
