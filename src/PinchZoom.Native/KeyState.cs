using System.Runtime.InteropServices;

namespace PinchZoom.Native;

/// <summary>
/// Polls the physical key state, as a backstop for a missed key-up. Only valid while the keyboard hook
/// passes Caps Lock through: a swallowed key reads as up here.
/// </summary>
public static class KeyState
{
    /// <summary>True while the key is physically held (the high bit of GetAsyncKeyState, not the toggle bit).</summary>
    public static bool IsKeyDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
    }
}
