using Knob.Core.Storage;

namespace Knob.Core.Tests.Storage;

public class AtomicFileTests
{
    [Fact]
    public void WriteAllText_RoundTripsUtf8_AndLeavesNoTmp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "KnobTests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "notes.txt");
            AtomicFile.WriteAllText(path, "สวัสดี\nhello");
            AtomicFile.WriteAllText(path, "สวัสดีครับ\nhello");
            Assert.Equal("สวัสดีครับ\nhello", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

public class ByteFormatterTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(128849018880L, "120 GB")]
    [InlineData(5905580032L, "5.5 GB")]
    [InlineData(1649267441664L, "1.5 TB")]
    public void Format(long bytes, string expected) => Assert.Equal(expected, ByteFormatter.Format(bytes));
}
