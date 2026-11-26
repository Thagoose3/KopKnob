using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Knob.App.Tiles;
using Knob.Core.Input;
using Knob.Core.Storage;

namespace Knob.App.Shell;

/// <summary>What the settings view needs from the app. Every change applies at once (theme restarts Knob).</summary>
public interface ISettingsHost
{
    AppSettings Settings { get; }
    void Save();
    void ChangeTheme(string theme);
    /// <summary>Restarts Knob.</summary>
    void SetHardwareRendering(bool on);
    /// <returns>The state actually in effect (registry write can fail).</returns>
    bool SetRunAtStartup(bool on);
    void SetHideOnFullscreen(bool on);
    /// <summary>Alert / indicator settings changed: start or stop watchers, refresh the pill.</summary>
    void ApplyAlerts();
    /// <summary>Unregister global hotkeys while one is being recorded (so the combination reaches us).</summary>
    void SuspendHotkeys();
    /// <summary>Register the hotkeys from settings again; returns the ones that couldn't be registered.</summary>
    IReadOnlyList<string> ApplyHotkeys();
    FrameworkElement TabsEditor();
    /// <summary>Update checks (Settings → About).</summary>
    Knob.App.Platform.UpdateService Updates { get; }
    /// <summary>Download and run the available update (Knob restarts).</summary>
    void InstallUpdate();
    void OpenPage(string id);
    void Close();
}

/// <summary>
/// Settings in the panel: a section list on the left (General, Alerts, Hotkeys, Tabs, About) and the section
/// on the right. Replaces hand-editing settings.json.
/// </summary>
public static class SettingsView
{
    private static readonly (string Id, string Title, string Glyph)[] Sections =
    [
        ("general", "General", ""),
        ("alerts", "Alerts", ""),
        ("hotkeys", "Hotkeys", ""),
        ("tabs", "Tabs", ""),
        ("about", "About", ""),
    ];

    public static FrameworkElement Create(ISettingsHost host, string? section)
    {
        var current = Sections.Any(s => s.Id == section) ? section! : Sections[0].Id;
        var nav = new StackPanel { Width = 128, Margin = new Thickness(0, 0, 12, 0) };
        var body = new Border();

        void Show(string id)
        {
            current = id;
            nav.Children.Clear();
            foreach (var (sid, title, glyph) in Sections)
            {
                var active = sid == current;
                // Fixed width: the button template centres its content, this keeps the labels left-aligned.
                var row = new StackPanel { Orientation = Orientation.Horizontal, Width = 108 };
                row.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.Res<FontFamily>("IconFont"), FontSize = 12, Foreground = active ? Ui.Fg : Ui.Subtle, Width = 22, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(Ui.Text(title, 12, active ? Ui.Fg : Ui.Subtle, active ? FontWeights.SemiBold : FontWeights.Normal));
                var b = new Button
                {
                    Content = row,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 0, 8, 0),
                    Height = 28,
                    Margin = new Thickness(0, 0, 0, 3),
                    Background = active ? Ui.Control : Brushes.Transparent,
                };
                var target = sid;
                b.Click += (_, _) => Show(target);
                nav.Children.Add(b);
            }
            body.Child = id switch
            {
                "alerts" => Alerts(host),
                "hotkeys" => Hotkeys(host),
                "tabs" => Tabs(host),
                "about" => About(host),
                _ => General(host),
            };
        }

        var done = new Button { Content = "Done", Padding = new Thickness(18, 3, 18, 3), Background = Ui.Accent, Foreground = Ui.AccentFg, Margin = new Thickness(0, 6, 0, 0) };
        done.Click += (_, _) => host.Close();
        var left = new DockPanel();
        DockPanel.SetDock(done, Dock.Bottom);
        done.HorizontalAlignment = HorizontalAlignment.Left;
        left.Children.Add(done);
        left.Children.Add(nav);

        var root = new DockPanel { Margin = new Thickness(6, 0, 6, 0) };
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false });
        Pages.Smooth(root);
        Show(current);
        return root;
    }

    // ───────── rows ─────────

    /// <summary>Label (+ grey description) on the left, a control on the right.</summary>
    private static FrameworkElement Row(string label, string? description, FrameworkElement control)
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Ui.Text(label, 12.5));
        if (description is not null)
        {
            var d = Ui.Text(description, 10.5, Ui.Dim);
            d.TextWrapping = TextWrapping.Wrap;
            texts.Children.Add(d);
        }
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(12, 0, 0, 0);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(control);
        row.Children.Add(texts);
        return row;
    }

    private static FrameworkElement Toggle(bool value, Action<bool> changed) => Pages.Toggle(value, changed).Switch;

    private static StackPanel Panel() => new() { Margin = new Thickness(0, 2, 4, 0) };

    // ───────── sections ─────────

    private static FrameworkElement General(ISettingsHost host)
    {
        var s = host.Settings;
        var p = Panel();
        p.Children.Add(Row("Theme", "Changing the theme restarts Knob",
            Pages.Choice<string>([("Default", AppSettings.ThemeDefault), ("Glass", AppSettings.ThemeGlass), ("Scroll", AppSettings.ThemeScroll)],
                s.Theme, host.ChangeTheme)));

        Action<bool>? setStartup = null;
        var (startupSwitch, set) = Pages.Toggle(s.RunAtStartup, on => setStartup?.Invoke(host.SetRunAtStartup(on)));
        setStartup = set; // show what actually happened (the registry write can fail)
        p.Children.Add(Row("Run at startup", "Start Knob when you sign in to Windows", startupSwitch));

        p.Children.Add(Row("Hide in full screen", "Get out of the way of games and full-screen videos",
            Toggle(s.HideOnFullscreen, host.SetHideOnFullscreen)));

        p.Children.Add(Row("Open on hover after", null,
            Pages.Choice<int>([("Instant", 0), ("Short", 120), ("Long", 400)], NearestDelay(s.HoverOpenDelayMs), ms =>
            {
                s.HoverOpenDelayMs = ms;
                host.Save();
                host.ApplyAlerts();
            })));

        p.Children.Add(Row("GPU drawing", "Smoother on some PCs, but ~120 MB more memory and can wake the graphics card. Restarts Knob",
            Toggle(s.HardwareRendering, host.SetHardwareRendering)));
        return p;
    }

    private static int NearestDelay(int ms) => ms <= 40 ? 0 : ms <= 250 ? 120 : 400;

    private static FrameworkElement Alerts(ISettingsHost host)
    {
        var a = host.Settings.Alerts;
        var w = host.Settings.Weather;
        void Changed()
        {
            host.Save();
            host.ApplyAlerts();
        }

        var p = Panel();
        p.Children.Add(Row("Keyboard language", "Show ก ไทย / A EN when you switch",
            Toggle(a.KeyboardLanguage, on => { a.KeyboardLanguage = on; Changed(); })));
        p.Children.Add(Row("Caps Lock on / off", null,
            Toggle(a.CapsLock, on => { a.CapsLock = on; Changed(); })));
        p.Children.Add(Row("Camera & microphone dots", "Green / orange dot while an app uses them",
            Toggle(a.PrivacyDots, on => { a.PrivacyDots = on; Changed(); })));
        p.Children.Add(Row("Weather on the pill", null,
            Toggle(w.ShowOnPill, on => { w.ShowOnPill = on; Changed(); })));
        p.Children.Add(Row("Charged-to alert", "Unplug reminder, for battery health",
            Pages.Choice<int>([("Off", 0), ("80%", 80), ("85%", 85), ("90%", 90)], a.ChargeLimit, v => { a.ChargeLimit = v; Changed(); })));
        p.Children.Add(Row("Device battery low", "Bluetooth mouse, keyboard, headphones",
            Pages.Choice<int>([("Off", 0), ("10%", 10), ("15%", 15), ("20%", 20)], a.DeviceLowBattery, v => { a.DeviceLowBattery = v; Changed(); })));
        return p;
    }

    private static FrameworkElement Hotkeys(ISettingsHost host)
    {
        var h = host.Settings.Hotkeys;
        var status = Ui.Text("Click a box, then press the new combination (with Ctrl, Alt, Shift or Win). Esc cancels.", 10.5, Ui.Dim);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 2, 0, 10);

        FrameworkElement Recorder(Func<string> get, Action<string> set)
        {
            var box = new TextBox
            {
                Text = get(),
                IsReadOnly = true,
                Width = 128,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4, 3, 4, 3),
                Cursor = Cursors.Hand,
                IsReadOnlyCaretVisible = false,
            };
            box.GotKeyboardFocus += (_, _) =>
            {
                host.SuspendHotkeys();
                box.Text = "Press keys…";
            };
            box.LostKeyboardFocus += (_, _) =>
            {
                box.Text = get();
                var failed = host.ApplyHotkeys();
                status.Text = failed.Count == 0 ? "Saved." : "Already used by another app: " + string.Join(", ", failed);
            };
            box.PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                    return; // wait for the actual key
                e.Handled = true;
                var mods = HotkeyModifiers.None;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Control;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;
                if (HotkeyParser.FromKeys(mods, KeyInterop.VirtualKeyFromKey(key)) is not string text)
                {
                    box.Text = "Try with Ctrl / Alt…";
                    return;
                }
                set(text);
                host.Save();
                box.Text = text;
                Keyboard.ClearFocus(); // → LostKeyboardFocus re-registers
            };
            return box;
        }

        // Two columns so all six fit without scrolling.
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        void Add(string label, FrameworkElement recorder)
        {
            var cell = Row(label, null, recorder);
            cell.Margin = new Thickness(0, 0, 18, 8);
            grid.Children.Add(cell);
        }
        Add("Quick search", Recorder(() => h.Search, v => h.Search = v));
        Add("Screenshot to Shelf", Recorder(() => h.Screenshot, v => h.Screenshot = v));
        Add("Colour picker", Recorder(() => h.ColorPicker, v => h.ColorPicker = v));
        Add("Capture text", Recorder(() => h.TextCapture, v => h.TextCapture = v));
        Add("Calculator", Recorder(() => h.Calculator, v => h.Calculator = v));
        Add("QR code", Recorder(() => h.Qr, v => h.Qr = v));

        var p = Panel();
        p.Children.Add(status);
        p.Children.Add(grid);
        return p;
    }

    private static FrameworkElement Tabs(ISettingsHost host)
    {
        var p = new DockPanel();
        var title = Ui.Text("Tick to show a tab, arrows to move it", 10.5, Ui.Dim);
        title.Margin = new Thickness(0, 2, 0, 8);
        DockPanel.SetDock(title, Dock.Top);
        p.Children.Add(title);
        p.Children.Add(host.TabsEditor());
        return p;
    }

    private static FrameworkElement About(ISettingsHost host)
    {
        var u = host.Updates;
        var p = Panel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Ui.Text("Knob", 16, weight: FontWeights.SemiBold));
        var ver = Ui.Text($"  {Knob.App.Platform.UpdateService.Current.ToString(3)}", 12, Ui.Subtle);
        ver.VerticalAlignment = VerticalAlignment.Bottom;
        head.Children.Add(ver);
        p.Children.Add(head);

        // Updates: status line + buttons, refreshed while the section is on screen.
        var status = Ui.Text("", 11.5, Ui.Subtle);
        var check = new Button { Content = "Check for updates", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0) };
        check.Click += (_, _) => _ = u.CheckAsync();
        var install = new Button { Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0), Background = Ui.Accent, Foreground = Ui.AccentFg };
        install.Click += (_, _) => host.InstallUpdate();
        var notes = new Button { Content = "What's new", Padding = new Thickness(12, 3, 12, 3) };
        notes.Click += (_, _) =>
        {
            if (u.Available?.PageUrl is string url && url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(check);
        buttons.Children.Add(install);
        buttons.Children.Add(notes);
        void Refresh()
        {
            status.Text = u.Status.Length > 0 ? u.Status
                : u.Settings.LastCheck is { } last ? $"Checked {last.LocalDateTime:d MMM HH:mm}" : "Not checked yet";
            check.IsEnabled = install.IsEnabled = !u.Busy;
            var available = u.Available is not null;
            install.Visibility = notes.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            if (available) install.Content = $"Update to {u.Available!.Version.ToString(3)}";
        }
        var updates = new StackPanel { Margin = new Thickness(0, 10, 0, 6) };
        updates.Children.Add(status);
        updates.Children.Add(buttons);
        p.Children.Add(updates);
        p.Children.Add(Row("Check automatically", "About once a day, from the project's GitHub releases",
            Toggle(u.Settings.AutoCheck, on => { u.Settings.AutoCheck = on; host.Save(); })));
        Ui.WhileLoaded(p, () => { u.Changed += Refresh; Refresh(); }, () => u.Changed -= Refresh);
        Refresh();

        var data = Ui.Text($"Your data: {KnobPaths.DataDir}", 11, Ui.Dim);
        data.Margin = new Thickness(0, 4, 0, 0);
        data.TextWrapping = TextWrapping.Wrap;
        p.Children.Add(data);
        var open = new Button { Content = "Open data folder", Padding = new Thickness(12, 3, 12, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        open.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", KnobPaths.DataDir) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { }
        };
        p.Children.Add(open);
        return p;
    }
}
