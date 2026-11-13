# Decisions

Things PLAN.md did not specify, and the choice made.

## M0

- **WinForms enabled in Knob.App** (`UseWindowsForms`) only for `NotifyIcon` and `SystemInformation.PowerStatus`. Its namespaces are removed from global usings to avoid clashes with WPF types.
- **Assembly name `Knob`** so the process shows as `Knob` (PLAN measures with `Get-Process Knob`).
- **`ShutdownMode=OnExplicitShutdown`**: the app lives in the tray; only `Quit` ends it.
- **`SystemClock`** added next to `IClock` in Core as the production implementation.
