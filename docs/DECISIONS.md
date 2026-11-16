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

## M5

- **Fullscreen detection uses three event sources, not just the appbar.** PLAN specified `ABN_FULLSCREENAPP` only. It worked in the first test, but later on this machine the shell stopped sending it (even after a fresh start) — likely because always-on-top overlay windows (NVIDIA overlay etc.) make the shell think a fullscreen app is already up. Now: `EVENT_SYSTEM_FOREGROUND` (foreground changed) + `EVENT_OBJECT_LOCATIONCHANGE` hooked only for the foreground window's process and filtered to that window (F11-style resize in place) + `ABN_FULLSCREENAPP` as a hint. Every source just re-checks: the foreground window covers the whole primary monitor, is not the desktop (`Progman`/`WorkerW`), not one of Knob's own windows (capture overlay), and not a normal maximized window with a title bar (those cover the monitor when the taskbar auto-hides). All event-driven; nothing runs while nothing happens.
- Hiding uses `ShowWindow(SW_HIDE / SW_SHOWNOACTIVATE)` and re-asserts topmost on return. A floating panel hides too.
- **Explorer restart:** WinForms `NotifyIcon` re-adds the tray icon on `TaskbarCreated` by itself; the fullscreen watcher re-registers its appbar on the same message. Verified by broadcasting `TaskbarCreated` (not by killing Explorer, which would close the user's folder windows): Knob stayed alive and fullscreen hiding still worked.
- **Crash handling:** `DispatcherUnhandledException` (logged, handled → keeps running), `AppDomain.UnhandledException` (logged), `TaskScheduler.UnobservedTaskException` (logged, observed) → `%APPDATA%\Knob\crash.log`, trimmed to ~1 MB. A tile whose `CreateView` throws shows an "error" tile (since M1).
- **Resume / unlock / RDP connect** re-place the knob (monitors may have changed meanwhile); the minute clock already re-aligns on resume (M0).
- **Publish:** `dotnet publish src/Knob.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish` → `Knob.exe` 24.9 MB (most of it is the Windows SDK/WinRT projection used by the media tile) + `tessdata/` and `x64/`,`x86/` native OCR libraries next to it (not bundled, so Tesseract can load them; `TesseractEnviornment.CustomSearchPath` points at the exe folder). `publish/` is git-ignored.

M5 verification (2026-11-13):

- [x] New fullscreen window → knob hidden; closed → back (3 runs, incl. after a `TaskbarCreated` broadcast)
- [x] Same window going fullscreen in place (F11-style) → hidden; back to normal size → shown
- [x] Normal maximized window with a title bar → knob stays visible
- [x] Published single-file `Knob.exe` runs, reads the same `%APPDATA%\Knob` data, and OCR works from it (captured the desktop label "Recycle Bin" exactly); no crash.log
- [x] **Idle (Task 5.2), published Release build, collapsed 5 min untouched: +0.047 s CPU** (target < 0.3 s). Cumulative every 30 s: 0, 0, 0.016 … 0.016, 0.047 — i.e. the minute tick and nothing else.
  An earlier 5-min run on the Debug build showed +4.2 s; a follow-up minute showed 0.125 s then flat zero, and the knob turned out to have been moved/used during that window — not a background timer.
- [x] Timer/animation audit: the only timer running while collapsed is the minute tick; all others are one-shot or run only while visible/active; the only repeating animation is the 3-blink flash; `CompositionTarget.Rendering` unused.
- [ ] **RAM target missed (accepted per PLAN: "WPF can't go much lower"):** fresh start working set 161 MB, of which **private 89 MB** (the rest is shared .NET/WPF images). After an OCR run the working set stays ~220 MB even after the Tesseract engine is released (native heap isn't returned to the OS right away). Possible follow-up: trim the working set after the engine is released.
- [ ] **User to check:** a real fullscreen video/game; sleep → resume; unplug/replug a monitor; leave it running overnight (no crash, memory not growing)

## Redesign: pages + bottom tab bar (2026-11-16, user request)

The user asked to switch from the movable tile grid to one page at a time with a tab bar along the bottom (inspired by a "dynamic island for Windows" clip they recorded — layout idea only; no name, logo, mascot or visuals copied). Same functions, design change only; no translucent/blur background (agreed: big rendering change for little gain).

- **Panel** (720×280 DIP): header (date left · time + ··· right), the current page, tab bar. The active tab is a capsule with icon + name; others are icons. The last tab is remembered.
- **Pages** reuse the existing tile views: Home (big clock, Coffee, disk, Capture text), **Music** (new full-page view: 150 px cover, big title, progress, controls; background tinted with the cover's average colour), Timer and Calendar (centred, scaled up), Clipboard, Notes, To-do, System (full page), Calc and QR (the M3 tools; QR is rebuilt each time it is shown so it re-reads the clipboard).
- **Only the page on screen** gets `OnPanelOpened/Closed`, so media progress and the system monitor run only while their page is visible. Hotkeys open their page directly (Calc/QR) even if hidden from the tab bar.
- **Layout editing (M4) is replaced by "Customize tabs"** (··· or tray → Edit layout): show/hide tabs and move them left/right; at least one tab always stays visible. Stored in settings.json `"Tabs": { Order, Hidden, Last }` (`TabSettings` in Core, tested). The tile grid, its editor, swap/replace, `layout.json`, the Launcher tile and the tool-page host were removed (they are in git history).
- **Fullscreen detection fix found while testing:** click-through (`WS_EX_TRANSPARENT`) fullscreen overlays — e.g. the Claude desktop app's screen-control overlay, screen recorders, some game overlays — were treated as fullscreen apps and hid the knob. They are ignored now; the new-window and F11-style tests still hide/show correctly.
- Verified on screen: every tab opens its page; Customize shows all 10 tabs with tick boxes and arrows; idle CPU of the published build 0 s in 60 s (private memory ~100 MB).
- **Follow-up (same day):** the big Home clock looked jagged — it was a small view scaled up by a `Viewbox` while the window renders text in WPF "Display" mode (hinted for small sizes). Home now draws the clock at its real size (76 px), and scaled/large content (Home, Music, Timer, Calendar, System) uses `TextFormattingMode.Ideal`. Home was redone as cards (badge icon + title + status): Coffee, **Local Disk — click opens the drive in File Explorer**, Capture text (shows its hotkey).
- **System page** is now four cards with 60-second graphs: CPU (name from the registry), Memory (used of total), **GPU** (3D load summed over all `\GPU Engine(*engtype_3D)\Utilization Percentage` instances, VRAM from `\GPU Adapter Memory(*)\Dedicated Usage` — the counters Task Manager uses; card name and total VRAM from the display-adapter registry key, picking the adapter with the most VRAM), Network (↓ and ↑ with two lines). Sampling still runs only while the page is shown (0 s CPU in 60 s after visiting it). Note: 3D load is summed across GPUs (integrated + discrete) and capped at 100 %.

## Theme: "Sutra scroll" (2026-11-16, user request)

The user wanted a Journey-to-the-West look. After mascot drafts (gourd, chibi and "cool" Monkey King — kept as sketches only, not in the app) they chose a **theme where Knob itself is a sutra scroll**:

- Tray → **Theme → Default / Sutra scroll**, stored as `"Theme"` in settings.json (unknown values → Default; tested). Switching saves and **restarts Knob** (the single-instance mutex is released first), because every view reads its colours once at creation.
- `Themes/Scroll.xaml` is merged after `Dark.xaml` and overrides the palette: lacquer red-brown background, aged-paper text, gold accent, gold trim, gold rod gradients. Control styles in `Dark.xaml` now use `DynamicResource` for brushes so a later theme can override them.
- Look: two gold rods at the ends of the unroll direction (left/right on the top edge or floating; top/bottom on a side dock), 1.2 px gold border, square-ish corners, faint auspicious-cloud outline behind the pages. The pill is the rolled-up scroll.
- **Unroll animation:** two phases with keyframes — opening: the rolled scroll drops to full depth (140 ms), then unrolls along the edge (260 ms) as the rods slide apart; closing: rolls up first (200 ms), then lifts (130 ms). Content fades in during the unroll and out before the roll-up. Finite animations only; nothing runs once settled.
- Verified: collapsed scroll pill renders (gold rods, red silk, time/battery); published build starts with the theme, no crash.log; idle 0.031 s CPU in 30 s. **Not verified by me:** the unroll frames — capturing them while the user was working also captured their browser and moved their cursor, so I stopped and deleted those captures. The user is to check the animation.

### Scroll theme v2 (same day, from the user's reference image)

- Follows the reference: **red silk frame, cream paper inside with a gold rule**, and **rollers with finials that overhang the paper** at both ends. Rollers (`ScrollRod`): red lacquer shaft with gold and jade bands, gold collars, **jade-green pointed finials** (the user asked to mix in a little green); the jade is also the theme's accent (active tab, today, play button) and Wukong's robe.
- **Background painting** on the paper (`WukongArt.xaml`, original design, ~30 % opacity): the Monkey King bust with golden circlet, staff, gourd, and golden clouds along the bottom. Cards/buttons in this theme are slightly translucent so the painting shows through.
- Because the paper is light, the theme's text is ink-dark; the pill (on the red silk) keeps light text. Places that hardcoded colours were fixed to use the theme: Music play button (accent + `AccentForegroundBrush`), Calendar's today, Coffee badge, Music progress track; Music page picks light or theme text by how dark the cover tint is.
- Scroll geometry: Surface background is transparent; the red body is inset by the rod overhang; the panel is 304 DIP tall (vs 280) and the pill 46 DIP (it includes the finials).
- Checked with an **offscreen render** of the real window visuals (a scratch tool referencing Knob.App renders to PNG; nothing shown on the user's screen, no cursor moves): pill and open panel (Home/Calendar) look as intended. Idle CPU after publishing: 0 s in 30 s; no crash.log. The unroll animation itself still needs the user's eye.

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
- **Swap tiles in edit mode (2026-11-13):** dropping a tile onto another swaps them. Both stay inside the cells they used together (no other tile moves) and end up in swapped reading order; sizes adapt — the arrangement that fills those cells best wins, preferring each tile to take the other's size/spot when it supports it. Impossible swaps (e.g. L-only Calendar into a 1-cell spot) show red and change nothing. Dropping a hidden tile onto a shown one replaces it (the old one becomes hidden). `GridLayout.TrySwap` / `TryReplace` in Core with tests. Verified on screen: Coffee→Notes, Timer(L)→Clock(S) (Clock became W, Timer S), Calendar→Disk refused, System→Notes replaced.
- **Git fix:** the default .NET `.gitignore` has `*.app` (macOS bundles), which matched `src/Knob.App` case-insensitively, so no App source was committed before commit "fix: track src/Knob.App". The M0/M1 "feat" commits before it contain Core/tests/docs only; the App sources for both milestones are in that one fix commit. `.gitignore` now un-ignores `/src/Knob.App/`.

## Glass theme (frosted Dynamic Island)

- Third theme, "Glass": the Dynamic Island shape in smoky translucent dark glass (`Themes/Glass.xaml`), a light hairline edge and a soft top sheen (`NotchWindow.UseGlassTheme`).
- No real blur. A layered transparent window only gets acrylic through the undocumented SetWindowCompositionAttribute, which blurs the whole 720 px window rectangle; we would need a window region following the pill↔panel animation every frame (jagged corners, possible stutter). Fake frosted glass costs nothing at idle; real blur can be layered on later.
- The drag snap preview still switches the outline to the accent colour, then back to the glass edge.
