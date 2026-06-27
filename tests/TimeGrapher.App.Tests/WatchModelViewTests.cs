using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// End-to-end check of the 3D position view: the bundled <c>avares://</c> model
/// loads at run time and the control rasterizes a visible watch into its bitmap.
/// Rendering needs the real platform (Skia), so it runs under
/// <see cref="HeadlessPlatform"/>.
/// </summary>
public sealed class WatchModelViewTests
{
    [Fact]
    public void RendersASolidWatchForTheSelectedPosition()
    {
        HeadlessPlatform.EnsureStarted();

        var view = new WatchModelView { Width = 236, Height = 126 };
        // Set before attach: snaps to the pose with no animation.
        view.Position = WatchPosition.P3H;
        Assert.Equal(WatchPosition.P3H, view.Position);

        view.Measure(new Size(236, 126));
        view.Arrange(new Rect(0, 0, 236, 126));

        var target = new RenderTargetBitmap(new PixelSize(236, 126), new Vector(96, 96));
        target.Render(view);

        int opaquePixels = CountOpaquePixels(target, 236, 126);

        // The watch fills a large part of the panel; a blank render (asset missing
        // or projection wrong) would leave it fully transparent.
        Assert.True(opaquePixels > 2000, $"Only {opaquePixels} opaque pixels were drawn.");
    }

    [Fact]
    public void RasterizerKeepsUnityModelScale()
    {
        Assert.Equal(1.0f, WatchModelRasterizer.ModelScale);
    }

    private static int CountOpaquePixels(RenderTargetBitmap bitmap, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally
        {
            handle.Free();
        }

        int opaque = 0;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 16)
            {
                opaque++;
            }
        }

        return opaque;
    }
}
