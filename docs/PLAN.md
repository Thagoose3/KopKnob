# Knob — แผนงานหลัก (Master Plan)

> **สำหรับ agent ที่มารับงาน (Opus):** อ่านไฟล์นี้ทั้งไฟล์ก่อนเริ่ม ทำทีละ Milestone ตามลำดับ ห้ามข้าม
> แต่ละ Milestone ต้อง build ผ่าน, test ผ่าน, และผ่าน "เกณฑ์รับงาน" ก่อน commit และไปต่อ
> ถ้าต้องตัดสินใจอะไรที่ไฟล์นี้ไม่ได้ระบุ ให้เลือกทางที่ง่ายที่สุดและจดไว้ใน `docs/DECISIONS.md`
> ห้ามเพิ่มฟีเจอร์นอกขอบเขต (ดูหัวข้อ "ไม่ทำ")

**เป้าหมาย:** แอป Windows แบบ "notch" — แถบเล็กกลางขอบบนจอ ขยายเป็นแผง tile เครื่องมือเล็กๆ ใช้เองคนเดียว เบา ไม่กิน CPU ตอนไม่ได้ใช้

**ต้นแบบอ้างอิง:** วิดีโอ `knobexample.mp4` (แอป Nook) — เอาเฉพาะแนวคิดและ layout ไม่ลอกโค้ด/ชื่อ/โลโก้ เขียนใหม่ทั้งหมด

**Tech stack:** C# 12, .NET 8, WPF, TFM `net8.0-windows10.0.19041.0` (เพื่อเรียก WinRT: media session, OCR), xUnit, QRCoder (NuGet, MIT), Tesseract (NuGet, Apache-2.0, สำหรับ OCR ไทย+อังกฤษ) — ไม่มี dependency อื่น

**เหตุผลที่เลือก WPF:** หน้าต่างโปร่งใส/ไร้ขอบ/topmost ทำได้ตรงๆ และเสถียร, เรียก Win32 + WinRT ได้ครบ, event-driven ได้จริง (WinUI 3 มีปัญหากับหน้าต่างโปร่งใส, Electron/Tauri กิน RAM และ idle CPU มากกว่า)

---

## ขอบเขต

| # | ฟีเจอร์ | Milestone |
|---|---|---|
| 1 | Notch shell (pill เวลา+แบต, hover ขยาย) | M0 |
| 2 | Timer (Pomodoro) | M1 |
| 5 | Coffee (กัน sleep) | M1 |
| 6 | Notes | M1 |
| 15 | Widgets: นาฬิกา, ปฏิทิน, to-do, ดิสก์ | M1 |
| 3 | Media (now playing + ปุ่มควบคุม) | M2 |
| 4 | Clipboard history | M2 |
| 14 | System monitor | M2 |
| 11 | Calculator `Ctrl+Alt+K` | M3 |
| 12 | QR code `Ctrl+Alt+Q` | M3 |
| 7 | Text capture / OCR `Ctrl+Alt+T` | M3 |
| 16 | จัด layout เอง (ลาก + ขนาด S/W/L) | M4 |
| 18 | Idle ~0% CPU + ซ่อนตอน fullscreen | กฎตั้งแต่ M0, ตรวจวัดจริง M5 |

**ไม่ทำ (YAGNI):** Color picker, Wallpaper/ธีมตามวอลเปเปอร์, Phone link, Voice memo, Plugin SDK, หลายจอ (v1 ใช้จอหลักเท่านั้น), installer/auto-update, cloud sync, telemetry

---

## ข้อกำหนดรวม (ทุก task ต้องทำตาม)

- รองรับ Windows 10 build 19041 ขึ้นไป และ Windows 11
- Per-Monitor DPI v2 (`app.manifest`) — ทุกพิกัด Win32 ต้องแปลง DPI
- **กฎ idle (ฟีเจอร์ 18):** ห้ามมี timer/polling ที่รันตอน notch หุบ ยกเว้น 1 ตัว: นาฬิกา tick นาทีละครั้ง (ตั้งให้ตรงขอบนาที) ทุกอย่างอื่นต้องเป็น event (clipboard listener, media events, power events) Tile ที่ต้อง poll (system monitor) เริ่มเมื่อแผงเปิด และหยุดเมื่อแผงปิด
- ห้ามมี animation ที่วนไม่จบ (`RepeatBehavior.Forever`) ตอนหุบ
- ข้อมูลทั้งหมดเก็บที่ `%APPDATA%\Knob\` เป็น JSON (`System.Text.Json`) เขียนแบบ atomic (เขียน `.tmp` แล้ว `File.Move` ทับ) ไฟล์เสีย/ไม่มี = ใช้ค่า default ห้าม crash
- Single instance (named mutex `Knob.SingleInstance`)
- Logic ที่ไม่แตะ UI/OS อยู่ใน `Knob.Core` และต้องมี unit test; โค้ดที่แตะ OS ห่อหลัง interface
- ไม่ต้องใช้สิทธิ์ admin ไม่มี network call ใดๆ
- UI: ธีมมืดชุดเดียว, มุมโค้ง 12px, accent `#7C5CFF`, ฟอนต์ Segoe UI Variable (fallback Segoe UI), ข้อความ UI เป็นภาษาอังกฤษ
- Commit ย่อยๆ ต่อ task ข้อความแบบ `feat: …` / `fix: …` / `test: …`

**เครื่องที่ต้องมี:** Windows + .NET 8 SDK (`dotnet --version` ≥ 8.0) + git

---

## โครงสร้างไฟล์

```
Knob.sln
src/
  Knob.Core/                      net8.0 — logic ล้วน ไม่มี WPF/Win32
    Layout/   GridLayout.cs, TilePlacement.cs, TileSize.cs
    Timer/    PomodoroTimer.cs
    Calc/     ExpressionEvaluator.cs, CalcHistory.cs
    Clipboard/ClipboardHistory.cs
    Todo/     TodoList.cs
    Monitor/  CpuUsageCalculator.cs, RateCalculator.cs
    Ocr/      ThaiTextCleaner.cs
    Storage/  JsonStore.cs, AppSettings.cs
    IClock.cs
  Knob.App/                       WPF
    App.xaml(.cs)                 composition root, mutex, tray
    Shell/    NotchWindow.xaml(.cs), NotchController.cs, TrayIcon.cs
    Tiles/    ITile.cs, TileHost.xaml(.cs), TileRegistry.cs
              TimerTile, MediaTile, ClipboardTile, CoffeeTile, NotesTile,
              ClockTile, CalendarTile, TodoTile, DiskTile, MonitorTile,
              LauncherTile (ปุ่มเปิด Calc/QR/OCR)
    Tools/    CalculatorView, QrView, CaptureOverlayWindow, OcrService.cs
    Platform/ NativeMethods.cs, HotkeyManager.cs, ClipboardListener.cs,
              FullscreenWatcher.cs, PowerStatus.cs, KeepAwake.cs,
              MediaSessionService.cs, SystemMetrics.cs, ScreenCapture.cs
    Themes/   Dark.xaml
    app.manifest
tests/
  Knob.Core.Tests/                xUnit
docs/
  PLAN.md  DECISIONS.md
```

## Interface กลาง (ชื่อและ signature ที่ทุก task ต้องใช้ตรงกัน)

```csharp
// Knob.Core
public enum TileSize { S, W, L }            // S=1x1, W=2x1, L=2x2 (cols x rows)
public record TilePlacement(string TileId, int Col, int Row, TileSize Size);
public sealed class GridLayout {            // grid 8 คอลัมน์ x 3 แถว
    public const int Columns = 8, Rows = 3;
    public IReadOnlyList<TilePlacement> Placements { get; }
    public bool TryMove(string tileId, int col, int row);   // false ถ้าชน/หลุดขอบ
    public bool TryResize(string tileId, TileSize size);
    public static GridLayout Default();
}
public interface IClock { DateTimeOffset Now { get; } }
public sealed class JsonStore<T> where T : new() {
    public JsonStore(string path);
    public T Load();                        // ไฟล์หาย/เสีย -> new T()
    public void Save(T value);              // atomic
}

// Knob.App
public interface ITile {
    string Id { get; }                      // เช่น "timer", "media"
    string Title { get; }
    IReadOnlyList<TileSize> SupportedSizes { get; }
    FrameworkElement CreateView(TileSize size);
    void OnPanelOpened();                   // เริ่ม poll/subscribe ที่ต้องใช้ตอนเห็น
    void OnPanelClosed();                   // หยุดทุกอย่างที่ไม่จำเป็น
}
public interface IToolHost {                // แผงเปลี่ยนเป็นหน้าเครื่องมือเต็มแผง (Calc/QR)
    void ShowTool(FrameworkElement view, string title);
    void Back();
}
```

ไฟล์ข้อมูลใน `%APPDATA%\Knob\`: `settings.json`, `layout.json`, `notes.txt`, `todo.json`, `clipboard.json`, `calc-history.json`

---

## M0 — Shell (ฟีเจอร์ 1 + โครง 18)

**Task 0.1 Scaffold:** สร้าง solution + 3 โปรเจกต์ตามโครงสร้าง, `app.manifest` (PerMonitorV2), `.gitignore`, `git init`. `dotnet build` และ `dotnet test` ผ่าน

**Task 0.2 `JsonStore<T>` + `AppSettings`** (Core, TDD)
- `AppSettings`: `HoverOpenDelayMs=120`, `CloseDelayMs=350`, `HideOnFullscreen=true`, `RunAtStartup=false`, `Hotkeys { Calculator="Ctrl+Alt+K", Qr="Ctrl+Alt+Q", TextCapture="Ctrl+Alt+T" }`, `ClipboardMaxItems=50`
- Test: round-trip; ไฟล์ไม่มี → default; ไฟล์ JSON เสีย → default ไม่ throw; Save ไม่ทิ้ง `.tmp`

**Task 0.3 `NotchWindow`**
- `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`, `ShowInTaskbar=False`, `ResizeMode=NoResize`; ex-style `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` (ไม่โผล่ Alt+Tab, ไม่แย่ง focus ตอน hover)
- ตำแหน่ง: กึ่งกลางแนวนอน ชิดขอบบนของจอหลัก; คำนวณใหม่เมื่อ `WM_DISPLAYCHANGE` / `WM_DPICHANGED` / `SystemEvents.DisplaySettingsChanged`
- สถานะหุบ (pill): ~150×30 DIP, มุมล่างโค้ง แสดง `HH:mm` และ `🔋 NN%` (เครื่องไม่มีแบต → ซ่อนส่วนแบต)
- สถานะขยาย (panel): ~720×250 DIP, animate ขนาด+opacity 180ms ease-out
- เปิด: เมาส์อยู่บน pill ≥ `HoverOpenDelayMs` หรือคลิก; ปิด: เมาส์ออกจากแผง ≥ `CloseDelayMs` หรือกด Esc; ถ้า control ในแผงมี keyboard focus (กำลังพิมพ์ notes) ไม่ปิดจนกว่าจะคลิกนอกแผง
- เมื่อผู้ใช้คลิก control ที่ต้องพิมพ์ ให้ถอด `WS_EX_NOACTIVATE` ชั่วคราวแล้ว `Activate()`; ใส่กลับตอนปิดแผง
- `NotchController`: state machine `Collapsed → Opening → Open → Closing` ยิง `PanelOpened`/`PanelClosed`

**Task 0.4 เวลา + แบตบน pill**
- เวลา: `DispatcherTimer` ตั้ง interval ถึงขอบนาทีถัดไป แล้วทุก 60s (ตัวเดียวที่อนุญาตตอนหุบ)
- แบต: `SystemInformation.PowerStatus` อ่านตอน start + เมื่อ `SystemEvents.PowerModeChanged` + ทุกครั้งที่นาที tick (ไม่ตั้ง timer เพิ่ม)

**Task 0.5 Tray + single instance + autostart**
- Tray icon (`NotifyIcon`): เมนู `Open`, `Edit layout` (disabled จนถึง M4), `Run at startup` (toggle → `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Knob`), `Quit`
- เปิดซ้ำ → instance ใหม่ออกเงียบๆ

**เกณฑ์รับงาน M0:** เปิดแอปแล้วเห็น pill กลางบน, hover แล้วขยาย/หุบลื่น, ไม่โผล่ taskbar/Alt+Tab, ไม่แย่ง focus จากแอปที่พิมพ์อยู่, เปลี่ยน scaling 100%→150% แล้วตำแหน่งยังถูก, Task Manager แสดง CPU 0% ตอนหุบทิ้งไว้ 1 นาที

---

## M1 — Tile host + tile ง่าย (2, 5, 6, 15)

**Task 1.1 `GridLayout`** (Core, TDD)
- Default layout (col,row,size): timer(0,0,L) media(2,0,L) clipboard(4,0,L) clock(6,0,W) calendar(6,1,W) coffee(0,2,W) notes(2,2,W) todo(4,2,W) disk(6,2,S) launcher(7,2,S) — monitor ไม่อยู่ใน default เปิดจาก launcher (ปรับได้ถ้าที่ไม่พอ ให้จดใน DECISIONS)
- Test: default ไม่มี tile ซ้อนกัน; `TryMove` ไปทับ tile อื่น → false และไม่เปลี่ยน state; ขยับหลุดขอบ → false; `TryResize` เป็น L ตอนชนขอบ → false; serialize round-trip

**Task 1.2 `TileHost` + `TileRegistry`:** วาด tile ตาม `GridLayout` ลง `Grid` 8×3, เรียก `OnPanelOpened/Closed` ของทุก tile ตาม `NotchController`, โหลด/เซฟ `layout.json`; tile id ที่ไม่รู้จักใน layout → ข้าม

**Task 1.3 Timer (2)** — `PomodoroTimer` (Core, TDD, ใช้ `IClock`)
- `Duration` default 25:00, `AddMinutes(±1)` ช่วง 1–180 นาที, `Start()`, `Pause()`, `Reset()`, `Remaining`, event `Finished`
- คิดเวลาจาก timestamp ไม่ใช่นับ tick (เครื่อง sleep แล้วกลับมาต้องถูก)
- UI: วงแหวน progress + `mm:ss` + ปุ่ม − ▶/⏸ + reset; อัปเดตทุก 1s **เฉพาะตอนแผงเปิด**; ตอนหุบใช้ one-shot timer ถึงเวลาจบเท่านั้น; ระหว่างรัน pill แสดงเวลาที่เหลือ (อัปเดตตาม tick นาที + แสดงวินาทีเมื่อ < 1 นาที)
- จบ: `SystemSounds.Asterisk` + pill กะพริบ 3 ครั้งแล้วหยุด
- Test: start→เลื่อน clock 10 นาที→Remaining=15:00; pause แล้วเลื่อน clock ไม่ลด; เลื่อนเกิน → Finished ยิงครั้งเดียว; AddMinutes ต่ำกว่า 1 → clamp

**Task 1.4 Coffee (5):** `KeepAwake.Set(bool)` → `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)` / คืนเป็น `ES_CONTINUOUS`; ต้องเรียกจาก thread เดิมเสมอ (UI thread); ปิดอัตโนมัติตอน quit; สถานะไม่จำข้าม restart (เริ่มเป็น off เสมอ); เปิดอยู่ → pill มีจุดเล็กบอก

**Task 1.5 Notes (6):** `TextBox` หลายบรรทัด, auto-save ลง `notes.txt` (UTF-8) แบบ debounce 500ms + เซฟทันทีตอนแผงปิด/quit

**Task 1.6 Widgets (15)**
- Clock: analog หรือ digital + วันที่; อัปเดตเฉพาะตอนแผงเปิด
- Calendar: เดือนปัจจุบัน ไฮไลต์วันนี้ ปุ่มเลื่อนเดือน; เริ่มสัปดาห์ตาม `CultureInfo.CurrentCulture`
- To-do: `TodoList` (Core, TDD) — `Add(text)`, `Toggle(id)`, `Remove(id)`, เซฟ `todo.json`; ข้อความว่าง/ช่องว่างล้วน → ไม่เพิ่ม; tile แสดง "ทำแล้ว n/m"
- Disk: `DriveInfo` ของไดรฟ์ระบบ, อ่านตอน `OnPanelOpened` เท่านั้น

**เกณฑ์รับงาน M1:** ทุก tile ข้างบนใช้งานได้, ปิดแอปเปิดใหม่ notes/todo/layout อยู่ครบ, เปิด Coffee แล้ว `powercfg /requests` (admin) เห็น Knob, ปิดแล้วหาย

---

## M2 — Tile ที่คุยกับระบบ (3, 4, 14)

**Task 2.1 Media (3):** `MediaSessionService` ห่อ `GlobalSystemMediaTransportControlsSessionManager`
- Subscribe `CurrentSessionChanged`, `MediaPropertiesChanged`, `PlaybackInfoChanged`, `TimelinePropertiesChanged` (event ล้วน ไม่ poll)
- แสดง title, artist, thumbnail, ตำแหน่ง/ความยาว; ปุ่ม prev / play-pause / next ผ่าน `TrySkipPreviousAsync` ฯลฯ; ปุ่ม disable ตาม `Controls.IsXxxEnabled`
- progress bar ขยับเองทุก 1s เฉพาะตอนแผงเปิดและกำลังเล่น
- ไม่มี session → แสดง "Nothing playing"; ทุก WinRT call ครอบ try/catch (session หายกลางคันได้)

**Task 2.2 Clipboard (4)**
- `ClipboardHistory` (Core, TDD): `Add(text)` — ซ้ำกับรายการบนสุด → ไม่เพิ่ม; ซ้ำกับรายการเก่า → ย้ายขึ้นบนสุด; เกิน `ClipboardMaxItems` → ตัดตัวเก่าสุดที่ไม่ได้ pin; `Pin(id)`, `Remove(id)`, `Clear()` (เก็บ pinned); ข้อความยาว > 100,000 ตัวอักษร → ไม่เก็บ
- `ClipboardListener`: `AddClipboardFormatListener` + `WM_CLIPBOARDUPDATE` บน message-only window; เก็บเฉพาะ text (v1)
- **ความเป็นส่วนตัว:** ข้ามถ้า clipboard มี format `ExcludeClipboardContentFromMonitorProcessing` หรือ `CanIncludeInClipboardHistory`=0 (password manager ใช้สัญญาณนี้)
- `Clipboard.GetText` ล้มเหลวได้ (แอปอื่นล็อกอยู่) → retry สูงสุด 5 ครั้ง ห่าง 50ms แล้วทิ้ง
- คลิกรายการ = copy กลับ (ต้องไม่ถูกนับเป็นรายการใหม่ซ้ำ); เซฟ `clipboard.json` debounce 1s; มี toggle "Pause" ใน tile

**Task 2.3 System monitor (14)**
- Core (TDD): `CpuUsageCalculator.Compute(prevIdle, prevKernel, prevUser, idle, kernel, user) → double %` (kernel รวม idle อยู่แล้ว), `RateCalculator` (bytes/s จาก delta, counter ถอยหลัง → 0)
- `SystemMetrics`: `GetSystemTimes`, `GlobalMemoryStatusEx`, `NetworkInterface.GetIPv4Statistics()` รวมทุก interface ที่ Up และไม่ใช่ loopback
- Poll ทุก 1s **เฉพาะระหว่าง `OnPanelOpened`–`OnPanelClosed`**; แสดง CPU %, RAM %, ↓/↑ ความเร็วเน็ต, กราฟเส้น CPU 60 จุดล่าสุด
- ห้ามใช้ `PerformanceCounter` (ช้าตอน init และกินทรัพยากร)

**เกณฑ์รับงาน M2:** เล่นเพลงใน Spotify/เบราว์เซอร์แล้ว tile แสดงและคุมได้; copy 3 ข้อความแล้วขึ้นตามลำดับ; copy รหัสจาก password manager แล้วไม่ถูกเก็บ; ค่า CPU ใกล้เคียง Task Manager (±5%); หุบแผงแล้ว CPU กลับเป็น 0%

---

## M3 — เครื่องมือ + hotkey (11, 12, 7)

**Task 3.1 `HotkeyManager`:** `RegisterHotKey` บน message-only window, parse สตริง `"Ctrl+Alt+K"` จาก settings (parser อยู่ Core + test: ตัวพิมพ์เล็กใหญ่, ช่องว่าง, คีย์ไม่รู้จัก → null); ลงทะเบียนไม่ได้ (ชนแอปอื่น) → ไม่ crash, แจ้งผ่าน tray balloon ครั้งเดียว; unregister ตอน quit

**Task 3.2 Calculator (11)**
- `ExpressionEvaluator.TryEvaluate(string, out decimal result, out string error)` (Core, TDD, recursive descent, ใช้ `decimal`)
- รองรับ `+ - * / ^ ( )`, unary minus, ทศนิยม, `%` (หลังตัวเลข = หาร 100), ยอมรับ `×` `÷` และ `,` เป็นตัวคั่นหลักพัน
- Test: `1+2*3`=7; `(1+2)*3`=9; `-2^2`=-4; `2^3^2`=512; `50%`=0.5; `0.1+0.2`=0.3; `1/0` → error "Division by zero"; `2+` → error; สตริงว่าง → error; overflow → error ไม่ throw
- UI: ช่องพิมพ์ + ผลลัพธ์สด, Enter = บันทึกลง history + copy ผลลัพธ์, history 20 รายการ (`calc-history.json`) คลิกเพื่อเอากลับมาใช้
- Hotkey เปิดแผงตรงเข้าหน้านี้พร้อม focus ช่องพิมพ์

**Task 3.3 QR code (12)**
- ใช้ `QRCoder` สร้าง matrix แล้ววาดเองด้วย WPF `DrawingVisual` (เพื่อทำสไตล์)
- ตัวเลือก: Style = Square / Dots / Rounded; Color = 5 สีสำเร็จรูป (`#000000`, `#7C5CFF`, `#14B8A6`, `#EC4899`, `#EF4444`); ECC ระดับ M; พื้นหลังขาวเสมอ + quiet zone 4 โมดูล
- ช่องข้อความ: เปิดมาใส่ข้อความจาก clipboard ให้ถ้าเป็น text ≤ 500 ตัวอักษร; ข้อความยาวเกินความจุ QR → แสดง error ไม่ crash
- ปุ่ม Copy (PNG 1024×1024 ลง clipboard) และ Save… (PNG)
- **ต้อง verify:** decode QR ทุกสไตล์ด้วยมือถือจริงได้ (สไตล์ Dots ต้องไม่ทำ finder pattern เพี้ยน — finder 3 มุมวาดแบบทึบเสมอ)

**Task 3.4 Text capture / OCR (7)**
- Hotkey → `CaptureOverlayWindow`: borderless topmost คลุม virtual screen ทั้งหมด, จับภาพจอก่อน (`ScreenCapture` ด้วย GDI `BitBlt`) แล้วแสดงภาพนิ่งหรี่ 40%, ลากเลือกสี่เหลี่ยม, Esc/คลิกขวา = ยกเลิก
- **ต้องอ่านได้ทั้งไทยและอังกฤษปนกัน** → ใช้ Tesseract ไม่ใช้ Windows OCR (ไม่แน่ใจว่า Windows OCR รองรับไทย และต้องให้ผู้ใช้ติดตั้ง language pack เอง)
  - NuGet `Tesseract` (charlesw, Apache-2.0) + ไฟล์ `tha.traineddata` และ `eng.traineddata` จาก `tesseract-ocr/tessdata_fast` วางใน `tessdata/` ข้าง exe (copy-to-output)
  - Engine ใช้ภาษา `"tha+eng"`, `EngineMode.LstmOnly`, PageSegMode `Auto`
  - สร้าง engine ครั้งแรกตอนใช้งานครั้งแรก (lazy) แล้ว cache ไว้; ปล่อย (Dispose) หลังไม่ใช้ 5 นาที เพื่อคืน RAM (~50–80 MB)
  - รันบน background thread (`Task.Run`) ห้ามบล็อก UI; ระหว่างรันแสดง "Reading…" บน pill
- `OcrService.RecognizeAsync(byte[] png) → Task<string>` ห่อหลัง interface `IOcrService` (เผื่อเปลี่ยน engine ทีหลัง)
- Preprocess ก่อน OCR: แปลงเป็น grayscale; ภาพที่ความสูงตัวอักษรน่าจะเล็ก (กรอบสูง < 60px) → ขยาย 3 เท่า ไม่งั้นขยาย 2 เท่า (Tesseract อ่านตัวเล็กบนจอได้แย่); ถ้าพื้นมืด (ค่าเฉลี่ยความสว่าง < 128) → invert สี
- Post-process: ตัดช่องว่างที่ Tesseract แทรกระหว่างอักษรไทยติดกัน (regex ลบ space ที่อยู่ระหว่างอักขระช่วง `฀-๿` ทั้งสองฝั่ง) — ฟังก์ชันนี้อยู่ Core: `ThaiTextCleaner.Clean(string) → string` พร้อม test: `"สวัส ดี ครับ"` → `"สวัสดีครับ"`; `"Hello world"` ไม่เปลี่ยน; `"ราคา 100 บาท"` ไม่เปลี่ยน (space ข้างตัวเลขคงไว้); `"ไทย English"` ไม่เปลี่ยน
- ผลลัพธ์: copy ลง clipboard + pill แสดง "Copied N chars" 2 วินาที; ไม่เจอข้อความ → "No text found"; ไฟล์ traineddata หาย → "OCR data missing" ไม่ crash
- DPI: พิกัดที่ลากต้องแปลงเป็น physical pixel ให้ตรงกับภาพที่จับ (ทดสอบที่ scaling 150%)

**เกณฑ์รับงาน M3:** hotkey ทั้ง 3 ทำงานจากแอปใดก็ได้; คำนวณตาม test; QR ทุกสไตล์สแกนติด; OCR ข้อความไทย, อังกฤษ และไทยปนอังกฤษ บนหน้าเว็บได้ถูก (ไม่มีช่องว่างแปลกๆ กลางคำไทย) ที่ scaling 100% และ 150%; จับภาพ+OCR กรอบขนาดย่อหน้าเสร็จภายใน 2 วินาที

---

## M4 — แก้ layout (16)

- เข้า edit mode จาก tray `Edit layout` หรือปุ่มเล็กมุมแผง; แผงค้างเปิดระหว่างแก้
- ลาก tile → snap เข้า cell; ตำแหน่งที่วางไม่ได้ (ชน/หลุดขอบ) แสดงเงาสีแดงและเด้งกลับ (ใช้ `GridLayout.TryMove`)
- แต่ละ tile มีปุ่ม S / W / L เฉพาะขนาดที่ `SupportedSizes` รองรับ (`TryResize`)
- ซ่อน/แสดง tile ได้ (รายการ tile ที่ซ่อนอยู่ด้านล่างแผง ลากกลับเข้ามาได้)
- ปุ่ม Done = เซฟ `layout.json`; ปุ่ม Reset = `GridLayout.Default()`
- Test เพิ่มใน Core: ซ่อนแล้วแสดง tile กลับเมื่อไม่มีที่ว่าง → false; layout.json ที่มี tile ซ้อนกัน (แก้มือ) → โหลดแล้ว fallback เป็น default

**เกณฑ์รับงาน M4:** ย้าย/ปรับขนาด/ซ่อน tile แล้วปิดเปิดแอป layout คงเดิม; ไม่มีทางทำให้ tile ซ้อนกันหรือหลุดแผง

---

## M5 — Idle, fullscreen, เก็บงาน (18)

**Task 5.1 `FullscreenWatcher`:** ลงทะเบียน appbar (`SHAppBarMessage(ABM_NEW)`) เพื่อรับ `ABN_FULLSCREENAPP` (event ไม่ poll) → ซ่อน notch เมื่อมีแอป fullscreen บนจอหลัก และกลับมาเมื่อออก; ปิดได้ด้วย `HideOnFullscreen=false`; unregister ตอน quit

**Task 5.2 ตรวจ idle จริง**
- วัด: หุบทิ้งไว้ 5 นาที ดู CPU time ที่เพิ่ม (`Get-Process Knob | select CPU` ก่อน/หลัง) — เป้า: เพิ่ม < 0.3 วินาที ใน 5 นาที (เฉลี่ย < 0.1%)
- ไล่เช็ก: ไม่มี `DispatcherTimer` อื่นรันตอนหุบ, ไม่มี animation ค้าง, `CompositionTarget.Rendering` ไม่ถูก subscribe, media progress timer หยุด
- RAM เป้า < 120 MB working set (WPF ทำต่ำกว่านี้มากไม่ได้ — ยอมรับ)
- บันทึกผลวัดจริงลง `docs/DECISIONS.md`

**Task 5.3 ความทนทาน:** global handler (`DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`) → เขียน `%APPDATA%\Knob\crash.log` แล้วพยายามทำงานต่อ; tile ที่ `CreateView` throw → แสดง tile "error" แทน ไม่ล้มทั้งแผง; ทดสอบ: ถอด/เสียบจอ, sleep/resume, explorer.exe restart (tray icon ต้องกลับมา — ฟัง `TaskbarCreated`), lock/unlock

**Task 5.4 Publish:** `dotnet publish src/Knob.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true` → `publish/Knob.exe`; เขียน `README.md` (วิธี build, hotkey, ที่เก็บข้อมูล)

**เกณฑ์รับงาน M5:** เปิดวิดีโอ/เกม fullscreen แล้ว notch หาย ออกแล้วกลับมา; ผลวัด idle ผ่านเป้า; รันข้ามคืนไม่ crash ไม่ memory โตเรื่อยๆ

---

## จุดเสี่ยงที่ต้องระวังตอน review

1. **Focus:** hover เปิดแผงต้องไม่แย่ง focus จากแอปที่กำลังพิมพ์ แต่พิมพ์ใน Notes/Calc ได้ — สลับ `WS_EX_NOACTIVATE` ให้ถูกจังหวะ
2. **DPI ผสม:** ตำแหน่ง notch และพิกัด OCR ที่ scaling ≠ 100%
3. **Clipboard loop:** คลิก copy กลับจาก history ต้องไม่สร้างรายการซ้ำ และ `GetText` ที่ล้มเหลวต้องไม่ crash
4. **Sleep/resume:** Pomodoro ต้องนับจาก timestamp; timer ขอบนาทีต้อง re-align หลัง resume (`PowerModeChanged.Resume`)
5. **ไฟล์ข้อมูลเสีย/แก้มือ:** ทุก store ต้อง fallback เป็น default
6. **Media session หายกลางคัน:** ทุก call ไป WinRT ต้องกัน exception

---

## วิธีสั่งงาน Opus

ทำทีละ Milestone ต่อ session (context ไม่บวม review ง่าย) ใช้ prompt นี้:

```
อ่าน docs/PLAN.md ทั้งไฟล์ แล้วทำ Milestone M<n> ให้ครบทุก task ตามลำดับ
- Logic ใน Knob.Core ทำแบบ TDD (เขียน test ให้ fail ก่อน)
- จบแต่ละ task: dotnet build + dotnet test ต้องผ่าน แล้ว commit
- จบ Milestone: ไล่เช็ก "เกณฑ์รับงาน" ทีละข้อ รายงานว่าข้อไหน verify แล้วด้วยวิธีใด
  ข้อไหนต้องให้คนลองเอง (เช่น สแกน QR ด้วยมือถือ) ให้ระบุชัดเจน
- ห้ามทำเกินขอบเขต Milestone นี้ ห้ามแตะฟีเจอร์ในหัวข้อ "ไม่ทำ"
- การตัดสินใจที่แผนไม่ได้ระบุ ให้จดลง docs/DECISIONS.md
```

ลำดับ: M0 → M1 → M2 → M3 → M4 → M5 (M2 กับ M3 สลับกันได้ ที่เหลือห้ามสลับ)
