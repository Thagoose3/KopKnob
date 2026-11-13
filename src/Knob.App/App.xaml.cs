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

/// <summary>Composition root: single instance, settings, window, pill status, tiles, tools, hotkeys, tray.</summary>
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
    private JsonStore<LayoutData>? _layoutStore;
    private TileHost? _tileHost;
    private PanelHost? _panelHost;
    private CalculatorTool? _calculator;
    private QrTool? _qr;
    private TesseractOcrService? _ocr;
    private TextCaptureTool? _textCapture;
    private HotkeyManager? _hotkeys;
    private bool _editing;
    private FullscreenWatcher? _fullscreen;

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

        _controller = new NotchController(_settings, Dispatcher);
        _window = new NotchWindow(_controller);
        _window.UseDock(_settings.Dock);
        _window.DockChanged += SaveSettings;
        _window.EditRequested += EnterEditMode;
        _window.EditCancelRequested += () => _panelHost?.Back();
        _window.Show();

        _pillStatus = new PillStatus(_window, SystemClock.Instance);
        _pillStatus.Start();

        _calculator = new CalculatorTool(KnobPaths.CalcHistory);
        _qr = new QrTool(_controller);
        _ocr = new TesseractOcrService();
        _textCapture = new TextCaptureTool(_window, _controller, _ocr);

        BuildTiles();

        _tray = new TrayIcon(_settings.RunAtStartup);
        _tray.OpenRequested += OnTrayOpen;
        _tray.QuitRequested += () => Shutdown();
        _tray.RunAtStartupToggled += OnRunAtStartupToggled;
        _tray.PresetChosen += p => _window.ApplyPreset(p);
        _tray.EditLayoutRequested += () =>
        {
            EnterEditMode();
            ActivateForKeyboard(); // so Esc works and a click elsewhere doesn't leave it hanging
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

    private void BuildTiles()
    {
        var clock = SystemClock.Instance;
        _pomodoro = new PomodoroService(clock, _window!, _pillStatus!);
        _coffee = new CoffeeTile(_window!);
        _notes = new NotesTile(KnobPaths.Notes, Dispatcher);
        _media = new MediaSessionService(Dispatcher);
        _ = _media.StartAsync();
        _clipboard = new ClipboardService(KnobPaths.Clipboard, _settings.ClipboardMaxItems, clock, Dispatcher);
        var monitor = new MonitorTile(clock, Dispatcher);

        var registry = new TileRegistry(
        [
            new TimerTile(_pomodoro),
            new MediaTile(_media, clock, Dispatcher),
            new ClipboardTile(_clipboard),
            _coffee,
            _notes,
            new ClockTile(clock, _pillStatus!),
            new CalendarTile(clock),
            new TodoTile(KnobPaths.Todo),
            new DiskTile(),
            monitor,
            new LauncherTile(
            [
                new LauncherTile.Entry("", $"Calculator ({_settings.Hotkeys.Calculator})", OpenCalculator),
                new LauncherTile.Entry("", $"QR code ({_settings.Hotkeys.Qr})", OpenQr),
                new LauncherTile.Entry("", $"Text capture ({_settings.Hotkeys.TextCapture})", () => _textCapture!.Start()),
                new LauncherTile.Entry("", "System monitor", () => monitor.ShowAsTool(_panelHost!)),
            ]),
        ]);

        _layoutStore = new JsonStore<LayoutData>(KnobPaths.Layout);
        var layout = GridLayout.FromData(_layoutStore.Load());
        if (!File.Exists(KnobPaths.Layout)) SaveLayout(layout); // persist even if we never exit cleanly
        _tileHost = new TileHost(registry, layout);
        _panelHost = new PanelHost(_tileHost);
        _window!.PanelBody = _panelHost;

        _controller!.PanelOpened += (_, _) => _tileHost.OnPanelOpened();
        _controller.PanelClosed += (_, _) =>
        {
            _panelHost.Back(); // next open starts on the tiles; stops tool-page polling
            _tileHost.OnPanelClosed();
        };
    }

    /// <summary>
    /// Layout edit mode on a copy of the layout. The panel stays open (KeepOpen) and gets taller;
    /// Done applies + saves; Back / Esc / panel closing discards.
    /// </summary>
    private void EnterEditMode()
    {
        if (_editing || _tileHost is null || _panelHost is null || _controller is null || _window is null) return;
        _editing = true;

        _controller.RequestOpen();
        _controller.KeepOpen = true;
        _window.Editing = true;

        var editor = new LayoutEditor(_tileHost.Registry, _tileHost.Layout.Clone(), done: layout =>
        {
            _tileHost.Layout = layout;
            SaveLayout(layout);
            _panelHost.Back();
        });

        void OnClosed()
        {
            _panelHost.ToolClosed -= OnClosed;
            _editing = false;
            _window.Editing = false;
            if (!_window.IsFloating) _controller.KeepOpen = false; // floating panels stay open anyway
        }
        _panelHost.ShowTool(editor, "Edit layout");
        _panelHost.ToolClosed += OnClosed;
    }

    private void OpenCalculator() => OpenTool(_calculator!.CreateView(), "Calculator");

    private void OpenQr() => OpenTool(_qr!.CreateView(), "QR code");

    /// <summary>Open the panel straight into a tool page that takes typing.</summary>
    private void OpenTool(FrameworkElement view, string title)
    {
        _controller!.RequestOpen();
        _panelHost!.ShowTool(view, title);
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

    private void SaveLayout(GridLayout layout)
    {
        try { _layoutStore?.Save(layout.ToData()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
        _hotkeys?.Dispose();
        _notes?.Save();
        _coffee?.Set(false);
        _media?.Dispose();
        _clipboard?.Dispose();
        _ocr?.Dispose();
        if (_tileHost is not null) SaveLayout(_tileHost.Layout);
        _tray?.Dispose();
        _pillStatus?.Dispose();
        _window?.Close();
        if (_mutex is not null)
        {
            SaveSettings();
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
