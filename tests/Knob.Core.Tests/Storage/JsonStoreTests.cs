using Knob.Core.Storage;

namespace Knob.Core.Tests.Storage;

public sealed class JsonStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "KnobTests", Guid.NewGuid().ToString("N"));
    private string PathFor(string name) => System.IO.Path.Combine(_dir, name);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var store = new JsonStore<AppSettings>(PathFor("settings.json"));
        var s = new AppSettings { HoverOpenDelayMs = 200, RunAtStartup = true, ClipboardMaxItems = 10 };
        s.Hotkeys.Calculator = "Ctrl+Shift+C";

        store.Save(s);
        var loaded = store.Load();

        Assert.Equal(200, loaded.HoverOpenDelayMs);
        Assert.True(loaded.RunAtStartup);
        Assert.Equal(10, loaded.ClipboardMaxItems);
        Assert.Equal("Ctrl+Shift+C", loaded.Hotkeys.Calculator);
        Assert.Equal("Ctrl+Alt+Q", loaded.Hotkeys.Qr);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var loaded = new JsonStore<AppSettings>(PathFor("nope.json")).Load();

        Assert.Equal(120, loaded.HoverOpenDelayMs);
        Assert.Equal(350, loaded.CloseDelayMs);
        Assert.True(loaded.HideOnFullscreen);
        Assert.False(loaded.RunAtStartup);
        Assert.Equal("Ctrl+Alt+K", loaded.Hotkeys.Calculator);
        Assert.Equal("Ctrl+Alt+Q", loaded.Hotkeys.Qr);
        Assert.Equal("Ctrl+Alt+T", loaded.Hotkeys.TextCapture);
        Assert.Equal(50, loaded.ClipboardMaxItems);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"HoverOpenDelayMs\":\"abc\"}")]
    public void Load_CorruptFile_ReturnsDefaults(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor("settings.json"), content);

        var loaded = new JsonStore<AppSettings>(PathFor("settings.json")).Load();

        Assert.Equal(120, loaded.HoverOpenDelayMs);
        Assert.NotNull(loaded.Hotkeys);
    }

    [Fact]
    public void Load_NullNestedObject_FallsBackToDefault()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor("settings.json"), "{\"Hotkeys\":null}");

        var loaded = new JsonStore<AppSettings>(PathFor("settings.json")).Load();

        Assert.NotNull(loaded.Hotkeys);
        Assert.Equal("Ctrl+Alt+K", loaded.Hotkeys.Calculator);
    }

    [Fact]
    public void DockEdge_OutOfRangeNumber_FallsBackToTop_KeepingOtherSettings()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor("settings.json"), "{\"Dock\":{\"Edge\":7},\"CloseDelayMs\":500}");
        var loaded = new JsonStore<AppSettings>(PathFor("settings.json")).Load();
        Assert.Equal(Knob.Core.Shell.DockEdge.Top, loaded.Dock.Edge);
        Assert.Equal(500, loaded.CloseDelayMs);
    }

    [Fact]
    public void Theme_DefaultsToDefault_RoundTrips_AndUnknownFallsBack()
    {
        var store = new JsonStore<AppSettings>(PathFor("settings.json"));
        Assert.Equal(AppSettings.ThemeDefault, store.Load().Theme);

        store.Save(new AppSettings { Theme = AppSettings.ThemeScroll });
        Assert.Equal(AppSettings.ThemeScroll, store.Load().Theme);

        File.WriteAllText(PathFor("settings.json"), "{\"Theme\":\"Neon\",\"CloseDelayMs\":500}");
        var loaded = store.Load();
        Assert.Equal(AppSettings.ThemeDefault, loaded.Theme);
        Assert.Equal(500, loaded.CloseDelayMs);

        File.WriteAllText(PathFor("settings.json"), "{\"Theme\":\"scroll\"}");
        Assert.Equal(AppSettings.ThemeScroll, store.Load().Theme); // case-insensitive
    }

    [Fact]
    public void Save_LeavesNoTmpFile_AndCreatesDirectory()
    {
        var store = new JsonStore<AppSettings>(PathFor("settings.json"));

        store.Save(new AppSettings());
        store.Save(new AppSettings { CloseDelayMs = 1 });

        Assert.True(File.Exists(PathFor("settings.json")));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Equal(1, store.Load().CloseDelayMs);
    }
}
