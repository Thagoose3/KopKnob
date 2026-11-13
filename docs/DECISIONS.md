# Decisions

Things PLAN.md did not specify, and the choice made.

## ⚠ Testing status (read first)

This dev machine has **Smart App Control** in enforce mode. It blocks every unsigned DLL/EXE we build:
`dotnet test` cannot load `Knob.Core.Tests.dll`, and `Knob.exe` is blocked from starting (confirmed via the
CodeIntegrity event log, 2026-11-13). The user chose to keep writing code with **build-only** verification and test everything later.

Commits made under this rule have only been checked with `dotnet build` (0 warnings, 0 errors).
Before going further than M0, run on a machine (or after the user turns SAC off):

```
dotnet test Knob.sln
```

Not yet run / verified:

- [ ] `JsonStoreTests` (Task 0.2)
- [ ] `MinuteAlignerTests` (Task 0.4)
- [ ] M0 acceptance criteria (all need the app running) — see PLAN.md "เกณฑ์รับงาน M0"

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
