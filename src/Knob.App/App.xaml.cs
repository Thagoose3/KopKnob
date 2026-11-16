using System.IO;
using System.Windows;
using Knob.App.Platform;
using Knob.App.Shell;
using Knob.App.Tiles;
using Knob.App.Tools;
using Knob.Core;
using Knob.Core.Input;
using Knob.Core.Layout;
using Knob.Core.Storage;

namespace Knob.App;

/// <summary>Composition root: single instance, settings, window, pill status, pages, tools, hotkeys, tray.</summary>
public partial class App : Application
{
    private const string MutexName = "Knob.SingleInstance";

    private Mutex? _mutex;
    private JsonStore<AppSettings>? _settingsStore;
    private AppSettings _settings = new();
    private NotchController? _controller;
    private NotchWindow? _window;
    private PillStatus? _pillStatus;
    private TrayIcon? _tray;
    private PomodoroService? _pomodoro;
    private CoffeeTile? _coffee;
    private NotesTile? _notes;
    private MediaSessionService? _media;
    private ClipboardService? _clipboard;
    private PageHost? _pages;
    private CalculatorTool? _calculator;
    private QrTool? _qr;
    private TesseractOcrService? _ocr;
    private TextCaptureTool? _textCapture;
    private HotkeyManager? _hotkeys;
    private FullscreenWatcher? _fullscreen;
    private BluetoothMonitor? _bluetooth;
    private WeatherService? _weather;
    private DeviceAlerts? _alerts;
    private KeyboardWatcher? _keyboard;
    private PrivacyWatcher? _privacy;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Another instance is running: leave quietly.
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        InstallCrashHandlers();

        _settingsStore = new JsonStore<AppSettings>(KnobPaths.Settings);
        _settings = _settingsStore.Load();

        // Registry is the source of truth for autostart; keep settings in sync with it.
        StartupRegistration.RefreshPathIfEnabled();
        _settings.RunAtStartup = StartupRegistration.IsEnabled();

        // Theme colours must be in place before any window or view reads them.
        var scrollTheme = _settings.Theme == AppSettings.ThemeScroll;
        var glassTheme = _settings.Theme == AppSettings.ThemeGlass;
        if (scrollTheme)
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Scroll.xaml", UriKind.Relative) });
        if (glassTheme)
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Glass.xaml", UriKind.Relative) });

        _controller = new NotchController(_settings, Dispatcher);
        _window = new NotchWindow(_controller);
        if (scrollTheme) _window.UseScrollTheme();
        if (glassTheme) _window.UseGlassTheme();
        _window.UseDock(_settings.Dock);
        _window.DockChanged += SaveSettings;
        _window.Show();

        _pillStatus = new PillStatus(_window, SystemClock.Instance);
        _pillStatus.Start();

        _calculator = new CalculatorTool(KnobPaths.CalcHistory);
        _qr = new QrTool(_controller);
        _ocr = new TesseractOcrService();
        _textCapture = new TextCaptureTool(_window, _controller, _ocr);

        BuildPages();

        _tray = new TrayIcon(_settings.RunAtStartup, _settings.Theme);
        _tray.ThemeChosen += ChangeTheme;
        _tray.OpenRequested += OnTrayOpen;
        _tray.QuitRequested += () => Shutdown();
        _tray.RunAtStartupToggled += OnRunAtStartupToggled;
        _tray.PresetChosen += p => _window.ApplyPreset(p);
        _tray.EditLayoutRequested += () =>
        {
            _controller.RequestOpen();
            _pages!.ShowCustomize();
            ActivateForKeyboard(); // so a click elsewhere closes it
        };

        RegisterHotkeys();

        if (_settings.HideOnFullscreen)
        {
            _fullscreen = new FullscreenWatcher();
            _fullscreen.FullscreenChanged += on => _window.SetFullscreenHidden(on);
        }

        // Monitors can change while asleep / locked (docking station, RDP): put the knob back in place.
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume) Dispatcher.BeginInvoke(() => _window?.RefreshPlacement());
    }

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect
            or Microsoft.Win32.SessionSwitchReason.RemoteConnect)
            Dispatcher.BeginInvoke(() => _window?.RefreshPlacement());
    }

    /// <summary>Log unexpected errors to crash.log and keep running where that is possible.</summary>
    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write("UI thread", args.Exception);
            args.Handled = true; // keep the knob alive
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashLog.Write(args.IsTerminating ? "Background thread (terminating)" : "Background thread", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write("Unobserved task", args.Exception);
            args.SetObserved();
        };
    }

    /// <summary>
    /// The panel: one page at a time with a bottom tab bar. Pages reuse the tile views; only the page on screen
    /// gets OnPanelOpened/Closed (so the media progress / system monitor run only while their page is shown).
    /// </summary>
    private void BuildPages()
    {
        var clock = SystemClock.Instance;
        _pomodoro = new PomodoroService(clock, _window!, _pillStatus!);
        _coffee = new CoffeeTile(_window!);
        _notes = new NotesTile(KnobPaths.Notes, Dispatcher);
        _media = new MediaSessionService(Dispatcher);
        _ = _media.StartAsync();
        _clipboard = new ClipboardService(KnobPaths.Clipboard, _settings.ClipboardMaxItems, clock, Dispatcher);

        var timer = new TimerTile(_pomodoro);
        var media = new MediaTile(_media, clock, Dispatcher);
        var clipboard = new ClipboardTile(_clipboard);
        var clockTile = new ClockTile(clock, _pillStatus!);
        var calendar = new CalendarTile(clock);
        var todo = new TodoTile(KnobPaths.Todo);
        var disk = new DiskTile();
        var monitor = new MonitorTile(clock, Dispatcher);

        _bluetooth = new BluetoothMonitor(Dispatcher);
        _bluetooth.Start();
        var controls = new ControlsTile(_bluetooth, Dispatcher);
        _weather = new WeatherService(_settings.Weather);
        var weather = new WeatherTile(_weather, SaveSettings);
        var shelf = new ShelfTile();
        var launch = new LaunchTile(() => _controller!.RequestClose(), keep => _controller!.KeepOpen = keep);

        PageDef[] pages =
        [
            new("home", "Home", Ui.IconHome, () => HomePage.Create(clockTile, _coffee, disk, _settings.Hotkeys.TextCapture, () => _textCapture!.Start()), [clockTile, _coffee, disk]),
            new("music", "Music", Ui.IconMusic, media.CreatePageView, [media]),
            new("controls", "Controls", Ui.IconControls, () => controls.CreateView(TileSize.L), [controls]),
            new("weather", "Weather", Ui.IconWeather, () => weather.CreateView(TileSize.L), [weather]),
            new("shelf", "Shelf", Ui.IconShelf, () => shelf.CreateView(TileSize.L), [shelf]),
            new("launch", "Launch", Ui.IconLaunch, () => launch.CreateView(TileSize.L), [launch]),
            new("timer", "Timer", Ui.IconTimer, () => Pages.Centered(timer.CreateView(TileSize.L), 1.35), [timer]),
            new("clipboard", "Clipboard", Ui.IconClipboard, () => Pages.Padded(clipboard.CreateView(TileSize.L)), [clipboard]),
            new("notes", "Notes", Ui.IconNotes, () => Pages.Padded(_notes.CreateView(TileSize.L)), [_notes]),
            new("todo", "To-do", Ui.IconTodo, () => Pages.Padded(todo.CreateView(TileSize.L)), [todo]),
            new("calendar", "Calendar", Ui.IconCalendar, () => Pages.Centered(calendar.CreateView(TileSize.L), 1.25), [calendar]),
            new("calc", "Calc", Ui.IconCalculator, _calculator!.CreateView, []),
            new("qr", "QR", Ui.IconQr, _qr!.CreateView, [], Recreate: true), // re-read the clipboard each time
            new("system", "System", Ui.IconSystem, () => Pages.Padded(monitor.CreateView(TileSize.L)), [monitor]),
        ];

        _pages = new PageHost(pages, _settings.Tabs, clock, _pillStatus!, SaveSettings);
        _window!.PanelBody = _pages;

        _controller!.PanelOpened += (_, _) => _pages.OnPanelOpened();
        _controller.PanelClosed += (_, _) => _pages.OnPanelClosed();

        // The knob is a volume knob: mouse wheel on the collapsed pill, or on the header row when open.
        _window.KnobWheel += d =>
        {
            if (controls.Nudge(d) is int p) _window.ShowPillMessage(VolumeText(p), TimeSpan.FromMilliseconds(1200));
        };
        _pages.HeaderWheel += d =>
        {
            if (controls.Nudge(d) is int p) _pages.Flash(VolumeText(p));
        };

        // Files dragged onto the knob: park them on the shelf (or pin them when the Launch page is showing).
        _window.FilesDragEntered += () =>
        {
            if (_pages.CurrentId is not ("launch" or "shelf")) _pages.Show("shelf");
        };
        _window.FilesDropped += files =>
        {
            if (_pages.CurrentId == "launch") launch.Add(files);
            else
            {
                shelf.Add(files);
                _pages.Show("shelf");
            }
        };

        // Weather on the pill; refreshed (at most every 30 min) on the pill's minute tick.
        _weather.Changed += UpdatePillWeather;
        _pillStatus!.MinuteTick += () => _ = _weather.RefreshAsync();
        _ = _weather.RefreshAsync();

        // The pill is hidden while the panel is open: messages go to the header there.
        void Say(string text, TimeSpan duration)
        {
            if (_controller!.IsExpanded) _pages!.Flash(text, duration);
            else _window!.ShowPillMessage(text, duration);
        }
        var alerts = _settings.Alerts;
        _alerts = new DeviceAlerts(_window, _bluetooth, _pillStatus!, alerts, text => Say(text, TimeSpan.FromSeconds(3)));

        // Input language (TH/EN) and Caps Lock, shown briefly.
        if (alerts.KeyboardLanguage || alerts.CapsLock)
        {
            _keyboard = new KeyboardWatcher(Dispatcher);
            if (alerts.KeyboardLanguage)
                _keyboard.LanguageChanged += lang =>
                {
                    if (Knob.Core.Devices.KeyboardLanguage.Label(lang) is string label) Say(label, TimeSpan.FromMilliseconds(900));
                };
            if (alerts.CapsLock)
                _keyboard.CapsLockChanged += on => Say(on ? "⇪ CAPS LOCK" : "caps lock off", TimeSpan.FromMilliseconds(900));
            _keyboard.Start();
        }

        // Camera / microphone in use: dots on the pill, and a short message when an app starts using one.
        if (alerts.PrivacyDots)
        {
            _privacy = new PrivacyWatcher(Dispatcher);
            IReadOnlyList<string> cam = [], mic = [];
            _privacy.Changed += () =>
            {
                _window.SetPrivacy(_privacy.CameraApps, _privacy.MicrophoneApps);
                var newCam = _privacy.CameraApps.Except(cam).ToList();
                var newMic = _privacy.MicrophoneApps.Except(mic).ToList();
                if (newCam.Count > 0) Say($"\U0001F4F7 Camera on · {string.Join(", ", newCam)}", TimeSpan.FromSeconds(3));
                else if (newMic.Count > 0) Say($"\U0001F399 Microphone on · {string.Join(", ", newMic)}", TimeSpan.FromSeconds(3));
                cam = _privacy.CameraApps;
                mic = _privacy.MicrophoneApps;
            };
            _privacy.Start();
        }
    }

    private static string VolumeText(int percent) => percent == 0 ? "\U0001F507 Muted" : $"\U0001F50A {percent}%";

    private void UpdatePillWeather() =>
        _window?.SetPillWeather(_settings.Weather.ShowOnPill ? WeatherTile.PillText(_weather?.Report) : null);

    private void OpenCalculator() => OpenPage("calc");

    private void OpenQr() => OpenPage("qr");

    /// <summary>Open the panel straight onto a page that takes typing (hotkeys).</summary>
    private void OpenPage(string id)
    {
        _controller!.RequestOpen();
        _pages!.Show(id);
        ActivateForKeyboard();
    }

    /// <summary>Take keyboard focus; if Windows refuses, fall back to hover rules so the panel can't get stuck.</summary>
    private void ActivateForKeyboard()
    {
        _window!.EnableKeyboardInput();
        if (!_window.IsActive && !_controller!.PointerInside) _controller.PointerExited();
    }

    private void RegisterHotkeys()
    {
        _hotkeys = new HotkeyManager();
        var failed = new List<string>();
        void Add(string name, string combo, Action action)
        {
            var hk = HotkeyParser.Parse(combo);
            if (hk is null || !_hotkeys.Register(hk, action)) failed.Add($"{name} ({combo})");
        }
        Add("Calculator", _settings.Hotkeys.Calculator, OpenCalculator);
        Add("QR code", _settings.Hotkeys.Qr, OpenQr);
        Add("Text capture", _settings.Hotkeys.TextCapture, () => _textCapture!.Start());

        if (failed.Count > 0)
            _tray?.ShowWarning("Knob: hotkey not available", "Already used or invalid: " + string.Join(", ", failed));
    }

    private void OnTrayOpen()
    {
        if (_controller is null || _window is null) return;
        _controller.RequestOpen();
        // Pointer is in the tray, not over the panel: activate so a click elsewhere (Deactivated) closes it.
        ActivateForKeyboard();
    }

    private void OnRunAtStartupToggled(bool enabled)
    {
        if (_settings.RunAtStartup == enabled) return;
        if (!StartupRegistration.Set(enabled))
        {
            if (_tray is not null) _tray.RunAtStartupChecked = !enabled; // revert; re-entry is a no-op
            return;
        }
        _settings.RunAtStartup = enabled;
        SaveSettings();
    }

    /// <summary>
    /// Themes are applied at startup (every view reads its colours once), so switching saves the choice and
    /// restarts Knob. The single-instance mutex is released first so the new process can take it.
    /// </summary>
    private void ChangeTheme(string theme)
    {
        if (theme == _settings.Theme) return;
        _settings.Theme = theme;
        SaveSettings();
        _notes?.Save();

        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
        }
        try
        {
            if (Environment.ProcessPath is string exe)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            CrashLog.Write("Restart after theme change", ex);
        }
        Shutdown();
    }

    private void SaveSettings()
    {
        try { _settingsStore?.Save(_settings); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        _fullscreen?.Dispose();
        _alerts?.Dispose();
        _keyboard?.Dispose();
        _privacy?.Dispose();
        _bluetooth?.Dispose();
        _hotkeys?.Dispose();
        _notes?.Save();
        _coffee?.Set(false);
        _media?.Dispose();
        _clipboard?.Dispose();
        _ocr?.Dispose();
        _tray?.Dispose();
        _pillStatus?.Dispose();
        _window?.Close();
        if (_settingsStore is not null) SaveSettings();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
