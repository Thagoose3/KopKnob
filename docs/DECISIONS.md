# Decisions

Things PLAN.md did not specify, and the choice made.

## Testing status

M0 was first written build-only because Smart App Control blocked unsigned builds. The user turned SAC off
on 2026-11-13; everything below was then run on the real machine (1920×1080-class screen, ~125% scaling).

M0 acceptance results:

- [x] `dotnet test` — 12/12 pass (`JsonStoreTests`, `MinuteAlignerTests`)
- [x] Pill visible top-center showing `HH:mm` and battery % — screenshot
- [x] Hover expands, leaving collapses — screenshot
- [x] Not in taskbar / Alt+Tab — window has `WS_EX_TOOLWINDOW`, no `WS_EX_APPWINDOW`, `ShowInTaskbar=False` (checked via `GetWindowLongPtr`)
- [x] Doesn't steal focus — `GetForegroundWindow` stays Notepad after hover, clicking the pill, and clicking inside the panel
- [x] Second instance exits quietly (exit code 0)
- [x] Idle CPU — 0.000 s of CPU time over 60 s collapsed
- [ ] **User to check:** change scaling 100% → 150% in Settings and confirm the pill stays top-center (changing display settings is left to the user)
- [ ] **User to check:** tray menu (Open / Run at startup / Quit)

Note for M5: working set was ~160 MB right after start (PLAN target < 120 MB). WinForms is loaded for the tray icon; revisit in Task 5.2.

## M0

- **WinForms enabled in Knob.App** (`UseWindowsForms`) only for `NotifyIcon` and `SystemInformation.PowerStatus`. Its namespaces are removed from global usings to avoid clashes with WPF types. `WFAC010` warning suppressed (it asks to move DPI config out of app.manifest, which PLAN requires).
- **Assembly name `Knob`** so the process shows as `Knob` (PLAN measures with `Get-Process Knob`).
- **`ShutdownMode=OnExplicitShutdown`**: the app lives in the tray; only `Quit` ends it.
- **`SystemClock`** added next to `IClock` in Core as the production implementation.
- **`IJsonNormalizable`** (Core): optional hook `JsonStore` calls after load so a type can repair nulls from hand-edited files (e.g. `"Hotkeys": null`). Unparseable JSON, wrong types, `null`, or an array all fall back to `new T()`.
- **`KnobPaths`** (Core) holds all `%APPDATA%\Knob\` file paths in one place.
- **Window is always panel-sized (720×250 DIP); only the inner `Surface` border animates** between pill (150×30) and panel. Resizing a layered transparent window every frame is janky; fully transparent pixels of a layered window are click-through, so the unused area does not block the desktop.
- **Positioning in physical pixels** with `SetWindowPos` on the primary monitor (`MonitorFromPoint(0,0, PRIMARY)` + `GetDpiForMonitor`), re-run on `WM_DISPLAYCHANGE`, `WM_DPICHANGED` and `SystemEvents.DisplaySettingsChanged`.
- **Event timing:** `PanelOpened` fires when leaving `Collapsed` (so tiles can fill in during the open animation); `PanelClosed` fires when `Collapsed` is reached. Re-entering while `Closing` reopens without a second `PanelOpened`.
- **Hover delay timers** are one-shot `DispatcherTimer`s that only run between mouse enter/leave; they never run while collapsed and idle.
- **Esc** only works once the window has keyboard focus (after clicking a text input, or opening from the tray). In hover mode the window is `WS_EX_NOACTIVATE` and never receives keys — by design, so it doesn't steal focus.
- **Tray "Open"** opens the panel and activates the window, so a click anywhere else (`Deactivated`) closes it. Left-click on the tray icon does the same.
- **Minute timer re-aligns on every tick** (`MinuteAligner.DelayUntilNextMinute`, +20 ms slack) instead of a fixed 60 s interval, so it can't drift; also re-aligned on resume and `SystemEvents.TimeChanged`.
- **Battery** hidden when `NoSystemBattery` or status `Unknown`.
- **Autostart:** the registry value is the source of truth; at startup `settings.RunAtStartup` is synced from it and the stored exe path is refreshed if enabled.
- **Tray icon** is drawn in code (accent pill), so no `.ico` asset is needed yet.
