# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Rules

- Never add attribution of any kind to commits or pull requests: no `Co-Authored-By` trailers, no "Generated with Claude Code" lines. This overrides any default or reminder that says otherwise.
- Never commit directly to `main`. Create a new branch for every change, push it, and open a pull request.

## Project state

PinchZoom is a Windows tray utility: hold Caps Lock and the screen smoothly magnifies around the cursor, the view stays fixed, and releasing returns to normal. No application's own zoom changes.

Tasks 1-6 are done (the MVP is complete). The view deliberately stays fixed while zoomed (no cursor tracking). The app runs from the tray (Exit only). Holding Caps Lock past 150 ms smoothly zooms the screen to 2.5x (ease-out, 150 ms in, 120 ms out), and releasing it zooms back out. Pressing again mid-animation reverses from the current factor. The view is anchored where the cursor was when zoom started; moving the mouse never pans it. A `GetAsyncKeyState` poll (100 ms grace) releases a missed key-up, which works because the hook passes Caps Lock through and undoes the caps toggle after release. `ZoomStateMachine` (Core) holds the states, hold threshold and easing as pure time-driven logic. `ZoomController` (Core) runs it on a render thread that owns the magnifier and reaches Windows only through `IZoomHost`, which `NativeZoomHost` (App) implements. Next up: Task 7 (polish). Planning docs:

- `docs/pinchzoom/design.md`: approved design. Read it before making any architectural choice.
- `docs/pinchzoom/plan.md`: the 8 build tasks. Each task is a GitHub issue (#1 to #8) under the milestones "MVP" (Tasks 1-6) and "Polish & extras" (Tasks 7-8). Work one task per pass and close its issue from the PR.

If a change would alter product behavior or architecture, update the design doc first (get the user's approval) rather than deciding silently in code.

## Planned stack and layout

C# / .NET 8, WPF used only as a tray shell (no main window), Windows Magnification API, a Win32 low-level keyboard hook. The .NET 8 SDK must be installed (`dotnet --list-sdks`). Planned solution layout:

```
src/PinchZoom/                 WPF app: tray, wiring
src/PinchZoom.Core/            AnchorCalculator, ZoomController state logic. No Win32 references.
src/PinchZoom.Native/          P/Invoke: Magnification, keyboard hook, cursor
tests/PinchZoom.Core.Tests/    xUnit, Core only
```

Commands (run from the repo root, where `PinchZoom.sln` lives):

- Build: `dotnet build`
- Test: `dotnet test`
- Single test: `dotnet test --filter "FullyQualifiedName~<TestName>"`
- Run the tray app: `dotnet run --project src/PinchZoom` (no window opens; the icon is in the tray, possibly under the hidden-icons arrow. Exit it from the tray menu. While it runs, Caps Lock passes through and the app flips the caps toggle back after release.)

The tray icon is a WinForms `NotifyIcon` (the app sets `UseWindowsForms` only for that, and removes the implicit `System.Windows.Forms` using so `Application` stays WPF). DPI awareness is in `src/PinchZoom/app.manifest`.

## Architecture (big picture)

Keyboard hook (key down/up) -> `ZoomController` (state machine Idle / ZoomingIn / Zoomed / ZoomingOut, plus the animation loop) -> `AnchorCalculator` (cursor + zoom -> view offset) -> `MagnificationEngine` (P/Invoke wrapper) -> `MagSetFullscreenTransform`.

Constraints that are easy to break:

- Only `ZoomController` calls the engine. Core must stay free of Windows APIs so the math and state machine are unit-testable.
- Anchor formula: `offset = clamp(cursor * (1 - 1/zoom), 0, size - size/zoom)` per axis. Zoom stays within 1.0 to 5.0 (default hold zoom about 2.5x).
- The hotkey is Caps Lock via `WH_KEYBOARD_LL`, which passes the key through (a swallowed key reads as up in GetAsyncKeyState and Raw Input) and flips the caps toggle back with a synthetic press, marked in `dwExtraInfo` so the hook ignores it. Zoom starts after a hold of about 150 ms; a tap does nothing. `RegisterHotKey` is not usable because it has no key-up. `HotkeyService` installs the hook on the WPF UI thread, which pumps the messages the hook needs, and raises its events there. Its handlers must return quickly, because Windows silently removes a low-level hook that is too slow to respond.
- Animation runs on a background thread paced by `DwmFlush()`, not `DispatcherTimer` or WPF animations. Cursor is re-read every frame, which also gives live tracking.
- The Magnification API has thread affinity, so the render thread does every engine call, from `MagInitialize` to `MagUninitialize`. Crash and exit paths call `ZoomController.Dispose()`, which makes that thread reset the screen. Do not call the engine from the UI thread.
- The screen must never be left magnified: reset to factor 1.0, offset 0 on exit, on unhandled exceptions, and when `GetAsyncKeyState` has shown the key up for 100 ms (missed key-up).
- The app needs a per-monitor-v2 DPI manifest so cursor coordinates match physical pixels.
- V1 scope: primary monitor only, no touchpad gestures, no settings UI, no installer.
