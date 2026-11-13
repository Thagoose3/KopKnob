using System.Windows;
using Knob.Core.Layout;

namespace Knob.App.Tiles;

public interface ITile
{
    /// <summary>Stable id used in layout.json, e.g. "timer".</summary>
    string Id { get; }
    string Title { get; }
    IReadOnlyList<TileSize> SupportedSizes { get; }
    FrameworkElement CreateView(TileSize size);
    /// <summary>Start polling/subscriptions that are only needed while visible.</summary>
    void OnPanelOpened();
    /// <summary>Stop everything that isn't needed while hidden.</summary>
    void OnPanelClosed();
}

/// <summary>Convenience base: no-op open/close.</summary>
public abstract class TileBase : ITile
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract IReadOnlyList<TileSize> SupportedSizes { get; }
    public abstract FrameworkElement CreateView(TileSize size);
    public virtual void OnPanelOpened() { }
    public virtual void OnPanelClosed() { }
}
