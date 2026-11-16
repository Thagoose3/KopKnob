using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    /// <summary>
    /// Full-page "now playing": big cover, big title, progress, controls; the page background is tinted with
    /// the cover's average colour (computed once per cover).
    /// </summary>
    public FrameworkElement CreatePageView()
    {
        var art = new Border { Width = 150, Height = 150, CornerRadius = new CornerRadius(14), Background = Ui.Control, VerticalAlignment = VerticalAlignment.Center };
        var artist = Ui.Text("", 12, Ui.Subtle, FontWeights.SemiBold);
        var title = new TextBlock
        {
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = Ui.Fg,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 70,
            Margin = new Thickness(0, 2, 0, 0),
        };

        var bar = new Grid { Height = 4, Margin = new Thickness(0, 12, 0, 3) };
        var done = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        var left = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        bar.ColumnDefinitions.Add(done);
        bar.ColumnDefinitions.Add(left);
        var track = new Border { Background = Ui.Control, Opacity = 0.7, CornerRadius = new CornerRadius(2) };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { Background = Ui.Fg, CornerRadius = new CornerRadius(2) });
        var pos = Ui.Text("", 10, Ui.Subtle);
        var dur = Ui.Text("", 10, Ui.Subtle);
        var times = new DockPanel();
        DockPanel.SetDock(dur, Dock.Right);
        times.Children.Add(dur);
        times.Children.Add(pos);

        var prev = Ui.IconButton("", async (_, _) => await _media.PreviousAsync(), 36, 30, ghost: true);
        var next = Ui.IconButton("", async (_, _) => await _media.NextAsync(), 36, 30, ghost: true);
        var play = Ui.IconButton(Ui.IconPlay, async (_, _) => await _media.PlayPauseAsync(), 40, 40);
        play.Background = Ui.Accent;
        play.Foreground = Ui.AccentFg;
        foreach (var b in new[] { prev, play, next }) ((TextBlock)b.Content).FontSize = 14;
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        controls.Children.Add(prev);
        controls.Children.Add(play);
        controls.Children.Add(next);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 6, 0) };
        info.Children.Add(artist);
        info.Children.Add(title);
        info.Children.Add(bar);
        info.Children.Add(times);
        info.Children.Add(controls);

        var layout = new DockPanel { Margin = new Thickness(14, 10, 14, 10) };
        DockPanel.SetDock(art, Dock.Left);
        layout.Children.Add(art);
        layout.Children.Add(info);

        var tint = Pages.Smooth(new Border { CornerRadius = new CornerRadius(14), Child = layout });
        ImageSource? tintedFor = null;

        void Progress()
        {
            var s = _media.Current;
            var visible = s.HasSession && s.Duration > TimeSpan.Zero;
            bar.Visibility = times.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
            if (!visible) return;
            var p = s.PositionAt(_clock.Now);
            done.Width = new GridLength(Math.Max(0.0001, p.TotalSeconds), GridUnitType.Star);
            left.Width = new GridLength(Math.Max(0.0001, (s.Duration - p).TotalSeconds), GridUnitType.Star);
            pos.Text = Fmt(p);
            dur.Text = "-" + Fmt(s.Duration - p);
        }

        void Refresh()
        {
            var s = _media.Current;
            artist.Text = s.HasSession ? s.Artist : "";
            title.Text = !s.HasSession ? "Nothing playing" : string.IsNullOrEmpty(s.Title) ? "Unknown" : s.Title;
            art.Child = s.Thumbnail is null ? null : new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new ImageBrush(s.Thumbnail) { Stretch = Stretch.UniformToFill },
            };
            if (!ReferenceEquals(tintedFor, s.Thumbnail))
            {
                tintedFor = s.Thumbnail;
                var cover = s.Thumbnail is BitmapSource bmp ? AverageColor(bmp) : null;
                tint.Background = cover is Color c
                    ? new LinearGradientBrush(Color.FromArgb(0xE6, c.R, c.G, c.B), Color.FromArgb(0x30, c.R, c.G, c.B), 0)
                    : Ui.Res<Brush>("TileBrush");
                // Readable on any theme: light text on a dark cover tint, the theme's text otherwise.
                var darkTint = cover is Color d && (0.299 * d.R + 0.587 * d.G + 0.114 * d.B) < 140;
                title.Foreground = darkTint ? Brushes.White : Ui.Fg;
                artist.Foreground = darkTint ? new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)) : Ui.Subtle;
            }
            prev.IsEnabled = s.CanPrevious;
            play.IsEnabled = s.CanPlayPause;
            next.IsEnabled = s.CanNext;
            ((TextBlock)play.Content).Text = s.IsPlaying ? Ui.IconPause : Ui.IconPlay;
            Progress();
        }

        Ui.WhileLoaded(tint,
            () => { _media.Changed += Refresh; ProgressTick += Progress; Refresh(); },
            () => { _media.Changed -= Refresh; ProgressTick -= Progress; });
        return tint;
    }

    /// <summary>Average colour of a cover, darkened a little so white text stays readable. Null if it can't be read.</summary>
    private static Color? AverageColor(BitmapSource source)
    {
        try
        {
            var small = new TransformedBitmap(source, new ScaleTransform(16.0 / source.PixelWidth, 16.0 / source.PixelHeight));
            var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            var w = bgra.PixelWidth;
            var h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);
            long r = 0, g = 0, b = 0;
            var n = w * h;
            for (var i = 0; i < px.Length; i += 4)
            {
                b += px[i];
                g += px[i + 1];
                r += px[i + 2];
            }
            const double dim = 0.75;
            return Color.FromRgb((byte)(r / n * dim), (byte)(g / n * dim), (byte)(b / n * dim));
        }
        catch (Exception)
        {
            return null;
        }
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
