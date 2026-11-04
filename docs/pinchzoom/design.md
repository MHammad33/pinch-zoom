# PinchZoom

> Status: Approved

## Summary

PinchZoom is a Windows tray utility. While the user holds a hotkey, the whole screen smoothly magnifies around the mouse cursor and the view follows the cursor. Releasing the key smoothly returns to normal. No application's own zoom level changes.

Stack: C# / .NET 8, WPF (tray shell only, no visible window), Windows Magnification API, Win32 low-level keyboard hook.

Main tradeoff: the Magnification API gives true, GPU-composited, live magnification (the screen stays interactive and updates in real time) but works on the primary monitor only and has DPI caveats. We accept that for V1.

Source brief: Notion → PinchZoom → Project Brief.

## Context

Windows has no system-wide "temporary zoom". The built-in Magnifier works as an accessibility tool (separate UI, modes, lens). Chrome's pinch-to-zoom feel is smooth and cursor-anchored, but only inside the browser. PinchZoom brings that feel to every app.

## Proposed approach

```
Low-level keyboard hook (key down / key up)
        ↓
   ZoomController   (state machine + animation loop)
        ↓
  AnchorCalculator  (cursor + zoom → view offset)
        ↓
 MagnificationEngine (P/Invoke wrapper)
        ↓
 Magnification API  (MagSetFullscreenTransform)
```

### Components

- **HotkeyService**: installs a `WH_KEYBOARD_LL` hook. Tracks whether the configured combo is fully held. Raises `Pressed` and `Released`. `RegisterHotKey` is rejected because it reports only press, not release.
- **ZoomController**: state machine `Idle → ZoomingIn → Zoomed → ZoomingOut → Idle`. Owns a render loop that runs only while not Idle. Each frame: advance the current zoom factor toward the target (1.0 or the target zoom), read the cursor, ask AnchorCalculator for the offset, call the engine.
- **AnchorCalculator**: pure function, no Windows calls, unit-testable.
  For screen size `W×H`, zoom `z`, cursor `(cx, cy)`:
  `xOff = clamp(cx * (1 - 1/z), 0, W - W/z)`, same for y.
  This keeps the pixel under the cursor fixed on screen and pans as the cursor moves.
- **MagnificationEngine**: wraps `MagInitialize`, `MagSetFullscreenTransform(factor, xOff, yOff)`, `MagUninitialize`. Guarantees a reset to `1.0, 0, 0` on exit.
- **TrayHost** (WPF `Application` with no main window, plus `NotifyIcon`): tray icon, Exit menu item, later a simple settings entry.

### Animation

A background thread loop paced by `DwmFlush()` (vsync) rather than `DispatcherTimer`, which is too jittery for smooth zoom. Ease-out curve, about 150 ms in and 120 ms out. Zoom factor is the animated value; the offset is recomputed every frame from the live cursor position, so tracking (Phase 6) falls out of the same loop.

### Project layout

```
PinchZoom.sln
src/PinchZoom/                 WPF app (tray, wiring)
src/PinchZoom.Core/            AnchorCalculator, ZoomController state logic (no Win32)
src/PinchZoom.Native/          P/Invoke: Magnification, keyboard hook, cursor
tests/PinchZoom.Core.Tests/    xUnit tests for Core
docs/pinchzoom/design.md
```

Core has no Windows dependency so the math and state machine can be tested without a screen.

## Decisions

| Decision | Choice | Rejected | Why |
|---|---|---|---|
| Language/stack | C# / .NET 8 / WPF | Electron/JS | Native access to Magnification API; Electron would need a capture overlay and a native hook module. |
| Magnification | `MagSetFullscreenTransform` | Capture + overlay window; `MagSetWindowTransform` host window | Live content, no capture lag, no self-capture problem. Simplest API. |
| Hotkey | Low-level keyboard hook | `RegisterHotKey` | Need key-up. |
| Hotkey | Caps Lock, held (user's choice; configurable later). The hook swallows Caps Lock so it never toggles caps state or shows the indicator. | Multi-key combos, Win key, Right Ctrl | User wants one key. Caps Lock is rarely needed; swallowing it is done in the keyboard hook. |
| Zoom amount | Chrome-like range: 1x to 5x max. MVP zooms to a default of 2.5x on hold; mouse wheel while held adjusts level (Phase 8) | Fixed single level only | Chrome pinch is continuous and caps near 5x. Wheel gives similar control without a touchpad. |
| Animation timing | `DwmFlush` loop on a background thread | `DispatcherTimer`, WPF animations | Better frame pacing; WPF animation does not drive the Magnification API anyway. |
| Settings storage | JSON file in `%AppData%\PinchZoom` | Registry, settings UI | Simplest; V1 has no settings UI. |
| Tests | xUnit for Core only | UI/integration automation | Hooks and screen magnification cannot be asserted reliably; verify those manually. |

## Requirements

- R1: Holding the hotkey zooms the screen to the target zoom around the cursor within ~200 ms with no visible stepping.
- R2: While zoomed, moving the cursor pans the view so the cursor stays at the same relative screen position (offset formula above).
- R3: Releasing the hotkey returns to 1.0x smoothly and leaves the screen exactly as before (offset 0, factor 1.0).
- R4: No application's own zoom changes.
- R5: App runs with no taskbar window; tray icon with Exit.
- R6: Exiting the app (or crashing) never leaves the screen magnified.
- R7: Pressing again during zoom-out reverses from the current factor with no jump.
- R8: Caps Lock is consumed by PinchZoom and no longer toggles caps. Zoom triggers when it is held past a short threshold (about 150 ms); a quicker tap does nothing.

## Invariants

- Zoom factor stays within `[1.0, maxZoom]`.
- Offsets always stay within the screen so no area outside the desktop is shown.
- Only ZoomController calls the engine.
- Core project references no Windows APIs.

## Error behavior

- `MagInitialize` fails: show a tray balloon "Could not start magnifier", app stays idle, no crash.
- Hook install fails: same, with the error code logged.
- Unhandled exception: global handler resets magnification before the process dies. The OS also resets the transform when the initializing process ends.
- Stuck state (key-up missed, e.g. focus change or UAC prompt): ZoomController also polls `GetAsyncKeyState` for the combo each frame and releases if it is no longer down.

## Test strategy

- Unit (xUnit, Core): AnchorCalculator (corners, centre, clamping, z=1), ZoomController transitions (press during zoom-out, release during zoom-in), easing bounds.
- Manual checklist per phase, run on the real machine: VS Code, a PDF viewer, Figma/browser, Slack.
  - Press/hold/move/release feels smooth.
  - Exit while zoomed returns to normal.
  - Kill the process via Task Manager while zoomed returns to normal.
- Performance check: no dropped-frame feel at 1080p and at the user's actual display resolution.

## Build phases (maps to the Notion brief)

1. **Scaffold**: solution, WPF app with no window, tray icon, Exit. Done when the tray icon appears and Exit quits.
2. **Magnification**: engine wrapper, hard-code a 2x zoom via a tray menu item "Test zoom" with a toggle. Done when the screen visibly zooms and resets cleanly. This is the key risk-proving phase.
3. **Cursor anchoring**: AnchorCalculator plus tests; apply at cursor position at the moment of zoom.
4. **Hotkey**: keyboard hook, press-and-hold triggers zoom, release resets (still instant).
5. **Smooth animation**: DwmFlush loop, easing, reversal handling.
6. **Live tracking**: per-frame cursor update while zoomed.
7. **Polish**: tray controls, clean exit/crash safety, JSON config for hotkey and zoom level.
8. **Wheel zoom (after MVP)**: while the key is held, the mouse wheel changes zoom between 1x and 5x, like Chrome pinch.

Each phase ends in something runnable and a small commit.

## Out of scope

- macOS/Linux.
- Touchpad pinch (future: separate investigation, since Windows exposes no simple API for precision-touchpad gestures to desktop apps).
- Settings UI, installer, visual effects.
- Second monitor: user has one monitor for now.
- Multi-monitor and mixed-DPI correctness (`MagSetFullscreenTransform` affects the primary monitor; we will note behavior, not solve it).

## Open questions

1. **Caps Lock side effects**: decided. Swallow it fully, so caps typing is lost unless the user turns it back on via a tray option or setting. Revisit in Phase 7. Non-blocking.
2. **Default zoom level** (2.5x) and 5x cap: tune by feel in Phase 5.
3. **Elevated windows**: the fullscreen transform generally magnifies everything, but behaviour over elevated/secure windows (UAC, admin apps) should be checked in Phase 2. Non-blocking; if it proves limited, document it.
   - Phase 2 result: an elevated (administrator) terminal is magnified like any other window. UAC secure-desktop prompts are untested. A fixed centre zoom hides the taskbar, so the test zoom auto-reverts after 5 s. Revisit when cursor anchoring lands.
4. **DPI awareness**: the app needs a per-monitor-v2 manifest so cursor coordinates and the screen size match physical pixels. We will set this in Phase 1. Non-blocking.
