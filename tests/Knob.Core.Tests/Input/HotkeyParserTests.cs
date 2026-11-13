using Knob.Core.Input;

namespace Knob.Core.Tests.Input;

public class HotkeyParserTests
{
    [Fact]
    public void Parses_CtrlAltLetter()
    {
        var h = HotkeyParser.Parse("Ctrl+Alt+K");
        Assert.NotNull(h);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, h!.Modifiers);
        Assert.Equal(0x4B, h.VirtualKey);
    }

    [Theory]
    [InlineData("ctrl+alt+k")]
    [InlineData("CTRL + ALT + K")]
    [InlineData("  Alt+Ctrl+k ")]
    [InlineData("Control+Alt+K")]
    public void CaseSpacesAndOrder_DoNotMatter(string text)
    {
        var h = HotkeyParser.Parse(text);
        Assert.Equal(new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4B), h);
    }

    [Theory]
    [InlineData("Shift+Win+F5", HotkeyModifiers.Shift | HotkeyModifiers.Win, 0x74)]
    [InlineData("Ctrl+1", HotkeyModifiers.Control, 0x31)]
    [InlineData("Alt+Space", HotkeyModifiers.Alt, 0x20)]
    [InlineData("Ctrl+Shift+PrintScreen", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x2C)]
    public void OtherKeys(string text, HotkeyModifiers mods, int vk) =>
        Assert.Equal(new Hotkey(mods, vk), HotkeyParser.Parse(text));

    [Theory]
    [InlineData("Ctrl+Alt+Banana")] // unknown key
    [InlineData("Ctrl+Alt")]        // no key
    [InlineData("K")]               // no modifier
    [InlineData("Ctrl+K+L")]        // two keys
    [InlineData("Ctrl++K")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Invalid_ReturnsNull(string? text) => Assert.Null(HotkeyParser.Parse(text));

    [Fact]
    public void ToString_IsReadable() =>
        Assert.Equal("Ctrl+Alt+K", HotkeyParser.Parse("alt+ctrl+k")!.ToString());
}
