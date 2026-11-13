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

    [Fact]
    public void Clean_DropsLeadingBlankLines_AndCollapsesBlankRuns()
    {
        Assert.Equal("a\n\nb", ThaiTextCleaner.Clean("\n \n\na\n\n\n\n b".Replace("\n b", "\nb")));
        Assert.Equal("a\nb", ThaiTextCleaner.Clean("\n\na\nb"));
    }
}

public class OcrPreprocessTests
{
    [Theory]
    [InlineData(464, 73, 4)]      // a few lines of normal screen text → ×4 (best on real Notepad text)
    [InlineData(80, 20, 4)]
    [InlineData(1200, 600, 2)]    // big selection: capped near 6 MP output
    [InlineData(3000, 2000, 1)]   // never below ×1
    public void ScaleFactor(int w, int h, int expected) => Assert.Equal(expected, OcrPreprocess.ScaleFactor(w, h));

    [Fact]
    public void OtsuThreshold_SplitsTwoPeaks()
    {
        var hist = new int[256];
        hist[30] = 500;   // text
        hist[225] = 4000; // background
        var t = OcrPreprocess.OtsuThreshold(hist);
        Assert.InRange(t, 30, 224);
    }

    [Fact]
    public void OtsuThreshold_Degenerate_IsMidGrey()
    {
        Assert.Equal(128, OcrPreprocess.OtsuThreshold(new int[256]));
        var flat = new int[256];
        flat[200] = 1000;
        Assert.Equal(128, OcrPreprocess.OtsuThreshold(flat));
    }

    [Theory]
    [InlineData(30.0, true)]
    [InlineData(127.9, true)]
    [InlineData(128.0, false)]
    [InlineData(240.0, false)]
    public void ShouldInvert(double meanLuma, bool expected) => Assert.Equal(expected, OcrPreprocess.ShouldInvert(meanLuma));
}
