using System.IO;
using System.Windows;
using Knob.App.Platform;
using Knob.App.Shell;
using Knob.App.Tiles;
using Knob.Core;
using Knob.Core.Layout;
using Knob.Core.Storage;

namespace Knob.App;

/// <summary>Composition root: single instance, settings, window, pill status, tray.</summary>
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
    private JsonStore<LayoutData>? _layoutStore;
    private TileHost? _tileHost;

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

        _settingsStore = new JsonStore<AppSettings>(KnobPaths.Settings);
        _settings = _settingsStore.Load();

        // Registry is the source of truth for autostart; keep settings in sync with it.
        StartupRegistration.RefreshPathIfEnabled();
        _settings.RunAtStartup = StartupRegistration.IsEnabled();

        _controller = new NotchController(_settings, Dispatcher);
        _window = new NotchWindow(_controller);
        _window.Show();

        _pillStatus = new PillStatus(_window, SystemClock.Instance);
        _pillStatus.Start();

        BuildTiles();

        _tray = new TrayIcon(_settings.RunAtStartup);
        _tray.OpenRequested += OnTrayOpen;
        _tray.QuitRequested += () => Shutdown();
        _tray.RunAtStartupToggled += OnRunAtStartupToggled;
    }

    private void BuildTiles()
    {
        var clock = SystemClock.Instance;
        _pomodoro = new PomodoroService(clock, _window!, _pillStatus!);
        _coffee = new CoffeeTile(_window!);
        _notes = new NotesTile(KnobPaths.Notes, Dispatcher);
        _media = new MediaSessionService(Dispatcher);
        _ = _media.StartAsync();

        var registry = new TileRegistry(
        [
            new TimerTile(_pomodoro),
            new MediaTile(_media, clock, Dispatcher),
            _coffee,
            _notes,
            new ClockTile(clock, _pillStatus!),
            new CalendarTile(clock),
            new TodoTile(KnobPaths.Todo),
            new DiskTile(),
        ]);

        _layoutStore = new JsonStore<LayoutData>(KnobPaths.Layout);
        var layout = GridLayout.FromData(_layoutStore.Load());
        if (!File.Exists(KnobPaths.Layout)) SaveLayout(layout); // persist even if we never exit cleanly
        _tileHost = new TileHost(registry, layout);
        _window!.PanelBody = _tileHost;

        _controller!.PanelOpened += (_, _) => _tileHost.OnPanelOpened();
        _controller.PanelClosed += (_, _) => _tileHost.OnPanelClosed();
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
        _window.EnableKeyboardInput();
        // If Windows refused to activate us, nothing would ever close the panel: fall back to hover rules.
        if (!_window.IsActive) _controller.PointerExited();
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
        catch (System.IO.IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notes?.Save();
        _coffee?.Set(false);
        _media?.Dispose();
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
