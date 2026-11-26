using System.Text.Json.Serialization;
using Knob.Core.Storage;

namespace Knob.Core.Clipboard;

public sealed class ClipboardItem
{
    public long Id { get; set; }
    public string Text { get; set; } = "";
    public bool Pinned { get; set; }
    public DateTimeOffset CopiedAt { get; set; }

    /// <summary>For a copied image: the PNG's file name in the clipboard images folder (Text is then empty).</summary>
    public string? Image { get; set; }
    /// <summary>Hash of the image pixels, so copying the same picture again doesn't add a duplicate.</summary>
    public string? ImageHash { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    [JsonIgnore]
    public bool IsImage => !string.IsNullOrEmpty(Image);
}

/// <summary>Clipboard history (text and images), newest first. Serialized as clipboard.json; images live as PNG files beside it.</summary>
public sealed class ClipboardHistory : IJsonNormalizable
{
    public const int MaxTextLength = 100_000;

    public List<ClipboardItem> Items { get; set; } = [];

    /// <summary>From settings (ClipboardMaxItems); not stored in the file.</summary>
    [JsonIgnore]
    public int MaxItems { get; set; } = 50;

    /// <returns>true if the list changed.</returns>
    public bool Add(string? text, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength) return false;
        if (Items.Count > 0 && Items[0].Text == text) return false;

        var existing = Items.FindIndex(i => i.Text == text);
        if (existing >= 0)
        {
            var item = Items[existing];
            Items.RemoveAt(existing);
            item.CopiedAt = now;
            Items.Insert(0, item);
            return true;
        }

        Items.Insert(0, new ClipboardItem { Id = NextId(), Text = text, CopiedAt = now });
        Trim();
        return true;
    }

    /// <summary>Add a copied image (already saved as <paramref name="file"/>). The same picture again moves to the top.</summary>
    /// <returns>true if the list changed.</returns>
    public bool AddImage(string hash, string file, int width, int height, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(file)) return false;
        if (Items.Count > 0 && Items[0].ImageHash == hash) return false;

        var existing = Items.FindIndex(i => i.ImageHash == hash);
        if (existing >= 0)
        {
            var item = Items[existing];
            Items.RemoveAt(existing);
            item.CopiedAt = now;
            Items.Insert(0, item);
            return true;
        }

        Items.Insert(0, new ClipboardItem { Id = NextId(), Image = file, ImageHash = hash, Width = width, Height = height, CopiedAt = now });
        Trim();
        return true;
    }

    /// <summary>Image files still referenced (anything else in the images folder can be deleted).</summary>
    [JsonIgnore]
    public IReadOnlyList<string> ReferencedImages => Items.Where(i => i.IsImage).Select(i => i.Image!).ToList();

    /// <summary>Toggles the pin. Pinned items survive the size limit and Clear().</summary>
    public bool Pin(long id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return false;
        item.Pinned = !item.Pinned;
        if (!item.Pinned) Trim();
        return true;
    }

    public bool Remove(long id) => Items.RemoveAll(i => i.Id == id) > 0;

    public void Clear() => Items.RemoveAll(i => !i.Pinned);

    public void Normalize()
    {
        Items ??= [];
        Items.RemoveAll(i => i is null || (string.IsNullOrEmpty(i.Text) && !i.IsImage) || (i.Text?.Length ?? 0) > MaxTextLength);
        foreach (var i in Items) i.Text ??= "";
        var seen = new HashSet<long>();
        foreach (var item in Items)
        {
            if (seen.Add(item.Id)) continue;
            item.Id = NextId();
            seen.Add(item.Id);
        }
    }

    private void Trim()
    {
        while (Items.Count > MaxItems)
        {
            // Never drop the newest item (index 0), even if everything older is pinned.
            var oldestUnpinned = Items.FindLastIndex(i => !i.Pinned);
            if (oldestUnpinned <= 0) break;
            Items.RemoveAt(oldestUnpinned);
        }
    }

    private long NextId() => Items.Where(i => i is not null).Select(i => i.Id).DefaultIfEmpty(0).Max() + 1;
}
