using Knob.Core.Storage;
using Knob.Core.Todo;

namespace Knob.Core.Tests.Todo;

public class TodoListTests
{
    [Fact]
    public void Add_TrimsAndAppends()
    {
        var list = new TodoList();
        var item = list.Add("  buy milk  ");
        Assert.NotNull(item);
        Assert.Equal("buy milk", item!.Text);
        Assert.Single(list.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData(null)]
    public void Add_BlankText_DoesNothing(string? text)
    {
        var list = new TodoList();
        Assert.Null(list.Add(text));
        Assert.Empty(list.Items);
    }

    [Fact]
    public void Toggle_FlipsDone_AndCountsUpdate()
    {
        var list = new TodoList();
        var a = list.Add("a")!;
        list.Add("b");
        Assert.Equal(0, list.DoneCount);
        Assert.True(list.Toggle(a.Id));
        Assert.Equal(1, list.DoneCount);
        Assert.Equal(2, list.Items.Count);
        list.Toggle(a.Id);
        Assert.Equal(0, list.DoneCount);
    }

    [Fact]
    public void Remove_RemovesOnlyThatItem()
    {
        var list = new TodoList();
        var a = list.Add("a")!;
        var b = list.Add("b")!;
        Assert.True(list.Remove(a.Id));
        Assert.Equal([b.Id], list.Items.Select(i => i.Id));
        Assert.False(list.Remove(999));
    }

    [Fact]
    public void Ids_StayUnique_AfterRemoveAndAdd()
    {
        var list = new TodoList();
        list.Add("a");
        var b = list.Add("b")!;
        list.Remove(b.Id);
        list.Add("c");
        list.Add("d");
        Assert.Equal(list.Items.Count, list.Items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void RoundTrip_ThroughJsonStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "KnobTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonStore<TodoList>(Path.Combine(dir, "todo.json"));
            var list = new TodoList();
            var a = list.Add("ซื้อนม")!;
            list.Add("call mom");
            list.Toggle(a.Id);
            store.Save(list);

            var loaded = store.Load();
            Assert.Equal(2, loaded.Items.Count);
            Assert.Equal("ซื้อนม", loaded.Items[0].Text);
            Assert.True(loaded.Items[0].Done);
            Assert.Equal(1, loaded.DoneCount);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Normalize_RepairsHandEditedData()
    {
        var list = new TodoList
        {
            Items =
            [
                new TodoItem { Id = 1, Text = "a" },
                new TodoItem { Id = 1, Text = "b" },
                new TodoItem { Id = 2, Text = "   " },
                null!,
            ],
        };
        list.Normalize();
        Assert.Equal(["a", "b"], list.Items.Select(i => i.Text));
        Assert.Equal(2, list.Items.Select(i => i.Id).Distinct().Count());

        var empty = new TodoList { Items = null! };
        empty.Normalize();
        Assert.NotNull(empty.Items);
    }
}
