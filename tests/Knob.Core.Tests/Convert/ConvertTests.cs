using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Knob.Core.Convert;
using SkiaSharp;

namespace Knob.Core.Tests.Convert;

public sealed class ConvertTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "KnobConvertTests-" + Guid.NewGuid().ToString("N"));

    public ConvertTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    // ───────── ConvertPlan & safe output naming ─────────

    [Fact]
    public void TargetsForFile_OmitsOwnFormatAndReturnsValidOnes()
    {
        var pngTargets = ConvertPlan.TargetsForFile(@"C:\pics\cat.png").Select(t => t.Id).ToList();
        Assert.DoesNotContain("png", pngTargets);
        Assert.Equal(["webp", "jpg", "pdf", "ico", "bmp"], pngTargets);

        var jpegTargets = ConvertPlan.TargetsForFile(@"C:\pics\photo.JPEG").Select(t => t.Id).ToList();
        Assert.DoesNotContain("jpg", jpegTargets);
        Assert.Contains("webp", jpegTargets);
        Assert.Contains("png", jpegTargets);
    }

    [Fact]
    public void AvailableTargets_MixedImagesKeepBothPngAndJpg()
    {
        var targets = ConvertPlan.AvailableTargets([@"C:\a.png", @"C:\b.jpg"]).Select(t => t.Id).ToList();
        Assert.Contains("png", targets);
        Assert.Contains("jpg", targets);
        Assert.Contains("webp", targets);
        Assert.Contains("pdf", targets);
    }

    [Fact]
    public void AvailableTargets_IncompatibleCategoriesReturnEmpty()
    {
        var targets = ConvertPlan.AvailableTargets([@"C:\a.png", @"C:\song.mp3"]);
        Assert.Empty(targets);
    }

    [Fact]
    public void MediaAndDataTargets_MatchExpectedFormats()
    {
        Assert.Equal(["mp3", "wav", "m4a", "mp4"], ConvertPlan.TargetsForFile("clip.mov").Select(t => t.Id));
        Assert.Equal(["wav", "m4a"], ConvertPlan.TargetsForFile("track.mp3").Select(t => t.Id));
        Assert.Equal(["csv", "tsv"], ConvertPlan.TargetsForFile("items.json").Select(t => t.Id));
        Assert.Equal(["json", "tsv"], ConvertPlan.TargetsForFile("sheet.csv").Select(t => t.Id));
        Assert.Equal(["html", "txt"], ConvertPlan.TargetsForFile("README.md").Select(t => t.Id));
    }

    [Fact]
    public void NextOutputPath_NeverOverwritesExistingOrSourceFile()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\work\report.webp",
            @"C:\work\report (2).webp",
        };
        var next = ConvertPlan.NextOutputPath(@"C:\work\report.png", ".webp", taken.Contains);
        Assert.Equal(@"C:\work\report (3).webp", next);

        // Same extension as source must also pick (2) even if exists() were false for others
        var sameExt = ConvertPlan.NextOutputPath(@"C:\work\clip.mp4", ".mp4", _ => false);
        Assert.Equal(@"C:\work\clip (2).mp4", sameExt);
    }

    [Fact]
    public void NextMergedPdfPath_UsesMergedSuffixForMultipleImages()
    {
        var single = ConvertPlan.NextMergedPdfPath([@"C:\scans\page1.png"], _ => false);
        Assert.Equal(@"C:\scans\page1.pdf", single);

        var multi = ConvertPlan.NextMergedPdfPath([@"C:\scans\page1.png", @"C:\scans\page2.png"], _ => false);
        Assert.Equal(@"C:\scans\page1-merged.pdf", multi);
    }

    // ───────── JSON ↔ CSV / TSV (Thai + Excel BOM + leading zeros) ─────────

    [Fact]
    public void JsonToCsvFile_EmitsUtf8BomAndEscapesCommasQuotesAndThai()
    {
        var jsonPath = Path.Combine(_dir, "customers.json");
        var csvPath = Path.Combine(_dir, "customers.csv");
        File.WriteAllText(jsonPath, """
            [
              { "name": "สมชาย ใจดี", "phone": "0812345678", "note": "ห้อง 1, ชั้น \"2\"", "score": 95.5 },
              { "name": "สุดา", "phone": "0890001111", "active": true }
            ]
            """, Encoding.UTF8);

        TextFileConverter.Convert(jsonPath, csvPath, ConvertPlan.Csv);

        var bytes = File.ReadAllBytes(csvPath);
        // UTF-8 BOM (EF BB BF) for Excel Thai support
        Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

        var roundTripJsonPath = Path.Combine(_dir, "roundtrip.json");
        TextFileConverter.Convert(csvPath, roundTripJsonPath, ConvertPlan.Json);

        var jsonText = File.ReadAllText(roundTripJsonPath, Encoding.UTF8);
        Assert.Contains("สมชาย ใจดี", jsonText); // Thai stays readable, not \u0E...
        using var doc = JsonDocument.Parse(jsonText);
        var arr = doc.RootElement;
        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal("0812345678", arr[0].GetProperty("phone").GetString()); // leading zero preserved as string!
        Assert.Equal("ห้อง 1, ชั้น \"2\"", arr[0].GetProperty("note").GetString());
        Assert.Equal(95.5m, arr[0].GetProperty("score").GetDecimal());
        Assert.True(arr[1].GetProperty("active").GetBoolean());
    }

    [Fact]
    public void CsvSavedByThaiExcel_InWindows874_KeepsThai()
    {
        // Excel's plain "CSV (Comma delimited)" on Thai Windows writes code page 874, not UTF-8.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var csvPath = Path.Combine(_dir, "thai-ansi.csv");
        File.WriteAllText(csvPath, "name,city\r\nสมชาย,เชียงใหม่\r\n", Encoding.GetEncoding(874));
        var jsonPath = Path.Combine(_dir, "thai-ansi.json");

        TextFileConverter.Convert(csvPath, jsonPath, ConvertPlan.Json);

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
        Assert.Equal("สมชาย", doc.RootElement[0].GetProperty("name").GetString());
        Assert.Equal("เชียงใหม่", doc.RootElement[0].GetProperty("city").GetString());
    }

    [Fact]
    public void Utf8WithoutBom_IsNotMistakenForWindows874()
    {
        var csvPath = Path.Combine(_dir, "thai-utf8.csv");
        File.WriteAllText(csvPath, "name\r\nสุดา\r\n", new UTF8Encoding(false));
        var jsonPath = Path.Combine(_dir, "thai-utf8.json");

        TextFileConverter.Convert(csvPath, jsonPath, ConvertPlan.Json);

        Assert.Contains("สุดา", File.ReadAllText(jsonPath, Encoding.UTF8));
    }

    [Fact]
    public void CsvToTsv_HandlesMultilineQuotedFields()
    {
        var csv = "id,comment\r\n1,\"บรรทัดแรก\nบรรทัดสอง\"\r\n";
        var tsv = TabularConverter.ConvertDelimiter(csv, ',', '\t');
        var rows = TabularConverter.ParseDelimited(tsv, '\t');
        Assert.Equal(2, rows.Count);
        Assert.Equal("บรรทัดแรก\nบรรทัดสอง", rows[1][1]);
    }

    // ───────── Markdown → HTML / TXT ─────────

    [Fact]
    public void MarkdownToHtml_RendersHeadingsTablesCodeAndBlocksUnsafeLinks()
    {
        var md = """
            # สวัสดี Knob

            ข้อความ **ตัวหนา** และ `โค้ด` พร้อม [ลิงก์ปลอดภัย](https://example.com) กับ [อันตราย](javascript:alert(1))

            | ชื่อ | ราคา |
            |:---|---:|
            | กาแฟ | 65 |

            ```csharp
            var x = 1 < 2;
            ```
            """;

        var html = MarkdownConverter.ToHtmlDocument(md, "บันทึก");
        Assert.Contains("<title>บันทึก</title>", html);
        Assert.Contains("<h1>สวัสดี Knob</h1>", html);
        Assert.Contains("<strong>ตัวหนา</strong>", html);
        Assert.Contains("<code>โค้ด</code>", html);
        Assert.Contains("<a href=\"https://example.com\">ลิงก์ปลอดภัย</a>", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("<td style=\"text-align:right\">65</td>", html);
        Assert.Contains("var x = 1 &lt; 2;", html);

        var txt = MarkdownConverter.ToPlainText(md);
        Assert.Contains("สวัสดี Knob", txt);
        Assert.Contains("ลิงก์ปลอดภัย (https://example.com)", txt);
    }

    [Fact]
    public void MarkdownLinkLabel_WithInlineCode_RendersTheCode()
    {
        var html = MarkdownConverter.ToHtmlFragment("See [`Knob.exe`](https://example.com) now");
        Assert.Contains("<a href=\"https://example.com\"><code>Knob.exe</code></a>", html);
        Assert.False(html.Contains('\u0001'), "internal placeholder leaked into the HTML"); // char compare: culture rules ignore control chars
    }

    // ───────── SkiaSharp Image, WEBP, BMP, ICO & Multi-Image PDF ─────────

    [Fact]
    public void ImageConverter_RoundTripsPngWebpJpgBmpAndIco()
    {
        var srcPng = CreateSamplePng("sample.png", 120, 80, new SKColor(0x20, 0x80, 0xE0, 0x80)); // semi-transparent

        // PNG → WEBP → PNG
        var webp = Path.Combine(_dir, "sample.webp");
        ImageConverter.ConvertImage(srcPng, webp, ConvertPlan.Webp, quality: 90);
        Assert.True(File.Exists(webp) && new FileInfo(webp).Length > 20);
        // RIFF....WEBP magic bytes
        var webpBytes = File.ReadAllBytes(webp);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(webpBytes, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(webpBytes, 8, 4));

        var backPng = Path.Combine(_dir, "back.png");
        ImageConverter.ConvertImage(webp, backPng, ConvertPlan.Png);
        using (var decoded = SKBitmap.Decode(backPng))
        {
            Assert.Equal(120, decoded.Width);
            Assert.Equal(80, decoded.Height);
        }

        // Semi-transparent PNG → JPG composites onto white (not black)
        var jpg = Path.Combine(_dir, "sample.jpg");
        ImageConverter.ConvertImage(srcPng, jpg, ConvertPlan.Jpg, quality: 95);
        using (var decodedJpg = SKBitmap.Decode(jpg))
        {
            var px = decodedJpg.GetPixel(60, 40);
            Assert.True(px.Red > 100 && px.Green > 150 && px.Blue > 200);
        }

        // PNG → BMP (readable back by Skia)
        var bmp = Path.Combine(_dir, "sample.bmp");
        ImageConverter.ConvertImage(srcPng, bmp, ConvertPlan.Bmp);
        using (var decodedBmp = SKBitmap.Decode(bmp))
        {
            Assert.Equal(120, decodedBmp.Width);
            Assert.Equal(80, decodedBmp.Height);
        }

        // PNG → Multi-size ICO (4 PNG frames: 16, 32, 48, 256)
        var ico = Path.Combine(_dir, "sample.ico");
        ImageConverter.ConvertImage(srcPng, ico, ConvertPlan.Ico);
        var icoBytes = File.ReadAllBytes(ico);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(icoBytes.AsSpan(0, 2)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(icoBytes.AsSpan(2, 2)));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(icoBytes.AsSpan(4, 2)));
        Assert.Equal(16, icoBytes[6]);
        Assert.Equal(32, icoBytes[6 + 16]);
        Assert.Equal(48, icoBytes[6 + 32]);
        Assert.Equal(0, icoBytes[6 + 48]); // 0 means 256 px in ICONDIRENTRY
    }

    [Fact]
    public void ConvertImagesToPdf_MergesMultipleImagesIntoValidPdf()
    {
        var p1 = CreateSamplePng("p1.png", 200, 100, SKColors.Coral);
        var p2 = CreateSamplePng("p2.png", 150, 220, SKColors.SeaGreen);
        var pdf = Path.Combine(_dir, "merged.pdf");

        ImageConverter.ConvertImagesToPdf([p1, p2], pdf);

        var bytes = File.ReadAllBytes(pdf);
        Assert.True(bytes.Length > 200);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        var ascii = Encoding.ASCII.GetString(bytes);
        Assert.Contains("/Type /Pages", ascii);
        Assert.Contains("/Count 2", ascii);
    }

    private string CreateSamplePng(string name, int width, int height, SKColor color)
    {
        var path = Path.Combine(_dir, name);
        using var bmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(color);
        }
        using var fs = File.Create(path);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100)!;
        data.SaveTo(fs);
        return path;
    }
}
