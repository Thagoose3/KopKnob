# Knob

A small "notch" for Windows: a pill on the edge of the screen that expands into a panel of little tools.
Built for one person's daily use — light, and ~0 % CPU while it sits collapsed.

Windows 10 (19041+) / 11 · .NET 8 · WPF

## Install

Run `KnobSetup-<version>.exe`. It installs for the current user only (no admin rights) into
`%LOCALAPPDATA%\Programs\Knob`, adds a Start menu shortcut, optionally starts Knob when you sign in, and shows up
in **Settings → Apps**, where it can be uninstalled. .NET is bundled — nothing else to install.
Running a newer setup updates in place and keeps notes, to-dos and settings (`%APPDATA%\Knob`).

The installer isn't code-signed, so Windows SmartScreen shows "Windows protected your PC" the first time:
**More info → Run anyway**.

Build it with:

```
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

→ `artifacts\KnobSetup-<version>.exe` (the version comes from `Directory.Build.props`).
Silent install: `KnobSetup.exe /S` (`/D=folder`, `/NOLAUNCH`); silent uninstall: `"Uninstall Knob.exe" /S` (`/PURGE` also deletes user data).

## Build & run

Needs the .NET 8 SDK.

```
dotnet build Knob.sln
dotnet test Knob.sln
dotnet run --project src/Knob.App
```

Single-file build (needs the .NET 8 Desktop Runtime on the machine that runs it):

```
dotnet publish src/Knob.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

→ `publish/Knob.exe`. Keep the `tessdata` and `x64` folders next to it (OCR language data and native OCR libraries).

> If Windows **Smart App Control** is on, it blocks unsigned builds like this one (tests and the app). Either turn it off or sign the exe.

## Using it

- **Hover** the pill (or click it) to open the panel; move away or press **Esc** to close.
- **Move it:** with the panel open, press on an empty area and drag. Drop it in the middle → floating panel (stays open; ✕ or Esc sends it back). Drop it on the **left / right / top** edge → it docks there, at that spot. Tray → **Position** has presets if it ever gets lost.
- **Pages:** the panel shows one page at a time; switch with the tab bar along the bottom (the open tab shows its name). The last tab is remembered.
- **Customize tabs:** ··· in the panel's top-right corner, or tray → **Edit layout**. Tick to show/hide a tab, arrows to change the order.
- Hidden automatically while a fullscreen app (video, game, slideshow) is in front.
- **Themes:** tray → Theme → Default or **Sutra scroll** (Journey to the West: lacquer red and gold, the knob is a rolled scroll that unrolls when it opens). Switching restarts Knob.
- Tray menu: Open · Edit layout · Position · Theme · Run at startup · Quit.

### Pages

Home (big clock, Coffee keep-awake, disk space, Capture text) · Music (now playing, cover-tinted) · Timer (Pomodoro) · Clipboard history · Notes · To-do · Calendar · Calc · QR · System monitor.

### Hotkeys

| Keys | Tool |
|---|---|
| `Ctrl+Alt+Space` | Quick search — pages, pinned apps, actions, clipboard text; type `12*7` to calculate |
| `Ctrl+Alt+S` | Screenshot — drag a box (Enter = whole screen); saved to Pictures\Knob Screenshots, copied, and put on the Shelf |
| `Ctrl+Alt+C` | Colour picker — magnifier over the screen; click copies `#RRGGBB` |
| `Ctrl+Alt+T` | Text capture — drag a box on screen; Thai + English text goes to the clipboard |
| `Ctrl+Alt+K` | Calculator — Enter saves to history and copies the result |
| `Ctrl+Alt+Q` | QR code — Copy (1024 px PNG) or Save… |

Change them in **Settings → Hotkeys** (the ··· button in the panel, or the tray menu). If one is taken by another app, a tray notification says so.

## Data

Everything lives in `%APPDATA%\Knob\` as plain files (written atomically; a broken or hand-edited file falls back to defaults):

| File | What |
|---|---|
| `settings.json` | delays, hotkeys, clipboard size, dock position, tabs (order/hidden/last), run-at-startup, hide-on-fullscreen |
| `notes.txt` | Notes |
| `todo.json` | To-do |
| `clipboard.json`, `clipimages\` | Clipboard history: text, and images (PNG) when nothing textual was copied. Items marked private by password managers are skipped; **Pause** stops recording |
| `shelf.json`, `launch.json` | Shelf and Launch pins (paths only — files are never copied) |
| `calc-history.json` | Calculator history |
| `events.log` | Hardware events and hotkey registration, for diagnosing alerts |
| `crash.log` | Unexpected errors, if any |

No admin rights, no telemetry. Network use: the weather (open-meteo.com: the city name when searching, then coordinates rounded to ~1 km) and update checks (api.github.com, about once a day; can be turned off in Settings → About).

## Project

- `src/Knob.Core` — logic without UI/OS (tab settings, timer, calculator, clipboard history, OCR text cleanup, dock geometry…), fully unit-tested in `tests/Knob.Core.Tests`.
- `src/Knob.App` — WPF app: shell window, pages + tab bar, tiles (the views pages are built from), tools, Win32/WinRT wrappers.
- `docs/PLAN.md` — the plan; `docs/DECISIONS.md` — every choice the plan didn't make, and verification results.

Third-party: [QRCoder](https://github.com/codebude/QRCoder) (MIT), [Tesseract .NET](https://github.com/charlesw/tesseract) (Apache-2.0), Tesseract `tessdata_fast` `tha`/`eng` (Apache-2.0).
