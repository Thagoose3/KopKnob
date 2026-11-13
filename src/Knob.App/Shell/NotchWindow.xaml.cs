using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Knob.App.Platform;
using Microsoft.Win32;

namespace Knob.App.Shell;

public partial class NotchWindow : Window
{
    private const double PillWidth = 150, PillHeight = 30;
    private const double PanelWidth = 720, PanelHeight = 250;
    private static readonly Duration AnimDuration = new(TimeSpan.FromMilliseconds(180));

    private readonly NotchController _controller;
    private readonly SolidColorBrush _surfaceBrush;
    private IntPtr _hwnd;
    private int _animGeneration;
    private double _pillWidth = PillWidth;
    private DispatcherTimer? _messageTimer;

    public NotchWindow(NotchController controller)
    {
        _controller = controller;
        InitializeComponent();

        Width = PanelWidth;
        Height = PanelHeight;

        // Own (unfrozen) brush so FlashPill can animate it.
        _surfaceBrush = new SolidColorBrush((Color)FindResource("BackgroundColor"));
        Surface.Background = _surfaceBrush;

        Surface.MouseEnter += (_, _) => _controller.PointerEntered();
        Surface.MouseLeave += (_, _) => _controller.PointerExited();
        Surface.MouseLeftButtonUp += (_, _) => _controller.Clicked();

        PreviewMouseDown += OnPreviewMouseDown;
        PreviewKeyDown += OnPreviewKeyDown;
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, _) => SyncHoldOpen()), handledEventsToo: true);
        AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, _) => SyncHoldOpen()), handledEventsToo: true);
        Activated += (_, _) => SyncHoldOpen();
        Deactivated += OnDeactivated;

        _controller.StateChanged += OnStateChanged;
    }

    /// <summary>Content shown inside the expanded panel (TileHost from M1).</summary>
    public object? PanelBody
    {
        get => PanelHost.Content;
        set => PanelHost.Content = value;
    }

    public void SetTime(string text)
    {
        TimeText.Text = text;
        UpdatePillWidth();
    }

    public void SetBattery(int? percent)
    {
        if (percent is int p)
        {
            BatteryText.Text = $"🔋 {p}%";
            BatteryText.Visibility = Visibility.Visible;
        }
        else
        {
            BatteryText.Visibility = Visibility.Collapsed;
        }
        UpdatePillWidth();
    }

    /// <summary>Pomodoro countdown on the pill; null hides it.</summary>
    public void SetPillTimer(string? text)
    {
        TimerText.Text = text ?? "";
        TimerText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        UpdatePillWidth();
    }

    public void SetCoffeeDot(bool on)
    {
        CoffeeDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        UpdatePillWidth();
    }

    /// <summary>
    /// Replace the pill content with a short status ("Reading…", "Copied 42 chars").
    /// With a duration it reverts on its own (one-shot timer); null keeps it until the next call.
    /// </summary>
    public void ShowPillMessage(string? text, TimeSpan? duration = null)
    {
        _messageTimer?.Stop();
        var show = !string.IsNullOrEmpty(text);
        MessageText.Text = text ?? "";
        MessageText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PillNormal.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        UpdatePillWidth();

        if (show && duration is TimeSpan d)
        {
            _messageTimer ??= new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
            _messageTimer.Interval = d;
            _messageTimer.Tick -= OnMessageTimeout;
            _messageTimer.Tick += OnMessageTimeout;
            _messageTimer.Start();
        }
    }

    private void OnMessageTimeout(object? sender, EventArgs e) => ShowPillMessage(null);

    /// <summary>Blink the pill background to the accent colour 3 times, then stop.</summary>
    public void FlashPill()
    {
        var anim = new ColorAnimation((Color)FindResource("AccentColor"), TimeSpan.FromMilliseconds(220))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
            FillBehavior = FillBehavior.Stop,
        };
        _surfaceBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private double PillTargetWidth()
    {
        PillContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(PillWidth, Math.Ceiling(PillContent.DesiredSize.Width) + 28);
    }

    /// <summary>Grow/shrink the collapsed pill to fit its content (animates only when the width really changes).</summary>
    private void UpdatePillWidth()
    {
        var target = PillTargetWidth();
        if (Math.Abs(target - _pillWidth) < 0.5) return;
        _pillWidth = target;
        if (_controller.State == NotchState.Collapsed)
            Surface.BeginAnimation(WidthProperty, new DoubleAnimation(target, AnimDuration));
    }

    /// <summary>
    /// Allow this window to take keyboard focus (user clicked a text input).
    /// Removes WS_EX_NOACTIVATE until the panel closes.
    /// </summary>
    public void EnableKeyboardInput()
    {
        if (_hwnd == IntPtr.Zero) return;
        var ex = NativeMethods.GetExStyle(_hwnd);
        if ((ex & NativeMethods.WS_EX_NOACTIVATE) != 0)
            NativeMethods.SetExStyle(_hwnd, ex & ~NativeMethods.WS_EX_NOACTIVATE);
        Activate();
        SyncHoldOpen();
    }

    private void DisableKeyboardInput()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (IsKeyboardFocusWithin) Keyboard.ClearFocus();
        var ex = NativeMethods.GetExStyle(_hwnd);
        if ((ex & NativeMethods.WS_EX_NOACTIVATE) == 0)
            NativeMethods.SetExStyle(_hwnd, ex | NativeMethods.WS_EX_NOACTIVATE);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        var ex = NativeMethods.GetExStyle(_hwnd);
        ex = (ex | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE) & ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetExStyle(_hwnd, ex);

        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Reposition();
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _controller.StateChanged -= OnStateChanged;
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_DPICHANGED)
        {
            // Let WPF apply the DPI change first, then snap back to top-center of the primary monitor.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, Reposition);
        }
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, Reposition);

    /// <summary>Top-center of the primary monitor, sized in physical pixels for that monitor's DPI.</summary>
    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero) return;

        var mon = NativeMethods.MonitorFromPoint(default, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var mi = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(mon, ref mi)) return;

        double scale = 1.0;
        if (NativeMethods.GetDpiForMonitor(mon, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            scale = dpiX / 96.0;

        int w = (int)Math.Round(PanelWidth * scale);
        int h = (int)Math.Round(PanelHeight * scale);
        int x = mi.rcMonitor.Left + (mi.rcMonitor.Width - w) / 2;
        int y = mi.rcMonitor.Top;

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, w, h, NativeMethods.SWP_NOACTIVATE);
    }

    private void OnStateChanged(NotchState state)
    {
        switch (state)
        {
            case NotchState.Opening: Animate(open: true); break;
            case NotchState.Closing:
                DisableKeyboardInput();
                Animate(open: false);
                break;
        }
    }

    private void Animate(bool open)
    {
        var gen = ++_animGeneration;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (open) PanelContent.Visibility = Visibility.Visible;

        var w = new DoubleAnimation(open ? PanelWidth : _pillWidth, AnimDuration) { EasingFunction = ease };
        var h = new DoubleAnimation(open ? PanelHeight : PillHeight, AnimDuration) { EasingFunction = ease };
        h.Completed += (_, _) =>
        {
            if (gen != _animGeneration) return; // superseded by a newer animation
            if (!open) PanelContent.Visibility = Visibility.Collapsed;
            _controller.AnimationCompleted();
        };

        Surface.BeginAnimation(WidthProperty, w);
        Surface.BeginAnimation(HeightProperty, h);
        PanelContent.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, AnimDuration));
        PillContent.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 0 : 1, AnimDuration));
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindAncestor<TextBoxBase>(d) is not null)
            EnableKeyboardInput();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _controller.RequestClose();
            e.Handled = true;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Clicked outside after typing in the panel.
        if (_controller.KeepOpen) return; // e.g. our own Save dialog took focus
        DisableKeyboardInput();
        _controller.HoldOpen = false;
        if (!_controller.PointerInside) _controller.RequestClose();
    }

    /// <summary>
    /// Keep the panel open only while the user can actually type: window active AND a text box focused.
    /// (Focus on the window itself, or a text box in a window that failed to activate, must not hold it open —
    /// in the latter case no Deactivated would ever arrive to close it.)
    /// </summary>
    private void SyncHoldOpen() =>
        _controller.HoldOpen = IsActive && Keyboard.FocusedElement is TextBoxBase tb && IsAncestorOf(tb);

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}
