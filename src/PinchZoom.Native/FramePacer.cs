using System.Runtime.InteropServices;

namespace PinchZoom.Native;

/// <summary>Paces a render loop to the display refresh using DWM composition.</summary>
public static class FramePacer
{
    // Roughly one 60 Hz frame, used only if DwmFlush fails.
    private const int FallbackFrameMs = 16;

    /// <summary>Blocks until DWM's next composition pass (vsync).</summary>
    public static void WaitForNextFrame()
    {
        if (NativeMethods.DwmFlush() < 0)
            Thread.Sleep(FallbackFrameMs);
    }

    private static class NativeMethods
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmFlush();
    }
}
