using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Knob.App.Platform;
using Knob.Core;
using Knob.Core.Layout;

namespace Knob.App.Tiles;

/// <summary>Now playing + transport controls. The progress bar ticks once per second only while open and playing.</summary>
public sealed class MediaTile : TileBase
{
    private readonly MediaSessionService _media;
    private readonly IClock _clock;
    private readonly DispatcherTimer _progress;
    private event Action? ProgressTick;
    private bool _open;

    public MediaTile(MediaSessionService media, IClock clock, Dispatcher dispatcher)
    {
        _media = media;
        _clock = clock;
        _progress = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _progress.Tick += (_, _) => ProgressTick?.Invoke();
        _media.Changed += SyncProgressTimer;
    }

    public override string Id => "media";
    public override string Title => "Media";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.L, TileSize.W];

    public override void OnPanelOpened()
    {
        _open = true;
        SyncProgressTimer();
    }

    public override void OnPanelClosed()
    {
        _open = false;
        SyncProgressTimer();
    }

    private void SyncProgressTimer()
    {
        if (_open && _media.Current.IsPlaying) { if (!_progress.IsEnabled) _progress.Start(); }
        else _progress.Stop();
    }

    private static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    public override FrameworkElement CreateView(TileSize size)
    {
        var large = size == TileSize.L;

        var art = new Border
        {
            Width = large ? 52 : 38,
            Height = large ? 52 : 38,
            CornerRadius = new CornerRadius(8),
            Background = Ui.Control,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var title = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Ui.Fg, TextTrimming = TextTrimming.CharacterEllipsis };
        var artist = new TextBlock { FontSize = 11, Foreground = Ui.Subtle, TextTrimming = TextTrimming.CharacterEllipsis };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(artist);
        var info = new DockPanel();
        DockPanel.SetDock(art, Dock.Left);
        info.Children.Add(art);
        info.Children.Add(text);

        var prev = Ui.IconButton("", async (_, _) => await _media.PreviousAsync());
        var play = Ui.IconButton(Ui.IconPlay, async (_, _) => await _media.PlayPauseAsync());
        var next = Ui.IconButton("", async (_, _) => await _media.NextAsync());
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(prev);
        buttons.Children.Add(play);
        buttons.Children.Add(next);

        var root = new DockPanel();

        if (large)
        {
            // progress bar + times
            var bar = new Grid { Height = 4, Margin = new Thickness(0, 10, 0, 2) };
            var done = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
            var left = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            bar.ColumnDefinitions.Add(done);
            bar.ColumnDefinitions.Add(left);
            var track = new Border { Background = Ui.Control, CornerRadius = new CornerRadius(2) };
            Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track);
            bar.Children.Add(new Border { Background = Ui.Accent, CornerRadius = new CornerRadius(2) });
            var pos = Ui.Text("", 9, Ui.Subtle);
            var dur = Ui.Text("", 9, Ui.Subtle);
            var times = new DockPanel();
            DockPanel.SetDock(dur, Dock.Right);
            times.Children.Add(dur);
            times.Children.Add(pos);
            var timeline = new StackPanel();
            timeline.Children.Add(bar);
            timeline.Children.Add(times);

            DockPanel.SetDock(info, Dock.Top);
            DockPanel.SetDock(timeline, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(info);
            root.Children.Add(timeline);
            root.Children.Add(buttons);

            void Progress()
            {
                var s = _media.Current;
                if (!s.HasSession || s.Duration <= TimeSpan.Zero)
                {
                    timeline.Visibility = Visibility.Hidden;
                    return;
                }
                timeline.Visibility = Visibility.Visible;
                var p = s.PositionAt(_clock.Now);
                done.Width = new GridLength(Math.Max(0.0001, p.TotalSeconds), GridUnitType.Star);
                left.Width = new GridLength(Math.Max(0.0001, (s.Duration - p).TotalSeconds), GridUnitType.Star);
                pos.Text = Fmt(p);
                dur.Text = Fmt(s.Duration);
            }

            void RefreshLarge()
            {
                Refresh();
                Progress();
            }

            Ui.WhileLoaded(root,
                () => { _media.Changed += RefreshLarge; ProgressTick += Progress; RefreshLarge(); },
                () => { _media.Changed -= RefreshLarge; ProgressTick -= Progress; });
        }
        else
        {
            buttons.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(buttons, Dock.Right);
            root.Children.Add(buttons);
            root.Children.Add(info);
            Ui.WhileLoaded(root, () => { _media.Changed += Refresh; Refresh(); }, () => _media.Changed -= Refresh);
        }

        return root;

        void Refresh()
        {
            var s = _media.Current;
            if (!s.HasSession)
            {
                title.Text = "Nothing playing";
                artist.Text = "";
                art.Child = null;
            }
            else
            {
                title.Text = string.IsNullOrEmpty(s.Title) ? "Unknown" : s.Title;
                artist.Text = s.Artist;
                art.Child = s.Thumbnail is null ? null : new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = new ImageBrush(s.Thumbnail) { Stretch = Stretch.UniformToFill },
                };
            }
            prev.IsEnabled = s.CanPrevious;
            play.IsEnabled = s.CanPlayPause;
            next.IsEnabled = s.CanNext;
            ((TextBlock)play.Content).Text = s.IsPlaying ? Ui.IconPause : Ui.IconPlay;
        }
    }
}
