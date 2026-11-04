using PinchZoom.Core;

namespace PinchZoom.Core.Tests;

public class AnchorCalculatorTests
{
    private const int W = 1920;
    private const int H = 1080;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(960, 540)]
    [InlineData(1919, 1079)]
    public void ZoomOfOne_GivesZeroOffset(int cx, int cy)
    {
        Assert.Equal((0, 0), AnchorCalculator.Calculate(cx, cy, W, H, 1.0));
    }

    [Fact]
    public void TopLeftCorner_StaysAtOrigin()
    {
        Assert.Equal((0, 0), AnchorCalculator.Calculate(0, 0, W, H, 2.0));
    }

    [Fact]
    public void Centre_MatchesCentredView()
    {
        // At 2x the view is half the screen, centred: (W/4, H/4).
        Assert.Equal((480, 270), AnchorCalculator.Calculate(960, 540, W, H, 2.0));
    }

    [Theory]
    [InlineData(2.0, 960, 540)]
    [InlineData(2.5, 1151, 647)]
    [InlineData(5.0, 1535, 863)]
    public void BottomRightCorner_StaysWithinScreen(double zoom, int expectedX, int expectedY)
    {
        var (x, y) = AnchorCalculator.Calculate(W - 1, H - 1, W, H, zoom);

        Assert.Equal((expectedX, expectedY), (x, y));
        Assert.True(x <= W - W / zoom);
        Assert.True(y <= H - H / zoom);
    }

    [Theory]
    [InlineData(-500, -500)]
    [InlineData(5000, 5000)]
    public void CursorOffScreen_IsClampedToScreen(int cx, int cy)
    {
        var zoom = 2.5;
        var (x, y) = AnchorCalculator.Calculate(cx, cy, W, H, zoom);

        var maxX = (int)Math.Floor(W - W / zoom);
        var maxY = (int)Math.Floor(H - H / zoom);
        Assert.InRange(x, 0, maxX);
        Assert.InRange(y, 0, maxY);
        Assert.Equal(cx < 0 ? (0, 0) : (maxX, maxY), (x, y));
    }

    [Theory]
    [InlineData(100, 900, 1.5)]
    [InlineData(1234, 321, 2.5)]
    [InlineData(1700, 60, 4.0)]
    [InlineData(37, 1043, 5.0)]
    public void PixelUnderCursor_StaysUnderCursor(int cx, int cy, double zoom)
    {
        var (x, y) = AnchorCalculator.Calculate(cx, cy, W, H, zoom);

        // Where the source pixel at the cursor lands on screen after magnification.
        var screenX = (cx - x) * zoom;
        var screenY = (cy - y) * zoom;

        // Integer offsets allow up to half a source pixel of drift, i.e. zoom/2 screen pixels.
        Assert.InRange(screenX, cx - zoom / 2, cx + zoom / 2);
        Assert.InRange(screenY, cy - zoom / 2, cy + zoom / 2);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    public void ZoomBelowOne_Throws(double zoom)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AnchorCalculator.Calculate(0, 0, W, H, zoom));
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, -1)]
    public void NonPositiveSize_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AnchorCalculator.Calculate(0, 0, width, height, 2.0));
    }
}
