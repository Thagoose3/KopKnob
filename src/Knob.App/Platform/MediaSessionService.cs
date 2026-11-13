using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using WinRtSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using WinRtManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace Knob.App.Platform;

public sealed record MediaSnapshot(
    bool HasSession,
    string Title,
    string Artist,
    ImageSource? Thumbnail,
    bool IsPlaying,
    TimeSpan Position,
    DateTimeOffset PositionUpdatedAt,
    TimeSpan Duration,
    bool CanPrevious,
    bool CanPlayPause,
    bool CanNext)
{
    public static readonly MediaSnapshot Empty = new(false, "", "", null, false, TimeSpan.Zero, DateTimeOffset.MinValue, TimeSpan.Zero, false, false, false);

    /// <summary>Position extrapolated to <paramref name="now"/> while playing.</summary>
    public TimeSpan PositionAt(DateTimeOffset now)
    {
        var p = Position;
        if (IsPlaying && PositionUpdatedAt != DateTimeOffset.MinValue)
        {
            var since = now - PositionUpdatedAt;
            if (since > TimeSpan.Zero) p += since;
        }
        if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
        return p < TimeSpan.Zero ? TimeSpan.Zero : p;
    }
}

/// <summary>
/// Wraps the system media session (what Windows shows in its media flyout). Event-driven only.
/// Every WinRT call is guarded: sessions can disappear at any moment.
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private WinRtManager? _manager;
    private WinRtSession? _session;
    private int _version;

    public MediaSessionService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public MediaSnapshot Current { get; private set; } = MediaSnapshot.Empty;

    /// <summary>Raised on the UI thread.</summary>
    public event Action? Changed;

    public async Task StartAsync()
    {
        try
        {
            _manager = await WinRtManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            Attach(_manager.GetCurrentSession());
        }
        catch (Exception)
        {
            _manager = null; // media API unavailable: tile shows "Nothing playing"
        }
    }

    public void Dispose()
    {
        if (_manager is not null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        Detach();
    }

    public Task PreviousAsync() => Try(s => s.TrySkipPreviousAsync().AsTask());
    public Task PlayPauseAsync() => Try(s => s.TryTogglePlayPauseAsync().AsTask());
    public Task NextAsync() => Try(s => s.TrySkipNextAsync().AsTask());

    private async Task Try(Func<WinRtSession, Task<bool>> action)
    {
        var s = _session;
        if (s is null) return;
        try { await action(s); } catch (Exception) { }
    }

    private void OnCurrentSessionChanged(WinRtManager sender, CurrentSessionChangedEventArgs args)
    {
        WinRtSession? s = null;
        try { s = sender.GetCurrentSession(); } catch (Exception) { }
        _dispatcher.BeginInvoke(() => Attach(s));
    }

    private void Attach(WinRtSession? session)
    {
        Detach();
        _session = session;
        if (session is null)
        {
            Publish(MediaSnapshot.Empty);
            return;
        }
        try
        {
            session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            session.TimelinePropertiesChanged += OnTimelineChanged;
        }
        catch (Exception) { }
        _ = RefreshAllAsync();
    }

    private void Detach()
    {
        var s = _session;
        _session = null;
        if (s is null) return;
        try
        {
            s.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            s.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            s.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        catch (Exception) { }
    }

    private void OnMediaPropertiesChanged(WinRtSession s, MediaPropertiesChangedEventArgs e) =>
        _dispatcher.BeginInvoke(() => { _ = RefreshAllAsync(); });

    private void OnPlaybackInfoChanged(WinRtSession s, PlaybackInfoChangedEventArgs e) =>
        _dispatcher.BeginInvoke(() => Publish(ReadPlaybackAndTimeline(Current)));

    private void OnTimelineChanged(WinRtSession s, TimelinePropertiesChangedEventArgs e) =>
        _dispatcher.BeginInvoke(() => Publish(ReadPlaybackAndTimeline(Current)));

    /// <summary>Title/artist/thumbnail (async), then playback + timeline.</summary>
    private async Task RefreshAllAsync()
    {
        var session = _session;
        if (session is null) return;
        var version = ++_version;

        string title = "", artist = "";
        ImageSource? thumb = null;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            title = props?.Title ?? "";
            artist = props?.Artist ?? "";
            if (props?.Thumbnail is { } reference) thumb = await LoadThumbnailAsync(reference);
        }
        catch (Exception) { }

        if (version != _version || !ReferenceEquals(session, _session)) return; // superseded
        var snap = Current with { HasSession = true, Title = title, Artist = artist, Thumbnail = thumb };
        Publish(ReadPlaybackAndTimeline(snap));
    }

    private MediaSnapshot ReadPlaybackAndTimeline(MediaSnapshot snap)
    {
        var s = _session;
        if (s is null) return MediaSnapshot.Empty;
        snap = snap with { HasSession = true };
        try
        {
            var pb = s.GetPlaybackInfo();
            var c = pb.Controls;
            snap = snap with
            {
                IsPlaying = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                CanPrevious = c.IsPreviousEnabled,
                CanPlayPause = c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled,
                CanNext = c.IsNextEnabled,
            };
        }
        catch (Exception) { }
        try
        {
            var t = s.GetTimelineProperties();
            snap = snap with
            {
                Position = t.Position,
                PositionUpdatedAt = t.LastUpdatedTime,
                Duration = t.EndTime - t.StartTime,
            };
        }
        catch (Exception) { }
        return snap;
    }

    private static async Task<ImageSource?> LoadThumbnailAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            using var source = stream.AsStreamForRead();
            var ms = new MemoryStream();
            await source.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 112;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Publish(MediaSnapshot snap)
    {
        Current = snap;
        Changed?.Invoke();
    }
}
