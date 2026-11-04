# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Rules

- Never add attribution of any kind to commits or pull requests: no `Co-Authored-By` trailers, no "Generated with Claude Code" lines. This overrides any default or reminder that says otherwise.
- Never commit directly to `main`. Create a new branch for every change, push it, and open a pull request.

## Project state

PinchZoom is a Windows tray utility: hold Caps Lock and the screen smoothly magnifies around the cursor, the view follows the cursor, and releasing returns to normal. No application's own zoom changes.

Task 1 (tray app shell) is done: the solution builds and the app shows a tray icon with Exit. Planning docs:

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
- Run the tray app: `dotnet run --project src/PinchZoom` (no window opens; the icon is in the tray, possibly under the hidden-icons arrow. Exit it from the tray menu.)

The tray icon is a WinForms `NotifyIcon` (the app sets `UseWindowsForms` only for that, and removes the implicit `System.Windows.Forms` using so `Application` stays WPF). DPI awareness is in `src/PinchZoom/app.manifest`.

## Architecture (big picture)

Keyboard hook (key down/up) -> `ZoomController` (state machine Idle / ZoomingIn / Zoomed / ZoomingOut, plus the animation loop) -> `AnchorCalculator` (cursor + zoom -> view offset) -> `MagnificationEngine` (P/Invoke wrapper) -> `MagSetFullscreenTransform`.

Constraints that are easy to break:

- Only `ZoomController` calls the engine. Core must stay free of Windows APIs so the math and state machine are unit-testable.
- Anchor formula: `offset = clamp(cursor * (1 - 1/zoom), 0, size - size/zoom)` per axis. Zoom stays within 1.0 to 5.0 (default hold zoom about 2.5x).
- The hotkey is Caps Lock via `WH_KEYBOARD_LL`, which is swallowed (it must not toggle caps). Zoom starts after a hold of about 150 ms; a tap does nothing. `RegisterHotKey` is not usable because it has no key-up.
- Animation runs on a background thread paced by `DwmFlush()`, not `DispatcherTimer` or WPF animations. Cursor is re-read every frame, which also gives live tracking.
- The screen must never be left magnified: reset to factor 1.0, offset 0 on exit, on unhandled exceptions, and when `GetAsyncKeyState` shows the key is no longer down (missed key-up).
- The app needs a per-monitor-v2 DPI manifest so cursor coordinates match physical pixels.
- V1 scope: primary monitor only, no touchpad gestures, no settings UI, no installer.
