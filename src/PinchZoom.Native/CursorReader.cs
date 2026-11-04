using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PinchZoom.Native;

/// <summary>Reads the mouse cursor position in physical screen pixels (requires a DPI-aware process).</summary>
public static class CursorReader
{
    public static (int X, int Y) GetPosition()
    {
        if (!NativeMethods.GetCursorPos(out var p))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetCursorPos failed.");
        }

        return (p.X, p.Y);
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT point);
    }
}
