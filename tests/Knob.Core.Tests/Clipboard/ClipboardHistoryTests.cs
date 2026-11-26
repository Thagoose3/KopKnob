using Knob.Core.Clipboard;
using Knob.Core.Storage;

namespace Knob.Core.Tests.Clipboard;

public class ClipboardHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private static ClipboardHistory New(int max = 50) => new() { MaxItems = max };

    private static List<string> Texts(ClipboardHistory h) => h.Items.Select(i => i.Text).ToList();

    // ───────── images ─────────

    [Fact]
    public void AddImage_StoresFileAndSize_NewestFirst()
    {
        var h = New();
        h.Add("hello", T0);
        Assert.True(h.AddImage("abc123", "abc123.png", 1920, 1080, T0.AddSeconds(1)));
        var top = h.Items[0];
        Assert.True(top.IsImage);
        Assert.Equal("abc123.png", top.Image);
        Assert.Equal((1920, 1080), (top.Width, top.Height));
        Assert.Equal("", top.Text);
        Assert.False(h.Items[1].IsImage);
    }

    [Fact]
    public void AddImage_SameHash_MovesToTopWithoutDuplicate()
    {
        var h = New();
        h.AddImage("aaa", "aaa.png", 10, 10, T0);
        h.Add("text", T0.AddSeconds(1));
        Assert.True(h.AddImage("bbb", "bbb.png", 10, 10, T0.AddSeconds(2)));
        Assert.True(h.AddImage("aaa", "aaa-again.png", 10, 10, T0.AddSeconds(3)));
        Assert.Equal(3, h.Items.Count);
        Assert.Equal("aaa.png", h.Items[0].Image); // keeps the file it already had
        Assert.False(h.AddImage("aaa", "x.png", 10, 10, T0.AddSeconds(4))); // already on top
    }

    [Fact]
    public void ReferencedImages_ListsFilesStillInHistory()
    {
        var h = New(max: 2);
        h.AddImage("a", "a.png", 1, 1, T0);
        h.AddImage("b", "b.png", 1, 1, T0.AddSeconds(1));
        h.Add("t", T0.AddSeconds(2)); // pushes a.png out
        Assert.Equal(["b.png"], h.ReferencedImages);
    }

    [Fact]
    public void Normalize_KeepsImageItems_DropsEmptyOnes()
    {
        var h = new ClipboardHistory
        {
            Items =
            [
                new ClipboardItem { Id = 1, Image = "a.png", ImageHash = "a", Width = 2, Height = 2 },
                new ClipboardItem { Id = 2, Text = "" },
                new ClipboardItem { Id = 3, Text = "ok" },
            ],
        };
        h.Normalize();
        Assert.Equal([1L, 3L], h.Items.Select(i => i.Id));
    }

    [Fact]
    public void Add_PutsNewestFirst()
    {
        var h = New();
        h.Add("a", T0);
        h.Add("b", T0);
        h.Add("c", T0);
        Assert.Equal(["c", "b", "a"], Texts(h));
    }

    [Fact]
    public void Add_SameAsTop_IsIgnored()
    {
        var h = New();
        Assert.True(h.Add("a", T0));
        Assert.False(h.Add("a", T0));
        Assert.Single(h.Items);
    }

    [Fact]
    public void Add_SameAsOlder_MovesItToTop_WithoutDuplicating()
    {
        var h = New();
        h.Add("a", T0);
        h.Add("b", T0);
        var aId = h.Items[1].Id;
        Assert.True(h.Add("a", T0.AddMinutes(1)));
        Assert.Equal(["a", "b"], Texts(h));
        Assert.Equal(aId, h.Items[0].Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Add_Empty_IsIgnored(string? text)
    {
        var h = New();
        Assert.False(h.Add(text, T0));
        Assert.Empty(h.Items);
    }

    [Fact]
    public void Add_TooLong_IsIgnored()
    {
        var h = New();
        Assert.False(h.Add(new string('x', ClipboardHistory.MaxTextLength + 1), T0));
        Assert.True(h.Add(new string('x', ClipboardHistory.MaxTextLength), T0));
    }

    [Fact]
    public void OverLimit_DropsOldestUnpinned()
    {
        var h = New(max: 3);
        h.Add("old-pinned", T0);
        h.Pin(h.Items[0].Id);
        h.Add("a", T0);
        h.Add("b", T0);
        h.Add("c", T0);
        Assert.Equal(["c", "b", "old-pinned"], Texts(h));
    }

    [Fact]
    public void OverLimit_AllPinned_KeepsThem()
    {
        var h = New(max: 1);
        h.Add("a", T0);
        h.Pin(h.Items[0].Id);
        h.Add("b", T0);
        h.Pin(h.Items[0].Id);
        Assert.Equal(2, h.Items.Count);
    }

    [Fact]
    public void Pin_Toggles()
    {
        var h = New();
        h.Add("a", T0);
        var id = h.Items[0].Id;
        Assert.True(h.Pin(id));
        Assert.True(h.Items[0].Pinned);
        h.Pin(id);
        Assert.False(h.Items[0].Pinned);
        Assert.False(h.Pin(999));
    }

    [Fact]
    public void Remove_And_Clear_KeepPinned()
    {
        var h = New();
        h.Add("keep", T0);
        h.Pin(h.Items[0].Id);
        h.Add("x", T0);
        h.Add("y", T0);
        Assert.True(h.Remove(h.Items[0].Id));
        Assert.Equal(["x", "keep"], Texts(h));
        h.Clear();
        Assert.Equal(["keep"], Texts(h));
    }

    [Fact]
    public void RoundTrip_AndNormalize()
    {
        var dir = Path.Combine(Path.GetTempPath(), "KnobTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonStore<ClipboardHistory>(Path.Combine(dir, "clipboard.json"));
            var h = New();
            h.Add("one", T0);
            h.Add("สอง", T0);
            h.Pin(h.Items[1].Id);
            store.Save(h);

            var loaded = store.Load();
            Assert.Equal(["สอง", "one"], Texts(loaded));
            Assert.True(loaded.Items[1].Pinned);

            File.WriteAllText(Path.Combine(dir, "clipboard.json"), "{\"Items\":[null,{\"Id\":1,\"Text\":\"\"},{\"Id\":1,\"Text\":\"a\"},{\"Id\":1,\"Text\":\"b\"}]}");
            var repaired = store.Load();
            Assert.Equal(["a", "b"], Texts(repaired));
            Assert.Equal(2, repaired.Items.Select(i => i.Id).Distinct().Count());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
