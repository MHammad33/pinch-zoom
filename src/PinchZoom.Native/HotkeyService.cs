using System.Runtime.InteropServices;

namespace PinchZoom.Native;

/// <summary>
/// Low-level keyboard hook for the zoom key (Caps Lock). The key is swallowed so it never
/// toggles caps state. Raises <see cref="Pressed"/> once per hold (auto-repeat is ignored)
/// and <see cref="Released"/> on key-up.
/// Install from a thread that pumps messages (the WPF UI thread); events are raised on that thread.
/// Handlers must return quickly: Windows drops hooks that block past its timeout.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int VK_CAPITAL = 0x14;

    // Kept in a field so the GC never collects the delegate while Windows still holds it.
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hook;
    private bool _isDown;

    public event EventHandler? Pressed;
    public event EventHandler? Released;

    public HotkeyService() => _proc = HookCallback;

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>Installs the hook. Returns false (with the Win32 error code) instead of throwing.</summary>
    public bool TryInstall(out int errorCode)
    {
        errorCode = 0;
        if (IsInstalled) return true;

        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);

        if (_hook == IntPtr.Zero)
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        return true;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == VK_CAPITAL)
            {
                var msg = (int)wParam;
                if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                {
                    if (!_isDown)
                    {
                        _isDown = true;
                        Pressed?.Invoke(this, EventArgs.Empty);
                    }
                }
                else if (msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                {
                    if (_isDown)
                    {
                        _isDown = false;
                        Released?.Invoke(this, EventArgs.Empty);
                    }
                }

                // Swallow: a non-zero return stops the key reaching apps and the caps toggle.
                return (IntPtr)1;
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (!IsInstalled) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _isDown = false;
    }

    private static class NativeMethods
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
