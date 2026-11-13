using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Knob.Core.Layout;
using Knob.Core.Storage;
using Knob.Core.Todo;

namespace Knob.App.Tiles;

/// <summary>To-do list saved to todo.json after every change.</summary>
public sealed class TodoTile : TileBase
{
    private readonly JsonStore<TodoList> _store;
    private readonly TodoList _list;
    private event Action? Changed;

    public TodoTile(string path)
    {
        _store = new JsonStore<TodoList>(path);
        _list = _store.Load();
    }

    public override string Id => "todo";
    public override string Title => "To-do";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.W, TileSize.L];

    private void Commit()
    {
        try { _store.Save(_list); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    public override FrameworkElement CreateView(TileSize size)
    {
        var root = new DockPanel();

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var count = Ui.Text("", 10, Ui.Subtle);
        count.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(count, Dock.Right);
        top.Children.Add(count);
        var input = new TextBox { FontSize = 11 };
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (_list.Add(input.Text) is not null) Commit();
            input.Clear();
            e.Handled = true;
        };
        top.Children.Add(Ui.WithHint(input, "Add a task…"));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var items = new StackPanel();
        root.Children.Add(new ScrollViewer
        {
            Content = items,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Focusable = false,
        });

        void Refresh()
        {
            count.Text = $"Done {_list.DoneCount}/{_list.Items.Count}";
            items.Children.Clear();
            foreach (var item in _list.Items)
            {
                var id = item.Id;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
                var del = Ui.IconButton(Ui.IconClose, (_, _) => { _list.Remove(id); Commit(); }, 18, 16, ghost: true);
                ((TextBlock)del.Content).FontSize = 8;
                DockPanel.SetDock(del, Dock.Right);
                row.Children.Add(del);
                var check = new CheckBox
                {
                    IsChecked = item.Done,
                    Content = new TextBlock { Text = item.Text, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11 },
                };
                check.Click += (_, _) => { _list.Toggle(id); Commit(); };
                row.Children.Add(check);
                items.Children.Add(row);
            }
        }

        Ui.WhileLoaded(root, () => { Changed += Refresh; Refresh(); }, () => Changed -= Refresh);
        return root;
    }
}
