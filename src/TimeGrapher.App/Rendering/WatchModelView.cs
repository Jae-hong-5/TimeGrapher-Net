using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Shows the watch as a shaded 3D model and, when <see cref="Position"/> changes,
/// rotates smoothly from the current orientation to the target orientation —
/// <see cref="Quaternion.Slerp"/> over <see cref="WatchModelOrientation.TransitionMilliseconds"/>
/// with cubic ease-in-out, the timing the model file recommends. There is no
/// drag or pointer interaction by design: orientation is driven solely by the
/// position selection. The model and its software renderer are loaded lazily on
/// first paint, so constructing the control headless costs nothing.
/// </summary>
internal sealed class WatchModelView : Control
{
    public static readonly StyledProperty<WatchPosition> PositionProperty =
        AvaloniaProperty.Register<WatchModelView, WatchPosition>(nameof(Position), WatchPosition.CH);

    // Render the model into an offscreen buffer at this multiple of the device
    // resolution, then let the high-quality bitmap scaler downsample it: cheap
    // antialiasing for a CPU rasterizer that has no per-edge coverage of its own.
    private const int Supersample = 2;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    private readonly WatchModelRasterizer _rasterizer = new();
    private readonly Stopwatch _clock = new();
    private DispatcherTimer? _timer;

    private Quaternion _current = WatchModelOrientation.For(WatchPosition.CH);
    private Quaternion _from;
    private Quaternion _to;
    private bool _attached;

    private WriteableBitmap? _bitmap;
    private byte[] _pixels = Array.Empty<byte>();
    private int _pixelWidth;
    private int _pixelHeight;

    static WatchModelView()
    {
        AffectsRender<WatchModelView>(PositionProperty);
    }

    public WatchModelView()
    {
        _from = _current;
        _to = _current;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    public WatchPosition Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PositionProperty)
        {
            OnPositionChanged(change.GetNewValue<WatchPosition>());
        }
    }

    private void OnPositionChanged(WatchPosition position)
    {
        Quaternion target = WatchModelOrientation.For(position);

        // Off-screen (not the active tab): snap so re-selecting the tab shows the
        // correct pose immediately, with no animation nobody would see.
        if (!_attached)
        {
            _current = target;
            _from = target;
            _to = target;
            return;
        }

        // Animate from wherever the model is now — re-targeting mid-flight stays
        // smooth (no jump back to a base pose).
        _from = _current;
        _to = target;
        _clock.Restart();
        StartAnimation();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _current = WatchModelOrientation.For(Position);
        _from = _current;
        _to = _current;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        StopAnimation();
    }

    private void StartAnimation()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = FrameInterval };
            _timer.Tick += (_, _) => Advance();
        }

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }

        InvalidateVisual();
    }

    private void StopAnimation()
    {
        _clock.Reset();
        _timer?.Stop();
    }

    private void Advance()
    {
        double progress = _clock.Elapsed.TotalMilliseconds / WatchModelOrientation.TransitionMilliseconds;
        double eased = WatchModelOrientation.CubicEaseInOut(progress);
        _current = Quaternion.Slerp(_from, _to, (float)eased);

        if (progress >= 1.0)
        {
            _current = _to;
            StopAnimation();
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 1.0 || bounds.Height < 1.0)
        {
            return;
        }

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int pw = Math.Max(1, (int)Math.Ceiling(bounds.Width * scaling * Supersample));
        int ph = Math.Max(1, (int)Math.Ceiling(bounds.Height * scaling * Supersample));
        EnsureBitmap(pw, ph);

        _rasterizer.Render(WatchModelMesh.Shared, _current, _pixels, pw, ph);
        using (ILockedFramebuffer framebuffer = _bitmap!.Lock())
        {
            CopyToFramebuffer(framebuffer);
        }

        context.DrawImage(_bitmap, new Rect(0, 0, pw, ph), bounds);
    }

    private void EnsureBitmap(int width, int height)
    {
        if (_bitmap is not null && _pixelWidth == width && _pixelHeight == height)
        {
            return;
        }

        _bitmap?.Dispose();
        _bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        _pixels = new byte[width * height * 4];
        _pixelWidth = width;
        _pixelHeight = height;
    }

    private void CopyToFramebuffer(ILockedFramebuffer framebuffer)
    {
        int rowBytes = _pixelWidth * 4;
        for (int y = 0; y < _pixelHeight; y++)
        {
            Marshal.Copy(_pixels, y * rowBytes, framebuffer.Address + y * framebuffer.RowBytes, rowBytes);
        }
    }
}
