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

M1 acceptance results (2026-11-13):

- [x] `dotnet test` — 73/73 pass
- [x] All M1 tiles render and work: Timer (start → ring/countdown, pill shows `25m`), Coffee (toggle → "Awake" + dot on pill), Notes (typing), To-do (add, tick → "Done 1/1"), Clock, Calendar (today highlighted, week starts per culture), Disk — screenshots
- [x] Restart: notes, to-do and layout come back; Coffee starts off — screenshot after relaunch
- [x] Click outside after typing closes the panel
- [x] Idle CPU still 0.000 s / 60 s collapsed
- [ ] **User to check (needs admin):** Coffee on → `powercfg /requests` lists Knob under DISPLAY and SYSTEM; off → gone
- [ ] **User to check:** quit from the tray while Coffee is on → keep-awake released
- [ ] **User to check:** let a 1-minute timer finish → sound + pill flashes 3×

M2 acceptance results (2026-11-13):

- [x] `dotnet test` — 98/98 pass
- [x] Clipboard: copied `knob-test-1/2/3` → shown 3, 2, 1; clicking item 1 moved it to the top, no duplicate (clipboard.json + screenshot)
- [x] Clipboard privacy: text with `ExcludeClipboardContentFromMonitorProcessing`, and text with `CanIncludeInClipboardHistory = 0`, were not stored
- [x] CPU vs Task Manager: 6 of 12 threads busy → Knob 95–100 %, `% Processor Utility` 92–106 % (Task Manager shows it capped at 100)
- [x] Panel collapsed after using the monitor → Knob used 0.016 s CPU in 60 s
- [x] Media tile shows "Nothing playing" with no session
- [ ] **User to check:** play something in Spotify / a browser → title, artist, art and progress show; prev / play-pause / next work
- [ ] **User to check:** copy a password from your password manager → it does not appear in the Clipboard tile

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

## M1

- **Default layout changed** (PLAN allowed it): a month calendar does not fit a W tile (~168×70 DIP), so Calendar is L at (6,0); Clock shrinks to S. Row 2 is now `coffee S, notes W, todo W, clock S, disk S, launcher S`. Media/clipboard/launcher slots stay empty until M2/M3.
- **TileSize stored as names** (`"L"`) in layout.json for hand-editing.
- **Pomodoro `AddMinutes` only when not running**, and it resets the countdown; ± buttons are disabled while running.
- **Pomodoro timer scheduling:** one `DispatcherTimer`. Panel open, or last minute → ticks on each countdown-second boundary. Collapsed with more than a minute left → a single one-shot to the 1-minute mark; the pill's `25m` text is refreshed by the existing minute tick. Countdown text always rounds up (`TimerFormat`), so `0:00` only shows when time is up.
- **Pill auto-width:** the pill grows to fit (coffee dot, timer text); it animates only when the width actually changes.
- **Panel background made fully opaque** (`#FF101014`); the 95% version let windows behind show through.
- **Clock and Calendar use English text and the Gregorian calendar** even on a th-TH system (which would show 2569); week start still follows `CultureInfo.CurrentCulture`.
- **Clock tile has no timer of its own:** it refreshes on panel open and on the shared minute tick while open.
- **Icons** use Segoe Fluent Icons / Segoe MDL2 Assets glyphs (built into Windows), no image assets.
- **Tile views subscribe on `Loaded` and unsubscribe on `Unloaded`**, so rebuilding views (M4) won't leak handlers.
- **layout.json is written at startup if missing**, not only on clean exit.
- **AtomicFile** (Core) holds the tmp-then-move write used by `JsonStore` and notes.txt.
- **Stuck-open fix (after M1):** "hold open while typing" used `IsKeyboardFocusWithin`, which is also true when focus sits on the window itself or in a text box of a window that failed to activate (then no `Deactivated` ever comes). Now: hold only while the window `IsActive` **and** a `TextBoxBase` in it has focus; when the hold ends with the pointer outside, the close timer starts. Tray "Open" falls back to hover rules if activation fails.

## M2

- **CPU % uses PDH `\Processor Information(_Total)\% Processor Utility`**, the frequency-aware number Task Manager shows. Time-based `GetSystemTimes` (what PLAN specified) is correct for "% Processor Time" but on this turbo-boosting CPU reads ~55 % where Task Manager shows ~95 %, failing the ±5 % criterion. Raw PDH (not `PerformanceCounter`) is opened once on first use and only sampled while the monitor is visible. `GetSystemTimes` + `CpuUsageCalculator` stays as the fallback if PDH is unavailable.
- **Minimal Launcher + `IToolHost` pulled forward from M3**: the monitor is not in the default layout and PLAN says it opens from the Launcher, so M2 couldn't be verified without them. Launcher currently has one button (System monitor); M3 adds Calc/QR/OCR. `PanelHost` swaps the tile grid for a full-panel tool page with a Back button; closing the panel returns to the tiles.
- **Monitor polls while visible only** — as a placed tile with the panel open, or as the tool page — and re-baselines counters when it starts, so the first value isn't averaged over the idle gap.
- **Clipboard privacy** also honours `Clipboard Viewer Ignore` (older convention some password managers use), besides the two formats in PLAN. Reading uses one `GetDataObject` snapshot; any failure other than "clipboard busy" drops the item rather than risk storing something private.
- **Clipboard trim never drops the newest item**, even when every older item is pinned (found by test).
- **Copy-back** isn't specially suppressed: the resulting clipboard update just moves the item to the top (no duplicate), which is also the expected "most recent" order.
- **Media position** is extrapolated from the session's `LastUpdatedTime` while playing; the 1 s progress timer runs only while the panel is open and something is playing.

## M3

- **Downloads (user approved 2026-11-13):** NuGet `QRCoder` 1.8.1 (MIT) and `Tesseract` 5.2.0 (Apache-2.0, ships x64/x86 native leptonica + tesseract50); `tha.traineddata` (1.0 MB) and `eng.traineddata` (3.9 MB) from `tesseract-ocr/tessdata_fast` (Apache-2.0), committed under `src/Knob.App/tessdata/` and copied next to the exe.
- **Hotkey parser** requires at least one modifier and exactly one key; accepts `Ctrl`/`Control`, `Win`/`Windows`, letters, digits, F1–F24, Space, Tab, Enter, navigation keys, PrintScreen. `MOD_NOREPEAT` so holding the keys doesn't re-fire. Failed registrations are reported in one tray balloon.
- **Calculator:** percent is postfix (`200*15%` = 30); unary minus binds looser than `^` (`-2^2` = -4); integer powers are exact decimal (square-and-multiply), fractional powers go through `double`. Results show with thousands separators; Enter copies the plain number (no separators) so it pastes anywhere. Clicking a history entry puts its *expression* back.
- **QR:** `forceUtf8` so Thai text scans as UTF-8. Preview renders at an integer number of pixels per module (crisp). Square modules overlap by 0.5 px to avoid hairline seams. Copy puts both a bitmap and a "PNG" stream on the clipboard.
- **`NotchController.KeepOpen`** (new): set while the Save dialog is up so hover-out / deactivation don't collapse the panel; M4 layout editing will reuse it. Esc still closes.
- **Pill messages** ("Reading…", "Copied N chars", "No text found", "OCR data missing", "OCR failed") temporarily replace the normal pill content.
- **Capture overlay** maps the dragged rectangle to screenshot pixels by ratio (screenshot is stretched to the overlay), so no DPI arithmetic is needed. It closes the panel first (220 ms) so the panel isn't in the screenshot. Losing focus cancels it.
- **Launcher** now has 4 buttons: Calculator, QR code, Text capture, System monitor (tooltips show the hotkeys).

- **OCR preprocessing changed from PLAN** (PLAN: ×3 if selection < 60 px tall, else ×2; grayscale; invert dark). Measured on real Notepad text (Thai + English, ~11 px glyphs): ×2 and ×3 both scored 88.7 % (character similarity) and misread short Thai words as Latin (`ราคา` → `mm`, `บาท` → `uw`); ×4 + grayscale + **Otsu black/white threshold** scored 99 %. Now: ×4, reduced so the output stays ≤ ~6 MP (big selections stay fast), grayscale, invert if dark, Otsu binarize. Language order stays `tha+eng` (`eng+tha` puts a space between every Thai letter). `OcrPreprocess.ScaleFactor(w, h)` and `OtsuThreshold` are in Core with tests.
- **OCR text cleanup** also drops leading blank lines and collapses runs of blank lines (Tesseract emits them around blocks).
- Known limitation, per PLAN's rule: spaces *between* Thai words are removed too (Tesseract's bogus intra-word spaces can't be told apart from real ones).

M3 verification (2026-11-13):

- [x] `dotnet test` — 170/170 pass (incl. every calculator case in PLAN)
- [x] All three hotkeys owned by Knob (another process can't register them) and work with Notepad in front
- [x] Calculator: hotkey opens with focus in the box; `(1200*7%)+15` shows `= 99` live; Enter → history + "Copied 99"; `1/0` → "Division by zero"; Esc closes
- [x] QR: hotkey prefills clipboard text; Square/Dots/Rounded × colours render with solid finder patterns; Copy → clipboard has a 1024×1024 bitmap + PNG; Save… dialog opens and the panel stays open behind it
- [x] Text capture end to end (hotkey → overlay → drag → pill "Copied N chars" → clipboard), well under 1 s including engine start
- [x] OCR at web-like size (Notepad zoom 150 %): `สวัสดีครับยินดีต้อนรับส Knob / ราคา 100 บาทและ English mixed text 2026 / The quick brown fox jumps over the lazy dog` — only miss: `สู่` → `ส` (thin lower vowel). At 100 % Notepad size `บาท` is still sometimes misread.
- Found while testing: a selection drawn tight against the text cuts off Thai upper vowels/tone marks and hurts accuracy — leave a little margin.
- [ ] **User to check:** scan Square / Dots / Rounded QR codes with a phone
- [ ] **User to check:** OCR on a real web page, and the capture/OCR at 150 % display scaling

## M4

- **Editor works on a copy** (`GridLayout.Clone()`): **Done** applies + saves layout.json; **Back / Esc / panel closing** discards; **Reset** puts the default layout in the editor (still needs Done).
- **Tiles appear as cards** in the editor (title, S/W/L buttons for the tile's `SupportedSizes`, ✕ to hide) rather than live tiles, so their own buttons/text boxes can't fight with dragging.
- **Drag feedback:** the target footprint is drawn while dragging — accent = fits, red = blocked or sticks out; dropping on red leaves the tile where it was. A refused S/W/L click flashes the would-be footprint red.
- **Hidden tiles** = registered tiles not in the layout (System monitor starts hidden). Dragging a hidden chip onto the grid places it at the tile's smallest supported size.
- **Panel is taller while editing** (330 vs 250 DIP) to fit the hidden strip + Reset/Done. The window is now always 330 DIP tall; the unused part is transparent (click-through). On side docks the open panel is centred on the pill.
- **Entry points:** tray → Edit layout, or the small ✎ in the open panel's top-left corner. Edit mode sets `KeepOpen` (panel stays open) and disables drag-out.
- **TileHost re-layout while open** closes the old tiles and opens the new ones, so each tile gets balanced Opened/Closed calls (no double minute-tick subscriptions, monitor polling not left running).
- Core API added: `CanPlace`, `Hide`, `TryShow`, `TryShowAnywhere`, `Clone` (tests incl. PLAN's "hide then show with no free space → false"; overlapping layout.json → default was already covered).

M4 verification (2026-11-13):

- [x] `dotnet test` — 197/197 pass
- [x] ✎ opens edit mode; cards with S/W/L + ✕; hidden strip shows System
- [x] Clock S→W refused while Disk is next to it; after hiding Disk, Clock → W works
- [x] Dragging Timer onto Media: red footprint, snaps back on drop
- [x] Dragging hidden System onto a full grid: red, stays hidden; after hiding Notes: accent footprint, placed as W
- [x] Done → real panel shows the new layout (System monitor live, Clock wide); restart → same layout from layout.json
- [x] Monitor tile on the grid, panel closed: 0.000 s CPU in 60 s (polling stops)
- [x] Back cancels (hidden Timer came back); Reset + Done → layout.json identical to the pre-test default

## Added at the user's request (outside PLAN)

- **Notch position (2026-11-13):** the centered pill covered browser tabs. First version was a tray Left/Center/Right switch; replaced the same day by dragging (below). The tray **Position** menu remains as presets: Top center / Top left (8 DIP gap) / Top right (150 DIP gap, keeps a maximized window's ─ □ ✕ clear) / Left side / Right side.
- **Drag out → floating → dock to an edge (2026-11-13, user's design):**
  - With the panel open, press on an empty area (not a button, text box, scrollbar or clickable row) and drag > 6 DIP → it becomes a **floating panel** (all corners rounded, grip + ✕ header) moved by the system move loop (`DragMove`).
  - Release within 32 DIP of the **left / right / top** edge of the primary monitor → docks there at the drop point (pill centre = cursor position along that edge). An accent outline shows while a release would dock. Bottom edge is not a dock (taskbar).
  - **Floating stays open** (user's choice): hover-out and deactivation don't close it (`KeepOpen`). ✕ or Esc docks it back to the edge it came from and collapses.
  - **Side docks:** the pill is vertical (pill content rotated −90° on the left, +90° on the right), the panel opens sideways from that edge at the drop height (user's choice: where dropped, not centred). Panel is kept fully on screen.
  - Saved as `"Dock": { Edge, Offset, FloatLeft, FloatTop, LastEdge, LastOffset }` (DIP relative to the primary monitor); bad values are repaired (`DockSettings.Normalize`). Snap and clamp maths (`DockGeometry`) are in Core with tests.
  - Verified on screen: drag to middle → floating; ✕ → back to top; drag to left edge (outline shown) → left dock, collapses to a vertical pill, hover reopens; drag to right edge → right dock; drag to top at x≈1055 → top dock there; floating and top positions survive a restart. Idle CPU after: 0.031 s / 60 s.
  - Known limit: docking uses the primary monitor only (PLAN v1); a floating panel can be dropped on another monitor but docks only to the primary one's edges.
- **Git fix:** the default .NET `.gitignore` has `*.app` (macOS bundles), which matched `src/Knob.App` case-insensitively, so no App source was committed before commit "fix: track src/Knob.App". The M0/M1 "feat" commits before it contain Core/tests/docs only; the App sources for both milestones are in that one fix commit. `.gitignore` now un-ignores `/src/Knob.App/`.
