using Knob.Core.Ocr;

namespace Knob.Core.Tests.Ocr;

public class ThaiTextCleanerTests
{
    [Theory]
    [InlineData("สวัส ดี ครับ", "สวัสดีครับ")]
    [InlineData("Hello world", "Hello world")]
    [InlineData("ราคา 100 บาท", "ราคา 100 บาท")]
    [InlineData("ไทย English", "ไทย English")]
    [InlineData("ภาษา  ไทย", "ภาษาไทย")]
    [InlineData("บรรทัด หนึ่ง\nบรรทัด สอง", "บรรทัดหนึ่ง\nบรรทัดสอง")]
    [InlineData("", "")]
    public void Clean(string input, string expected) => Assert.Equal(expected, ThaiTextCleaner.Clean(input));

    [Fact]
    public void Clean_TrimsTrailingWhitespace_AndCrLf()
    {
        Assert.Equal("a\nb", ThaiTextCleaner.Clean("a  \r\nb\n\n"));
    }
}

public class OcrPreprocessTests
{
    [Theory]
    [InlineData(20, 3)]
    [InlineData(59, 3)]
    [InlineData(60, 2)]
    [InlineData(400, 2)]
    public void ScaleFactor(int heightPx, int expected) => Assert.Equal(expected, OcrPreprocess.ScaleFactor(heightPx));

    [Theory]
    [InlineData(30.0, true)]
    [InlineData(127.9, true)]
    [InlineData(128.0, false)]
    [InlineData(240.0, false)]
    public void ShouldInvert(double meanLuma, bool expected) => Assert.Equal(expected, OcrPreprocess.ShouldInvert(meanLuma));
}
