using Knob.Core.Shelf;
using Knob.Core.Storage;

namespace Knob.Core.Tests.Shelf;

public class PathListTests
{
    [Fact]
    public void Add_KeepsOrder_SkipsDuplicatesIgnoringCase_AndBlanks()
    {
        var list = new PathList();
        Assert.Equal(2, list.Add([@"C:\a.txt", @"C:\B.txt"]));
        Assert.Equal(1, list.Add([@"c:\A.TXT", "", "  ", @"C:\c"]));
        Assert.Equal([@"C:\a.txt", @"C:\B.txt", @"C:\c"], list.Paths);
    }

    [Fact]
    public void Remove_And_Prune()
    {
        var list = new PathList();
        list.Add([@"C:\a", @"C:\b", @"C:\c"]);
        Assert.True(list.Remove(@"C:\B"));
        Assert.False(list.Remove(@"C:\zzz"));
        Assert.Equal(1, list.Prune(p => p != @"C:\c"));
        Assert.Equal([@"C:\a"], list.Paths);
    }

    [Fact]
    public void Move_Reorders()
    {
        var list = new PathList();
        list.Add(["a", "b", "c"]);
        Assert.True(list.Move("c", -1));
        Assert.Equal(["a", "c", "b"], list.Paths);
        Assert.False(list.Move("a", -1));
    }

    [Fact]
    public void Limit_DropsOldest()
    {
        var list = new PathList { Max = 3 };
        list.Add(["1", "2", "3", "4"]);
        Assert.Equal(["2", "3", "4"], list.Paths);
    }

    [Fact]
    public void RoundTrips_AndNormalizesBadFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "knob-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(dir, "shelf.json");
            var store = new JsonStore<PathList>(file);
            var list = new PathList();
            list.Add([@"C:\x", @"C:\y"]);
            store.Save(list);
            Assert.Equal([@"C:\x", @"C:\y"], store.Load().Paths);

            File.WriteAllText(file, "{\"Paths\":[null,\"C:\\\\x\",\"c:\\\\X\",\"\"]}");
            Assert.Equal([@"C:\x"], store.Load().Paths);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
