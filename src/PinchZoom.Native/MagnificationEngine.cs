using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PinchZoom.Native;

/// <summary>
/// Thin wrapper over the Windows Magnification API (full-screen transform, primary monitor only).
/// Call every member from the same thread that called <see cref="Initialize"/>; the API has thread affinity.
/// </summary>
public sealed class MagnificationEngine : IDisposable
{
    private bool _initialized;

    public bool IsInitialized => _initialized;

    /// <summary>Primary monitor size in physical pixels (requires a DPI-aware process).</summary>
    public static (int Width, int Height) PrimaryScreenSize =>
        (NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN),
         NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN));

    /// <summary>Starts the magnifier runtime. Returns false (with the Win32 error code) instead of throwing.</summary>
    public bool TryInitialize(out int errorCode)
    {
        errorCode = 0;
        if (_initialized) return true;

        if (!NativeMethods.MagInitialize())
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        _initialized = true;
        return true;
    }

    /// <summary>Sets the full-screen magnification. Factor 1.0 with offset 0,0 is "no zoom".</summary>
    public void SetTransform(float factor, int xOffset, int yOffset)
    {
        if (!_initialized) throw new InvalidOperationException("Engine is not initialized.");

        if (!NativeMethods.MagSetFullscreenTransform(factor, xOffset, yOffset))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "MagSetFullscreenTransform failed.");
        }
    }

    /// <summary>Returns the screen to normal. Never throws; safe to call from exit and crash paths.</summary>
    public void Reset()
    {
        if (!_initialized) return;
        try { NativeMethods.MagSetFullscreenTransform(1.0f, 0, 0); } catch { /* best effort */ }
    }

    public void Dispose()
    {
        if (!_initialized) return;
        Reset();
        try { NativeMethods.MagUninitialize(); } catch { /* best effort */ }
        _initialized = false;
    }

    private static class NativeMethods
    {
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        [DllImport("Magnification.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MagInitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MagUninitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MagSetFullscreenTransform(float magLevel, int xOffset, int yOffset);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);
    }
}
