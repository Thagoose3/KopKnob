using Knob.Core.Color;

namespace Knob.Core.Tests.Color;

public class ColorTextTests
{
    [Theory]
    [InlineData(30, 144, 255, "#1E90FF")]
    [InlineData(0, 0, 0, "#000000")]
    [InlineData(255, 255, 255, "#FFFFFF")]
    public void Hex(byte r, byte g, byte b, string expected) => Assert.Equal(expected, ColorText.Hex(r, g, b));

    [Fact]
    public void Rgb() => Assert.Equal("rgb(30, 144, 255)", ColorText.Rgb(30, 144, 255));
}
