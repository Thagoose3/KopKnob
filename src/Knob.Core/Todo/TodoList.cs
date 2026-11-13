using Knob.Core.Storage;

namespace Knob.Core.Todo;

public sealed class TodoItem
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}

/// <summary>Serialized as todo.json.</summary>
public sealed class TodoList : IJsonNormalizable
{
    public List<TodoItem> Items { get; set; } = [];

    public int DoneCount => Items.Count(i => i.Done);

    public TodoItem? Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var item = new TodoItem { Id = NextId(), Text = text.Trim() };
        Items.Add(item);
        return item;
    }

    public bool Toggle(int id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return false;
        item.Done = !item.Done;
        return true;
    }

    public bool Remove(int id) => Items.RemoveAll(i => i.Id == id) > 0;

    public void Normalize()
    {
        Items ??= [];
        Items.RemoveAll(i => i is null || string.IsNullOrWhiteSpace(i.Text));
        var seen = new HashSet<int>();
        foreach (var item in Items)
        {
            item.Text = item.Text.Trim();
            if (!seen.Add(item.Id))
            {
                item.Id = NextId();
                seen.Add(item.Id);
            }
        }
    }

    private int NextId() => Items.Count == 0 ? 1 : Items.Where(i => i is not null).Select(i => i.Id).DefaultIfEmpty(0).Max() + 1;
}
