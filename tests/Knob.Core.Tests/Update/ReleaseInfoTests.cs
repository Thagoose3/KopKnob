using Knob.Core.Update;

namespace Knob.Core.Tests.Update;

public class ReleaseInfoTests
{
    private const string Latest = """
    {
      "tag_name": "v1.2.0", "name": "Knob 1.2.0", "draft": false, "prerelease": false,
      "html_url": "https://github.com/o/r/releases/tag/v1.2.0",
      "body": "Screenshot to Shelf, colour picker",
      "assets": [
        { "name": "notes.txt", "browser_download_url": "https://x/notes.txt", "size": 10 },
        { "name": "KnobSetup-1.2.0.exe", "browser_download_url": "https://github.com/o/r/releases/download/v1.2.0/KnobSetup-1.2.0.exe",
          "size": 83886080, "digest": "sha256:AbCdEf0123456789abcdef0123456789abcdef0123456789abcdef0123456789" }
      ]
    }
    """;

    [Fact]
    public void Parse_ReadsVersionInstallerAndChecksum()
    {
        var r = ReleaseInfo.Parse(Latest)!;
        Assert.Equal(new Version(1, 2, 0), r.Version);
        Assert.Equal("KnobSetup-1.2.0.exe", r.AssetName);
        Assert.Equal("https://github.com/o/r/releases/download/v1.2.0/KnobSetup-1.2.0.exe", r.AssetUrl);
        Assert.Equal(83886080, r.Size);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", r.Sha256);
        Assert.Equal("https://github.com/o/r/releases/tag/v1.2.0", r.PageUrl);
        Assert.Equal("Screenshot to Shelf, colour picker", r.Notes);
    }

    [Theory]
    [InlineData("\"draft\": false", "\"draft\": true")]
    [InlineData("\"prerelease\": false", "\"prerelease\": true")]
    [InlineData("KnobSetup-1.2.0.exe\", \"browser", "Other.exe\", \"browser")]   // no installer attached
    [InlineData("\"v1.2.0\", \"name\"", "\"nightly\", \"name\"")]             // tag isn't a version
    public void Parse_SkipsReleasesWeCannotInstall(string from, string to) =>
        Assert.Null(ReleaseInfo.Parse(Latest.Replace(from, to)));

    [Fact]
    public void Parse_GarbageIsNull()
    {
        Assert.Null(ReleaseInfo.Parse("not json"));
        Assert.Null(ReleaseInfo.Parse("{\"message\":\"Not Found\"}"));
    }

    [Fact]
    public void Parse_WithoutDigest_HasNoChecksum() =>
        Assert.Null(ReleaseInfo.Parse(Latest.Replace("\"digest\": \"sha256:AbCdEf0123456789abcdef0123456789abcdef0123456789abcdef0123456789\"", "\"x\": 1"))!.Sha256);

    [Theory]
    [InlineData("1.1.0", "1.2.0", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.2.0.0", "1.2.0", false)]   // assembly versions have a 4th part
    [InlineData("1.10.0", "1.9.0", false)]
    [InlineData("1.9.9", "2.0.0", true)]
    public void IsNewer(string current, string latest, bool expected) =>
        Assert.Equal(expected, ReleaseInfo.IsNewer(Version.Parse(current), Version.Parse(latest)));

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("V2.0.1", "2.0.1")]
    [InlineData("release-1", null)]
    public void ParseTag(string tag, string? expected) =>
        Assert.Equal(expected is null ? null : Version.Parse(expected), ReleaseInfo.ParseTag(tag));
}
