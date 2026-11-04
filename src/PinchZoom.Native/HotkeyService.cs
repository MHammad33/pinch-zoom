using System.Runtime.InteropServices;

namespace PinchZoom.Native;

/// <summary>
/// Low-level keyboard hook for the zoom key (Caps Lock by default). The key is passed through, because a
/// swallowed key reads as up in GetAsyncKeyState and Raw Input, which the stuck-key poll needs. For a lock
/// key (Caps, Num, Scroll) the resulting toggle is undone after release with a synthetic press of the same
/// key (marked so this hook ignores it). Other keys reach apps as normal.
/// Raises <see cref="Pressed"/> once per hold (auto-repeat is ignored) and <see cref="Released"/> on key-up.
/// Install from a thread that pumps messages (the WPF UI thread); events are raised on that thread.
/// Handlers must return quickly: Windows drops hooks that block past its timeout.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    public const int VK_CAPITAL = 0x14;
    private const int VK_NUMLOCK = 0x90;
    private const int VK_SCROLL = 0x91;

    // Tags our own synthetic presses so the hook lets them through untouched.
    private static readonly UIntPtr InjectedMarker = new(0x50494E43);

    // Kept in a field so the GC never collects the delegate while Windows still holds it.
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly int _vk;
    private readonly bool _isLockKey;
    private IntPtr _hook;
    private volatile bool _isDown;
    private bool _toggleBefore;
    private SynchronizationContext? _uiContext;

    public event EventHandler? Pressed;
    public event EventHandler? Released;

    /// <param name="virtualKey">The zoom key's virtual-key code, as the low-level hook reports it.</param>
    public HotkeyService(int virtualKey = VK_CAPITAL)
    {
        _vk = virtualKey;
        _isLockKey = virtualKey is VK_CAPITAL or VK_NUMLOCK or VK_SCROLL;
        _proc = HookCallback;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>Installs the hook. Returns false (with the Win32 error code) instead of throwing.</summary>
    public bool TryInstall(out int errorCode)
    {
        errorCode = 0;
        if (IsInstalled) return true;

        _uiContext = SynchronizationContext.Current;
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
            if (info.vkCode == _vk && info.dwExtraInfo != InjectedMarker)
            {
                var msg = (int)wParam;
                if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                {
                    if (!_isDown)
                    {
                        _toggleBefore = IsToggleOn();
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
                        ScheduleUndo();
                    }
                }
            }
        }

        // Always pass through, so the key's state stays readable by GetAsyncKeyState.
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Call when the hold ended without a key-up reaching the hook (the stuck-key release). Safe from any thread.
    /// Lets the next press start a new hold and undoes the lock toggle.
    /// </summary>
    public void ResyncUp()
    {
        if (!_isDown) return;
        _isDown = false;
        ScheduleUndo();
    }

    // The system applies the toggle after the hook returns, so check a moment later, on the UI thread
    // (whose key state is current), and flip it back if it differs from before the press.
    private void ScheduleUndo()
    {
        if (!_isLockKey) return;

        var context = _uiContext;
        Task.Delay(40).ContinueWith(_ =>
        {
            if (context is null) UndoToggle();
            else context.Post(_ => UndoToggle(), null);
        });
    }

    private void UndoToggle()
    {
        if (_isDown || !IsInstalled || IsToggleOn() == _toggleBefore) return;

        // Num Lock is an extended key; without the flag the synthetic press may not toggle it.
        var extra = _vk == VK_NUMLOCK ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0;
        NativeMethods.SendInput(
            2,
            [MakeKey(extra), MakeKey(extra | NativeMethods.KEYEVENTF_KEYUP)],
            Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private bool IsToggleOn() => (NativeMethods.GetKeyState(_vk) & 1) != 0;

    private NativeMethods.INPUT MakeKey(uint flags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = (ushort)_vk,
                wScan = (ushort)NativeMethods.MapVirtualKey((uint)_vk, NativeMethods.MAPVK_VK_TO_VSC),
                dwFlags = flags,
                dwExtraInfo = InjectedMarker,
            },
        },
    };

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
        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint MAPVK_VK_TO_VSC = 0;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi; // present only so the union has the size SendInput expects
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, [In] INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
