using System;
using System.IO;

using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace TimeGrapher.App.Views;

public partial class SplashWindow : Window
{
    private const int FrameCount = 122;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1.0 / 30.0);

    private readonly DispatcherTimer mTimer;
    private int mNextFrameNumber = 1;
    private Bitmap? mCurrentFrame;
    private Bitmap? mPreviousFrame;
    private bool mCompleted;

    public event EventHandler? PlaybackCompleted;

    public SplashWindow()
    {
        InitializeComponent();

        mTimer = new DispatcherTimer { Interval = FrameInterval };
        mTimer.Tick += OnTimerTick;

        Opened += OnOpened;
        Closed += OnClosed;

        _ = ShowNextFrame();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        mTimer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!ShowNextFrame())
        {
            CompletePlayback();
        }
    }

    private bool ShowNextFrame()
    {
        if (mNextFrameNumber > FrameCount)
        {
            return false;
        }

        Bitmap frame;
        try
        {
            frame = LoadFrame(mNextFrameNumber);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine("Splash frame load failed: " + ex.Message);
            return false;
        }

        mPreviousFrame?.Dispose();
        mPreviousFrame = mCurrentFrame;
        mCurrentFrame = frame;
        SplashImage.Source = frame;
        mNextFrameNumber++;
        return true;
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
        mPreviousFrame?.Dispose();
        mCurrentFrame?.Dispose();
    }
}
