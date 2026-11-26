using System.Buffers.Binary;
using SkiaSharp;

namespace Knob.Core.Convert;

/// <summary>
/// Converts raster images between PNG, JPG, WEBP, BMP, multi-size ICO, and single/multi-page PDF using SkiaSharp.
/// Accepts an optional platform fallback decoder (e.g. Windows WIC for TIFF/HEIC) when Skia cannot decode an input.
/// </summary>
public static class ImageConverter
{
    private static readonly int[] IconSizes = [16, 32, 48, 256];

    public static void ConvertImage(
        string inputPath,
        string outputPath,
        ConvertFormat target,
        int quality = 88,
        Func<string, SKBitmap?>? fallbackDecoder = null)
    {
        quality = Math.Clamp(quality, 1, 100);
        if (target.Id == ConvertPlan.Pdf.Id)
        {
            ConvertImagesToPdf([inputPath], outputPath, quality, fallbackDecoder);
            return;
        }

        using var bitmap = LoadOrientedBitmap(inputPath, fallbackDecoder);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = outputPath + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                EncodeToStream(bitmap, fs, target, quality);
            }
            File.Move(tmp, outputPath, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    public static void ConvertImagesToPdf(
        IReadOnlyList<string> inputPaths,
        string outputPath,
        int quality = 90,
        Func<string, SKBitmap?>? fallbackDecoder = null)
    {
        if (inputPaths.Count == 0) throw new ArgumentException("At least one image is required.", nameof(inputPaths));
        quality = Math.Clamp(quality, 1, 100);

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = outputPath + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var pdf = SKDocument.CreatePdf(fs, new SKDocumentPdfMetadata { EncodingQuality = quality }))
            {
                foreach (var path in inputPaths)
                {
                    using var bitmap = LoadOrientedBitmap(path, fallbackDecoder);
                    // 96 DPI → 72 pt/in (0.75 pt per pixel), clamped to a sensible page range while keeping full pixel detail.
                    var pageW = Math.Max(36f, bitmap.Width * 0.75f);
                    var pageH = Math.Max(36f, bitmap.Height * 0.75f);
                    using var canvas = pdf.BeginPage(pageW, pageH);
                    canvas.Clear(SKColors.White);
                    using var image = SKImage.FromBitmap(bitmap);
                    canvas.DrawImage(image, new SKRect(0, 0, pageW, pageH), new SKSamplingOptions(SKCubicResampler.Mitchell));
                    pdf.EndPage();
                }
                pdf.Close();
            }
            File.Move(tmp, outputPath, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    public static void EncodeToStream(SKBitmap bitmap, Stream output, ConvertFormat target, int quality = 88)
    {
        switch (target.Id)
        {
            case "png":
                WriteSkiaEncoded(bitmap, output, SKEncodedImageFormat.Png, 100, flattenAlpha: false);
                break;
            case "jpg":
                WriteSkiaEncoded(bitmap, output, SKEncodedImageFormat.Jpeg, quality, flattenAlpha: true);
                break;
            case "webp":
                WriteWebp(bitmap, output, quality);
                break;
            case "bmp":
                WriteBmp24(bitmap, output);
                break;
            case "ico":
                WriteMultiSizeIco(bitmap, output);
                break;
            default:
                throw new NotSupportedException($"Unsupported image target format: {target.Id}");
        }
    }

    public static SKBitmap LoadOrientedBitmap(string path, Func<string, SKBitmap?>? fallbackDecoder = null)
    {
        using (var fs = File.OpenRead(path))
        using (var codec = SKCodec.Create(fs))
        {
            if (codec is not null)
            {
                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                var raw = new SKBitmap(info);
                var result = codec.GetPixels(info, raw.GetPixels());
                if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
                    return ApplyOrigin(raw, codec.EncodedOrigin);
                raw.Dispose();
            }
        }

        var fallback = fallbackDecoder?.Invoke(path);
        if (fallback is not null) return fallback;

        throw new InvalidDataException($"Could not decode image '{Path.GetFileName(path)}'.");
    }

    private static SKBitmap ApplyOrigin(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or 0) return src;

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var w = swap ? src.Height : src.Width;
        var h = swap ? src.Width : src.Height;

        var dst = new SKBitmap(w, h, src.ColorType, src.AlphaType);
        using var canvas = new SKCanvas(dst);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Scale(-1, 1, w / 2f, 0);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, w / 2f, h / 2f);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Scale(1, -1, 0, h / 2f);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.Translate(0, h);
                canvas.RotateDegrees(270);
                canvas.Scale(-1, 1, src.Width / 2f, 0);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(w, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(w, 0);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1, src.Width / 2f, 0);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, h);
                canvas.RotateDegrees(270);
                break;
        }
        using (var img = SKImage.FromBitmap(src))
            canvas.DrawImage(img, 0, 0, SKSamplingOptions.Default);
        src.Dispose();
        return dst;
    }

    private static void WriteSkiaEncoded(SKBitmap bitmap, Stream output, SKEncodedImageFormat format, int quality, bool flattenAlpha)
    {
        using var flattened = flattenAlpha ? OntoWhite(bitmap) : null;
        var target = flattened ?? bitmap;
        using var image = SKImage.FromBitmap(target);
        using var data = image.Encode(format, quality)
            ?? throw new InvalidOperationException($"Failed to encode image as {format}.");
        data.SaveTo(output);
    }

    private static SKBitmap OntoWhite(SKBitmap src)
    {
        var dst = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.White);
        using var img = SKImage.FromBitmap(src);
        canvas.DrawImage(img, 0, 0, SKSamplingOptions.Default);
        return dst;
    }

    private static void WriteWebp(SKBitmap bitmap, Stream output, int quality)
    {
        using var pixmap = bitmap.PeekPixels()
            ?? throw new InvalidOperationException("Cannot access image pixels.");
        var opts = quality >= 100
            ? new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 75)
            : new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality);
        using var data = pixmap.Encode(opts)
            ?? throw new InvalidOperationException("Failed to encode WEBP image.");
        data.SaveTo(output);
    }

    /// <summary>Standard uncompressed 24-bit BGR Windows BMP (universally readable, transparent pixels composited onto white).</summary>
    private static void WriteBmp24(SKBitmap bitmap, Stream output)
    {
        using var opaque = OntoWhite(bitmap);
        var width = opaque.Width;
        var height = opaque.Height;
        var stride = ((width * 3 + 3) / 4) * 4;
        var pixelBytes = stride * height;
        var fileSize = 14 + 40 + pixelBytes;

        Span<byte> header = stackalloc byte[54];
        header.Clear();
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header[2..], fileSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[10..], 54); // pixel data offset
        BinaryPrimitives.WriteInt32LittleEndian(header[14..], 40); // BITMAPINFOHEADER size
        BinaryPrimitives.WriteInt32LittleEndian(header[18..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[22..], height); // bottom-up
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], 1); // planes
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], 24); // 24 bpp
        BinaryPrimitives.WriteInt32LittleEndian(header[34..], pixelBytes);
        BinaryPrimitives.WriteInt32LittleEndian(header[38..], 3780); // 96 DPI
        BinaryPrimitives.WriteInt32LittleEndian(header[42..], 3780);
        output.Write(header);

        var row = new byte[stride];
        for (var y = height - 1; y >= 0; y--)
        {
            Array.Clear(row);
            for (var x = 0; x < width; x++)
            {
                var c = opaque.GetPixel(x, y);
                var offset = x * 3;
                row[offset] = c.Blue;
                row[offset + 1] = c.Green;
                row[offset + 2] = c.Red;
            }
            output.Write(row, 0, stride);
        }
    }

    /// <summary>Multi-resolution Windows .ico containing 16, 32, 48, and 256 px PNG frames (aspect-ratio preserved).</summary>
    private static void WriteMultiSizeIco(SKBitmap bitmap, Stream output)
    {
        var frames = new List<(int Size, byte[] Png)>(IconSizes.Length);
        foreach (var size in IconSizes)
        {
            using var square = RenderSquareIconFrame(bitmap, size);
            using var img = SKImage.FromBitmap(square);
            using var encoded = img.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("Failed to encode ICO frame.");
            frames.Add((size, encoded.ToArray()));
        }

        Span<byte> iconDir = stackalloc byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(iconDir[0..], 0); // reserved
        BinaryPrimitives.WriteUInt16LittleEndian(iconDir[2..], 1); // 1 = icon
        BinaryPrimitives.WriteUInt16LittleEndian(iconDir[4..], (ushort)frames.Count);
        output.Write(iconDir);

        var offset = 6 + frames.Count * 16;
        Span<byte> entry = stackalloc byte[16];
        foreach (var (size, png) in frames)
        {
            entry.Clear();
            entry[0] = size >= 256 ? (byte)0 : (byte)size;
            entry[1] = size >= 256 ? (byte)0 : (byte)size;
            entry[2] = 0; // palette count
            entry[3] = 0; // reserved
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1); // color planes
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32); // 32 bpp
            BinaryPrimitives.WriteInt32LittleEndian(entry[8..], png.Length);
            BinaryPrimitives.WriteInt32LittleEndian(entry[12..], offset);
            output.Write(entry);
            offset += png.Length;
        }

        foreach (var (_, png) in frames)
            output.Write(png, 0, png.Length);
    }

    private static SKBitmap RenderSquareIconFrame(SKBitmap src, int size)
    {
        var dst = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Transparent);

        var scale = Math.Min((float)size / src.Width, (float)size / src.Height);
        var drawW = Math.Max(1f, src.Width * scale);
        var drawH = Math.Max(1f, src.Height * scale);
        var left = (size - drawW) / 2f;
        var top = (size - drawH) / 2f;

        using var image = SKImage.FromBitmap(src);
        canvas.DrawImage(image, new SKRect(left, top, left + drawW, top + drawH), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return dst;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
