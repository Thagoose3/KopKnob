using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Knob.App.Platform;
using Knob.App.Tiles;
using Knob.Core.Shell;
using Microsoft.Win32;

namespace Knob.App.Shell;

/// <summary>Quick placements offered in the tray menu (a fallback to dragging).</summary>
public enum DockPreset { TopCenter, TopLeft, TopRight, LeftSide, RightSide }

/// <summary>
/// The knob. Docked to the top, left or right edge of the primary monitor it is a pill that expands into the
/// panel on hover; dragged out of the open panel it becomes a floating panel that stays open.
/// The window itself is always panel-sized (720×250 DIP); only <c>Surface</c> moves/resizes inside it.
/// </summary>
public partial class NotchWindow : Window
{
    private const double PillThickness = 30, MinPillLength = 150, MinSidePillLength = 100;
    private const double PanelWidth = 720, PanelHeight = 280, WindowHeight = PanelHeight;
    private const double PresetGapLeft = 8, PresetGapRight = 150; // right keeps a maximized window's ─ □ ✕ clear
    private const double DragThreshold = 6;
    private static readonly Duration AnimDuration = new(TimeSpan.FromMilliseconds(180));

    private readonly NotchController _controller;
    private readonly SolidColorBrush _surfaceBrush;
    private IntPtr _hwnd;
    private int _animGeneration;
    private double _pillLength = MinPillLength;
    private DispatcherTimer? _messageTimer;

    private DockSettings _dock = new();
    private NativeMethods.RECT _monitor;  // primary monitor, physical px
    private double _scale = 1;            // physical px per DIP
    private double _pillCentre;           // pill centre along its edge, DIP inside the window
    private Point? _dragPending;
    private bool _dragging;
    private bool _scroll;
    private double _surfaceBorder;
    private const double RodThickness = 11;

    public NotchWindow(NotchController controller)
    {
        _controller = controller;
        InitializeComponent();

        Width = PanelWidth;
        Height = WindowHeight;

        // Own (unfrozen) brush so FlashPill can animate it.
        _surfaceBrush = new SolidColorBrush((Color)FindResource("BackgroundColor"));
        Surface.Background = _surfaceBrush;

        Surface.MouseEnter += (_, _) => _controller.PointerEntered();
        Surface.MouseLeave += (_, _) => { if (!_dragging) _controller.PointerExited(); };
        Surface.MouseLeftButtonUp += (_, _) => _controller.Clicked();
        FloatClose.Click += (_, _) => DockBack(collapse: true);

        PreviewMouseDown += OnPreviewMouseDown;
        PreviewMouseLeftButtonDown += OnPreviewLeftDown;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseLeftButtonUp += (_, _) => _dragPending = null;
        PreviewKeyDown += OnPreviewKeyDown;
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, _) => SyncHoldOpen()), handledEventsToo: true);
        AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, _) => SyncHoldOpen()), handledEventsToo: true);
        Activated += (_, _) => SyncHoldOpen();
        Deactivated += OnDeactivated;
        LocationChanged += (_, _) => { if (_dragging) UpdateSnapPreview(); };

        _controller.StateChanged += OnStateChanged;
    }

    /// <summary>Raised after the user moves the knob (drag/drop or preset); the settings should be saved.</summary>
    public event Action? DockChanged;

    public bool IsFloating => _dock.Edge == DockEdge.Floating;

    /// <summary>Content shown inside the expanded panel.</summary>
    public object? PanelBody
    {
        get => PanelHost.Content;
        set => PanelHost.Content = value;
    }

    /// <summary>
    /// Hide the knob while a fullscreen app is in front (video, game, presentation); bring it back afterwards.
    /// Uses ShowWindow so WPF's own visibility/activation logic isn't involved.
    /// </summary>
    public void SetFullscreenHidden(bool hidden)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (hidden)
        {
            if (!IsFloating) _controller.RequestClose();
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
        }
        else
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
        }
    }

    /// <summary>Re-place the knob for the current monitors (after resume / unlock).</summary>
    public void RefreshPlacement() => Reposition();

    /// <summary>Set before Show(): the (shared, persisted) dock settings this window reads and updates.</summary>
    public void UseDock(DockSettings dock)
    {
        _dock = dock;
        ApplyEdgeLook();
        if (_hwnd != IntPtr.Zero) Reposition();
    }

    // ───────────────────────── pill content ─────────────────────────

    public void SetTime(string text)
    {
        TimeText.Text = text;
        UpdatePillLength();
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
        UpdatePillLength();
    }

    /// <summary>Pomodoro countdown on the pill; null hides it.</summary>
    public void SetPillTimer(string? text)
    {
        TimerText.Text = text ?? "";
        TimerText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        UpdatePillLength();
    }

    public void SetCoffeeDot(bool on)
    {
        CoffeeDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        UpdatePillLength();
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
        UpdatePillLength();

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

    private bool IsSide => _dock.Edge is DockEdge.Left or DockEdge.Right;

    private double MeasurePillLength()
    {
        PillContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // LayoutTransform is applied in DesiredSize, so on the sides the length is the height.
        var content = IsSide ? PillContent.DesiredSize.Height : PillContent.DesiredSize.Width;
        var rods = _scroll ? 2 * RodThickness : 0; // the scroll's rods take room at both ends
        return Math.Max(IsSide ? MinSidePillLength : MinPillLength, Math.Ceiling(content) + 28 + rods);
    }

    /// <summary>Grow/shrink the collapsed pill to fit its content (animates only when the length really changes).</summary>
    private void UpdatePillLength()
    {
        var target = MeasurePillLength();
        if (Math.Abs(target - _pillLength) < 0.5) return;
        _pillLength = target;
        if (_controller.State == NotchState.Collapsed && !IsFloating) SetSurface(SurfaceTarget(expanded: false), animate: true);
    }

    // ───────────────────────── keyboard focus ─────────────────────────

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

    /// <summary>
    /// Keep the panel open only while the user can actually type: window active AND a text box focused.
    /// (Focus on the window itself, or a text box in a window that failed to activate, must not hold it open —
    /// in the latter case no Deactivated would ever arrive to close it.)
    /// </summary>
    private void SyncHoldOpen() =>
        _controller.HoldOpen = IsActive && Keyboard.FocusedElement is TextBoxBase tb && IsAncestorOf(tb);

    // ───────────────────────── window placement ─────────────────────────

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

        if (IsFloating)
        {
            // Restored as floating: it is an open panel from the start.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                _controller.RequestOpen();
                _controller.KeepOpen = true;
            });
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _controller.StateChanged -= OnStateChanged;
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_DPICHANGED && !_dragging)
        {
            // Let WPF apply the DPI change first, then put the knob back in its place.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, Reposition);
        }
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, Reposition);

    private bool ReadMonitor()
    {
        var mon = NativeMethods.MonitorFromPoint(default, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var mi = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(mon, ref mi)) return false;
        _monitor = mi.rcMonitor;
        _scale = NativeMethods.GetDpiForMonitor(mon, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0
            ? dpiX / 96.0
            : 1.0;
        return true;
    }

    private double MonitorWidthDip => _monitor.Width / _scale;
    private double MonitorHeightDip => _monitor.Height / _scale;

    /// <summary>Place the window for the current dock edge (physical pixels on the primary monitor) and lay out Surface.</summary>
    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero || _dragging || !ReadMonitor()) return;

        double winX, winY;
        switch (_dock.Edge)
        {
            case DockEdge.Left:
            case DockEdge.Right:
            {
                var along = _dock.Offset ?? MonitorHeightDip / 2;
                winX = _dock.Edge == DockEdge.Left ? 0 : MonitorWidthDip - PanelWidth;
                winY = DockGeometry.Start(along, WindowHeight, MonitorHeightDip);
                _pillCentre = along - winY;
                break;
            }
            case DockEdge.Floating:
                winX = _dock.FloatLeft;
                winY = _dock.FloatTop;
                break;
            default:
            {
                var along = _dock.Offset ?? MonitorWidthDip / 2;
                winX = DockGeometry.Start(along, PanelWidth, MonitorWidthDip);
                winY = 0;
                _pillCentre = along - winX;
                break;
            }
        }

        int x = _monitor.Left + (int)Math.Round(winX * _scale);
        int y = _monitor.Top + (int)Math.Round(winY * _scale);
        int w = (int)Math.Round(PanelWidth * _scale);
        int h = (int)Math.Round(WindowHeight * _scale);
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, w, h, NativeMethods.SWP_NOACTIVATE);

        _pillLength = MeasurePillLength();
        SetSurface(SurfaceTarget(_controller.IsExpanded || IsFloating), animate: false);
    }

    /// <summary>Surface size and offset (inside the window) for the current edge.</summary>
    private (double W, double H, Thickness M) SurfaceTarget(bool expanded)
    {
        if (expanded || IsFloating)
        {
            var height = PanelHeight;
            // Side docks: centre the panel on the pill; top dock / floating: from the top of the window.
            var top = IsSide ? DockGeometry.Start(_pillCentre, height, WindowHeight) : 0;
            return (PanelWidth, height, new Thickness(0, top, 0, 0));
        }
        return _dock.Edge switch
        {
            DockEdge.Left => (PillThickness, _pillLength,
                new Thickness(0, DockGeometry.Start(_pillCentre, _pillLength, WindowHeight), 0, 0)),
            DockEdge.Right => (PillThickness, _pillLength,
                new Thickness(PanelWidth - PillThickness, DockGeometry.Start(_pillCentre, _pillLength, WindowHeight), 0, 0)),
            _ => (_pillLength, PillThickness,
                new Thickness(DockGeometry.Start(_pillCentre, _pillLength, PanelWidth), 0, 0, 0)),
        };
    }

    private void SetSurface((double W, double H, Thickness M) t, bool animate, Action? completed = null)
    {
        if (!animate)
        {
            Surface.BeginAnimation(WidthProperty, null);
            Surface.BeginAnimation(HeightProperty, null);
            Surface.BeginAnimation(MarginProperty, null);
            Surface.Width = t.W;
            Surface.Height = t.H;
            Surface.Margin = t.M;
            completed?.Invoke();
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var h = new DoubleAnimation(t.H, AnimDuration) { EasingFunction = ease };
        if (completed is not null) h.Completed += (_, _) => completed();
        Surface.BeginAnimation(WidthProperty, new DoubleAnimation(t.W, AnimDuration) { EasingFunction = ease });
        Surface.BeginAnimation(MarginProperty, new ThicknessAnimation(t.M, AnimDuration) { EasingFunction = ease });
        Surface.BeginAnimation(HeightProperty, h);
    }

    /// <summary>Corner rounding, pill orientation and the floating header for the current edge.</summary>
    private void ApplyEdgeLook()
    {
        Surface.CornerRadius = _scroll
            ? new CornerRadius(4)
            : _dock.Edge switch
            {
                DockEdge.Left => new CornerRadius(0, 12, 12, 0),
                DockEdge.Right => new CornerRadius(12, 0, 0, 12),
                DockEdge.Floating => new CornerRadius(12),
                _ => new CornerRadius(0, 0, 12, 12),
            };
        PillContent.LayoutTransform = _dock.Edge switch
        {
            DockEdge.Left => new RotateTransform(-90),
            DockEdge.Right => new RotateTransform(90),
            _ => Transform.Identity,
        };
        FloatBar.Visibility = IsFloating ? Visibility.Visible : Visibility.Collapsed;

        var margin = IsFloating ? new Thickness(12, 20, 12, 12) : new Thickness(12, 10, 12, 12);
        if (_scroll)
        {
            // Rods sit at the two ends of the unroll direction: left/right on the top edge or floating,
            // top/bottom when docked to a side. Keep the content clear of them.
            var rodsVertical = !IsSide;
            foreach (var rod in new[] { RodA, RodB })
            {
                rod.Visibility = Visibility.Visible;
                rod.Background = Ui.Res<Brush>(rodsVertical ? "ScrollRodVertical" : "ScrollRodHorizontal");
                rod.Width = rodsVertical ? RodThickness : double.NaN;
                rod.Height = rodsVertical ? double.NaN : RodThickness;
                rod.HorizontalAlignment = rodsVertical ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
                rod.VerticalAlignment = rodsVertical ? VerticalAlignment.Stretch : VerticalAlignment.Top;
            }
            RodB.HorizontalAlignment = rodsVertical ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            RodB.VerticalAlignment = rodsVertical ? VerticalAlignment.Stretch : VerticalAlignment.Bottom;
            margin = rodsVertical
                ? new Thickness(margin.Left + RodThickness, margin.Top, margin.Right + RodThickness, margin.Bottom)
                : new Thickness(margin.Left, margin.Top + RodThickness, margin.Right, margin.Bottom + RodThickness);
        }
        PanelContent.Margin = margin;
    }

    /// <summary>
    /// Journey-to-the-West "sutra scroll" look: gold rods at both ends, thin gold trim, a faint cloud motif,
    /// and an unroll/roll-up animation. Call once, before Show().
    /// </summary>
    public void UseScrollTheme()
    {
        _scroll = true;
        Surface.BorderBrush = Ui.Res<Brush>("ScrollTrimBrush");
        _surfaceBorder = 1.2;
        Surface.BorderThickness = new Thickness(_surfaceBorder);
        CloudMotif.Visibility = Visibility.Visible;
        ApplyEdgeLook();
        if (_hwnd != IntPtr.Zero) Reposition();
    }

    // ───────────────────────── open / close ─────────────────────────

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
        if (open) PanelContent.Visibility = Visibility.Visible;

        void Done()
        {
            if (gen != _animGeneration) return; // superseded by a newer animation
            if (!open) PanelContent.Visibility = Visibility.Collapsed;
            _controller.AnimationCompleted();
        }

        if (_scroll)
        {
            Unroll(SurfaceTarget(open), open, Done);
            // Content appears while the scroll unrolls and disappears before it rolls up.
            PanelContent.BeginAnimation(OpacityProperty, open
                ? new DoubleAnimation(1, new Duration(UnrollAlong)) { BeginTime = UnrollAcross }
                : new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(120))));
            PillContent.BeginAnimation(OpacityProperty, open
                ? new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(100)))
                : new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(120))) { BeginTime = RollAlong });
            return;
        }

        SetSurface(SurfaceTarget(open), animate: true, completed: Done);
        PanelContent.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, AnimDuration));
        PillContent.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 0 : 1, AnimDuration));
    }

    private static readonly TimeSpan UnrollAcross = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan UnrollAlong = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan RollAlong = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RollAcross = TimeSpan.FromMilliseconds(130);

    /// <summary>
    /// Two-phase scroll animation. Opening: the rolled scroll first drops to full height ("across" the dock edge),
    /// then unrolls along it, the rods sliding apart. Closing: rolls up along the edge first, then lifts away.
    /// Finite keyframe animations — nothing runs once it settles.
    /// </summary>
    private void Unroll((double W, double H, Thickness M) t, bool open, Action completed)
    {
        var w0 = double.IsNaN(Surface.Width) ? Surface.ActualWidth : Surface.Width;
        var h0 = double.IsNaN(Surface.Height) ? Surface.ActualHeight : Surface.Height;
        var m0 = Surface.Margin;

        // "Along" = the direction the scroll unrolls (width on the top edge / floating, height on the sides).
        var alongIsWidth = !IsSide;
        var first = open ? UnrollAcross : RollAlong;
        var total = first + (open ? UnrollAlong : RollAcross);
        var acrossFirst = open;

        // Which property moves in which phase.
        var widthFirst = alongIsWidth ? !acrossFirst : acrossFirst;
        var heightFirst = !widthFirst;

        DoubleAnimationUsingKeyFrames Dim(double from, double to, bool movesFirst)
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = new Duration(total) };
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            if (movesFirst)
            {
                a.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(first), ease));
                a.KeyFrames.Add(new DiscreteDoubleKeyFrame(to, KeyTime.FromTimeSpan(total)));
            }
            else
            {
                a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(first)));
                a.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(total), ease));
            }
            return a;
        }

        // Margin: the horizontal part follows width, the vertical part follows height.
        var mid = new Thickness(widthFirst ? t.M.Left : m0.Left, heightFirst ? t.M.Top : m0.Top, 0, 0);
        var margin = new ThicknessAnimationUsingKeyFrames { Duration = new Duration(total) };
        margin.KeyFrames.Add(new EasingThicknessKeyFrame(mid, KeyTime.FromTimeSpan(first), new CubicEase { EasingMode = EasingMode.EaseOut }));
        margin.KeyFrames.Add(new EasingThicknessKeyFrame(t.M, KeyTime.FromTimeSpan(total), new CubicEase { EasingMode = EasingMode.EaseOut }));

        var width = Dim(w0, t.W, widthFirst);
        width.Completed += (_, _) => completed();
        Surface.BeginAnimation(WidthProperty, width);
        Surface.BeginAnimation(HeightProperty, Dim(h0, t.H, heightFirst));
        Surface.BeginAnimation(MarginProperty, margin);
    }

    // ───────────────────────── drag out / dock ─────────────────────────

    /// <summary>Tray presets: dock to an edge at a fixed spot.</summary>
    public void ApplyPreset(DockPreset preset)
    {
        if (!ReadMonitor()) return;
        var half = MinPillLength / 2;
        var (edge, offset) = preset switch
        {
            DockPreset.TopLeft => (DockEdge.Top, (double?)(PresetGapLeft + half)),
            DockPreset.TopRight => (DockEdge.Top, MonitorWidthDip - PresetGapRight - half),
            DockPreset.LeftSide => (DockEdge.Left, null),
            DockPreset.RightSide => (DockEdge.Right, null),
            _ => (DockEdge.Top, (double?)null),
        };
        DockTo(edge, offset);
        _controller.RequestClose();
    }

    private void OnPreviewLeftDown(object sender, MouseButtonEventArgs e)
    {
        // Press on an empty part of the open panel (not a control) → may become a drag.
        _dragPending = _controller.State == NotchState.Open && !IsInteractive(e.OriginalSource as DependencyObject)
            ? e.GetPosition(this)
            : null;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragPending is not Point start) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragPending = null;
            return;
        }
        var d = e.GetPosition(this) - start;
        if (Math.Abs(d.X) < DragThreshold && Math.Abs(d.Y) < DragThreshold) return;
        _dragPending = null;
        DragOut();
    }

    /// <summary>Turn the open panel into a floating window and let the user move it; on release, dock or stay floating.</summary>
    private void DragOut()
    {
        if (!IsFloating)
        {
            _dock.LastEdge = _dock.Edge;
            _dock.LastOffset = _dock.Offset;
        }
        _dock.Edge = DockEdge.Floating;
        ApplyEdgeLook();
        _controller.KeepOpen = true;

        _dragging = true;
        try
        {
            DragMove(); // system move loop; returns on mouse up
        }
        catch (InvalidOperationException)
        {
            // button already released
        }
        finally
        {
            _dragging = false;
            Surface.BorderThickness = new Thickness(_surfaceBorder);
        }
        FinishDrag();
    }

    private (double X, double Y)? CursorDip()
    {
        if (!ReadMonitor() || !NativeMethods.GetCursorPos(out var p)) return null;
        return ((p.X - _monitor.Left) / _scale, (p.Y - _monitor.Top) / _scale);
    }

    private DockEdge? SnapEdgeAtCursor() =>
        CursorDip() is { } c ? DockGeometry.Snap(c.X, c.Y, 0, 0, MonitorWidthDip, MonitorHeightDip) : null;

    /// <summary>While dragging: accent outline when releasing here would dock to an edge.</summary>
    private void UpdateSnapPreview() =>
        Surface.BorderThickness = new Thickness(SnapEdgeAtCursor() is null ? _surfaceBorder : 2.5);

    private void FinishDrag()
    {
        var c = CursorDip();
        var edge = c is { } p ? DockGeometry.Snap(p.X, p.Y, 0, 0, MonitorWidthDip, MonitorHeightDip) : null;
        if (edge is DockEdge e && c is { } at)
        {
            DockTo(e, e == DockEdge.Top ? at.X : at.Y);
            return;
        }

        // Stay floating where it was dropped.
        if (NativeMethods.GetWindowRect(_hwnd, out var r))
        {
            _dock.FloatLeft = (r.Left - _monitor.Left) / _scale;
            _dock.FloatTop = (r.Top - _monitor.Top) / _scale;
        }
        DockChanged?.Invoke();
    }

    /// <summary>Dock to an edge (the panel stays as it is; it collapses once the pointer leaves).</summary>
    private void DockTo(DockEdge edge, double? offset)
    {
        _dock.Edge = edge;
        _dock.Offset = offset;
        _dock.LastEdge = edge;
        _dock.LastOffset = offset;
        ApplyEdgeLook();
        _controller.KeepOpen = false;
        Reposition();
        SyncPointerAfterMove();
        DockChanged?.Invoke();
    }

    /// <summary>✕ / Esc on a floating panel: go back to the edge it came from.</summary>
    private void DockBack(bool collapse)
    {
        DockTo(_dock.LastEdge, _dock.LastOffset);
        if (collapse) _controller.RequestClose();
    }

    /// <summary>The window jumped under a still mouse: tell the controller whether the pointer is over the panel now.</summary>
    private void SyncPointerAfterMove()
    {
        if (!NativeMethods.GetCursorPos(out var p) || !NativeMethods.GetWindowRect(_hwnd, out var r)) return;
        var inside = p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
        if (inside) _controller.PointerEntered();
        else _controller.PointerExited();
    }

    /// <summary>Controls (and clickable rows) keep their own mouse handling; drags start only from empty areas.</summary>
    private bool IsInteractive(DependencyObject? d)
    {
        while (d is not null && !ReferenceEquals(d, Surface))
        {
            if (d is ButtonBase or TextBoxBase or Thumb or ScrollBar or Selector) return true;
            if (d is FrameworkElement fe && fe.Cursor == Cursors.Hand) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    // ───────────────────────── input ─────────────────────────

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindAncestor<TextBoxBase>(d) is not null)
            EnableKeyboardInput();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (IsFloating) DockBack(collapse: true);
        else _controller.RequestClose();
        e.Handled = true;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Clicked outside after typing in the panel.
        if (_controller.KeepOpen) return; // floating panel, or our own Save dialog took focus
        DisableKeyboardInput();
        _controller.HoldOpen = false;
        if (!_controller.PointerInside) _controller.RequestClose();
    }

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
