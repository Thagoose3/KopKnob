using System.Windows.Threading;
using Knob.Core.Storage;

namespace Knob.App.Shell;

public enum NotchState { Collapsed, Opening, Open, Closing }

/// <summary>
/// Hover/click state machine: Collapsed → Opening → Open → Closing → Collapsed.
/// The two delay timers run only while the pointer is moving in/out — never while collapsed and idle.
/// <see cref="PanelOpened"/> fires when leaving Collapsed; <see cref="PanelClosed"/> when Collapsed is reached.
/// </summary>
public sealed class NotchController
{
    private readonly DispatcherTimer _openTimer;
    private readonly DispatcherTimer _closeTimer;
    private bool _holdOpen;
    private bool _keepOpen;

    public NotchController(AppSettings settings, Dispatcher dispatcher)
    {
        _openTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(0, settings.HoverOpenDelayMs)),
        };
        _openTimer.Tick += (_, _) => { _openTimer.Stop(); if (PointerInside) BeginOpen(); };

        _closeTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(0, settings.CloseDelayMs)),
        };
        _closeTimer.Tick += (_, _) => { _closeTimer.Stop(); if (!PointerInside && !_holdOpen && !_keepOpen) BeginClose(); };
    }

    public NotchState State { get; private set; } = NotchState.Collapsed;
    public bool PointerInside { get; private set; }

    /// <summary>True while a control in the panel has keyboard focus: hover-out does not close.</summary>
    public bool HoldOpen
    {
        get => _holdOpen;
        set
        {
            if (_holdOpen == value) return;
            _holdOpen = value;
            if (value) _closeTimer.Stop();
            else if (IsExpanded && !PointerInside) { _closeTimer.Stop(); _closeTimer.Start(); } // pointer already left
        }
    }

    /// <summary>
    /// True while something modal needs the panel (a Save dialog; layout editing in M4):
    /// hover-out and deactivation don't close it. Esc still does.
    /// </summary>
    public bool KeepOpen
    {
        get => _keepOpen;
        set
        {
            if (_keepOpen == value) return;
            _keepOpen = value;
            if (value) _closeTimer.Stop();
            else if (IsExpanded && !PointerInside && !_holdOpen) { _closeTimer.Stop(); _closeTimer.Start(); }
        }
    }

    public bool IsExpanded => State is NotchState.Opening or NotchState.Open;

    public event Action<NotchState>? StateChanged;
    public event EventHandler? PanelOpened;
    public event EventHandler? PanelClosed;

    public void PointerEntered()
    {
        PointerInside = true;
        _closeTimer.Stop();
        if (State == NotchState.Collapsed) _openTimer.Start();
        else if (State == NotchState.Closing) BeginOpen();
    }

    public void PointerExited()
    {
        PointerInside = false;
        _openTimer.Stop();
        if (IsExpanded && !_holdOpen && !_keepOpen) { _closeTimer.Stop(); _closeTimer.Start(); }
    }

    public void Clicked()
    {
        _openTimer.Stop();
        if (!IsExpanded) BeginOpen();
    }

    /// <summary>Open now (tray "Open", hotkeys).</summary>
    public void RequestOpen()
    {
        _openTimer.Stop();
        _closeTimer.Stop();
        if (!IsExpanded) BeginOpen();
    }

    /// <summary>Close now (Esc, click outside).</summary>
    public void RequestClose()
    {
        _openTimer.Stop();
        _closeTimer.Stop();
        _holdOpen = false;
        _keepOpen = false;
        if (IsExpanded) BeginClose();
    }

    /// <summary>Called by the window when the current open/close animation finishes.</summary>
    public void AnimationCompleted()
    {
        switch (State)
        {
            case NotchState.Opening:
                SetState(NotchState.Open);
                break;
            case NotchState.Closing:
                SetState(NotchState.Collapsed);
                PanelClosed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void BeginOpen()
    {
        var fromCollapsed = State == NotchState.Collapsed;
        SetState(NotchState.Opening);
        if (fromCollapsed) PanelOpened?.Invoke(this, EventArgs.Empty);
    }

    private void BeginClose() => SetState(NotchState.Closing);

    private void SetState(NotchState s)
    {
        if (State == s) return;
        State = s;
        StateChanged?.Invoke(s);
    }
}
