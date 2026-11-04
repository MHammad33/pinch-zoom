# PinchZoom build plan

Derived from the approved [design](design.md). Each task is one build pass and ends with something runnable. Tracked as GitHub issues under two milestones.

## Milestone: MVP (Tasks 1–6)

### Task 1: Tray app shell
**Goal:** WPF app with no window, tray icon and Exit menu item. Solution has App, Core, Native and test projects, plus a per-monitor DPI manifest.
**Done when:** `dotnet build` passes, the tray icon appears, Exit quits cleanly.
**Depends on:** .NET 8 SDK installed.

### Task 2: Prove the magnification works
**Goal:** Magnification API wrapper and a tray item "Test zoom" that toggles a fixed zoom.
**Done when:** the screen visibly zooms and resets cleanly; the app resets the screen on exit; limits over admin windows are noted.
**Depends on:** Task 1. Key risk-proving step.

### Task 3: Cursor anchoring
**Goal:** `AnchorCalculator` with unit tests, applied at the cursor position when zoom starts.
**Done when:** tests pass; "Test zoom" keeps the area under the cursor in place.
**Depends on:** Task 2.

### Task 4: Caps Lock hold-to-zoom
**Goal:** low-level keyboard hook that swallows Caps Lock; holding about 0.15 s zooms, releasing resets (still instant).
**Done when:** hold zooms, release resets, Caps Lock no longer toggles caps.
**Depends on:** Task 3. First usable version.

### Task 5: Smooth animation
**Goal:** `ZoomController` state machine with tests, driven by an easing loop paced by `DwmFlush`; handles pressing again during zoom-out.
**Done when:** zoom in and out are smooth with no jumps; state-machine tests pass.
**Depends on:** Task 4.

### Task 6: Live cursor tracking
**Goal:** the view pans with the cursor every frame while zoomed; a `GetAsyncKeyState` poll releases a stuck state if a key-up is missed.
**Done when:** the zoomed view follows the cursor smoothly; a missed key-up never leaves the screen zoomed.
**Depends on:** Task 5. This completes the MVP.

## Milestone: Polish & extras (Tasks 7–8)

### Task 7: Polish
**Goal:** JSON config in `%AppData%\PinchZoom` for key and default zoom; tray option to restore Caps Lock; crash handler that resets the screen; tray error notifications.
**Done when:** config changes take effect; killing the process while zoomed restores the screen; failures show a tray message.
**Depends on:** Task 6.

### Task 8: Mouse-wheel zoom
**Goal:** while Caps Lock is held, the wheel changes zoom between 1x and 5x, like Chrome pinch.
**Done when:** the wheel adjusts zoom smoothly in range; it behaves normally when Caps Lock is not held.
**Depends on:** Task 6.
