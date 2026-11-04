namespace PinchZoom.Core;

/// <summary>
/// Works out the magnifier view offset that keeps the pixel under the cursor fixed on screen.
/// Pure math, no Windows calls.
/// </summary>
public static class AnchorCalculator
{
    /// <summary>
    /// Returns the top-left of the magnified source area for the given cursor, screen size and zoom.
    /// Per axis: <c>offset = clamp(cursor * (1 - 1/zoom), 0, size - size/zoom)</c>,
    /// so the view never shows anything outside the screen.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Zoom is below 1 or not a number, or a size is not positive.</exception>
    public static (int X, int Y) Calculate(int cursorX, int cursorY, int screenWidth, int screenHeight, double zoom)
    {
        if (double.IsNaN(zoom) || zoom < 1.0) throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be at least 1.");
        if (screenWidth <= 0) throw new ArgumentOutOfRangeException(nameof(screenWidth), screenWidth, "Size must be positive.");
        if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight), screenHeight, "Size must be positive.");

        return (Axis(cursorX, screenWidth, zoom), Axis(cursorY, screenHeight, zoom));
    }

    private static int Axis(int cursor, int size, double zoom)
    {
        // Floor the upper bound so rounding can never push the view past the screen edge.
        var max = (int)Math.Floor(size - size / zoom);
        var offset = (int)Math.Round(cursor * (1 - 1 / zoom));
        return Math.Clamp(offset, 0, max);
    }
}
