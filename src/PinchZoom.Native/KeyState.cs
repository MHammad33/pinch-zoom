using System.Runtime.InteropServices;

namespace PinchZoom.Native;

/// <summary>Polls the physical key state, as a backstop for a missed key-up.</summary>
public static class KeyState
{
    private const int VK_CAPITAL = 0x14;

    /// <summary>True while Caps Lock is physically held (the high bit of GetAsyncKeyState, not the toggle bit).</summary>
    public static bool IsCapsLockDown() => (NativeMethods.GetAsyncKeyState(VK_CAPITAL) & 0x8000) != 0;

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
    }
}
