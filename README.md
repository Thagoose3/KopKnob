# Knob

A small "notch" for Windows: a pill on the edge of the screen that expands into a panel of little tools.
Built for one person's daily use — light, and ~0 % CPU while it sits collapsed.

Windows 10 (19041+) / 11 · .NET 8 · WPF

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
- **Edit layout:** ✎ in the panel's top-left corner, or tray → **Edit layout**. Drag tiles, resize with S/W/L, hide with ✕, drag hidden tiles back from the bottom strip. **Done** saves, **Reset** = default, Esc = cancel.
- Hidden automatically while a fullscreen app (video, game, slideshow) is in front.
- Tray menu: Open · Edit layout · Position · Run at startup · Quit.

### Tiles

Timer (Pomodoro) · Media (now playing + controls) · Clipboard history · Calendar · Coffee (keep awake) · Notes · To-do · Clock · Disk · Tools · System monitor (hidden by default — add it in Edit layout, or open it from Tools).

### Hotkeys

| Keys | Tool |
|---|---|
| `Ctrl+Alt+K` | Calculator — Enter saves to history and copies the result |
| `Ctrl+Alt+Q` | QR code — Copy (1024 px PNG) or Save… |
| `Ctrl+Alt+T` | Text capture — drag a box on screen; Thai + English text goes to the clipboard |

Change them in `settings.json` (`"Hotkeys"`). If one is taken by another app, a tray notification says so.

## Data

Everything lives in `%APPDATA%\Knob\` as plain files (written atomically; a broken or hand-edited file falls back to defaults):

| File | What |
|---|---|
| `settings.json` | delays, hotkeys, clipboard size, dock position, run-at-startup, hide-on-fullscreen |
| `layout.json` | tile positions/sizes |
| `notes.txt` | Notes |
| `todo.json` | To-do |
| `clipboard.json` | Clipboard history (text only; items marked private by password managers are skipped; **Pause** in the tile stops recording) |
| `calc-history.json` | Calculator history |
| `crash.log` | Unexpected errors, if any |

No network access, no admin rights, no telemetry.

## Project

- `src/Knob.Core` — logic without UI/OS (layout grid, timer, calculator, clipboard history, OCR text cleanup, dock geometry…), fully unit-tested in `tests/Knob.Core.Tests`.
- `src/Knob.App` — WPF app: shell window, tiles, tools, Win32/WinRT wrappers.
- `docs/PLAN.md` — the plan; `docs/DECISIONS.md` — every choice the plan didn't make, and verification results.

Third-party: [QRCoder](https://github.com/codebude/QRCoder) (MIT), [Tesseract .NET](https://github.com/charlesw/tesseract) (Apache-2.0), Tesseract `tessdata_fast` `tha`/`eng` (Apache-2.0).
