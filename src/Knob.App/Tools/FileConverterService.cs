using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Knob.Core.Convert;
using SkiaSharp;
using Windows.Graphics.Imaging;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Knob.App.Tools;

public sealed record ConvertJobOptions(
    ConvertFormat Target,
    int Quality = 88,
    bool MergeImagesToPdf = true);

public sealed record ConvertResultItem(
    string SourcePath,
    string? OutputPath,
    string? Error = null)
{
    public bool Succeeded => OutputPath is not null && Error is null;
}

/// <summary>
/// Runs batch file conversions off the UI thread: SkiaSharp (+ Windows WIC fallback) for images/ICO/PDF,
/// Windows Media Foundation (<see cref="MediaTranscoder"/>) for audio/video, and <see cref="TextFileConverter"/>
/// for JSON/CSV/TSV/Markdown. Never overwrites existing files.
/// </summary>
public sealed class FileConverterService
{
    public async Task<IReadOnlyList<ConvertResultItem>> ConvertBatchAsync(
        IReadOnlyList<string> inputs,
        ConvertJobOptions options,
        Action<int, int, double>? onProgress = null,
        CancellationToken ct = default)
    {
        if (inputs.Count == 0) return [];
        var target = options.Target;

        // Special case: merge multiple images into one PDF document.
        if (target.Id == ConvertPlan.Pdf.Id && options.MergeImagesToPdf && inputs.Count > 1)
        {
            onProgress?.Invoke(0, 1, 0.1);
            return await Task.Run<IReadOnlyList<ConvertResultItem>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var outPath = ConvertPlan.NextMergedPdfPath(inputs);
                    ImageConverter.ConvertImagesToPdf(inputs, outPath, options.Quality, DecodeWithWinRtWic);
                    onProgress?.Invoke(1, 1, 1.0);
                    return [new ConvertResultItem(inputs[0], outPath)];
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return [new ConvertResultItem(inputs[0], null, FriendlyError(ex))];
                }
            }, ct);
        }

        var results = new List<ConvertResultItem>(inputs.Count);
        for (var i = 0; i < inputs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var src = inputs[i];
            var index = i;
            onProgress?.Invoke(index, inputs.Count, (double)index / inputs.Count);

            // If a mixed batch includes a file already in the target format (e.g. a.png + b.jpg → PNG),
            // convert it only when requested or copy-convert safely to NextOutputPath.
            try
            {
                var outPath = ConvertPlan.NextOutputPath(src, target.Extension);
                switch (target.Kind)
                {
                    case ConvertKind.Image:
                        await Task.Run(() =>
                            ImageConverter.ConvertImage(src, outPath, target, options.Quality, DecodeWithWinRtWic), ct);
                        break;

                    case ConvertKind.Data:
                    case ConvertKind.Markdown:
                        await Task.Run(() => TextFileConverter.Convert(src, outPath, target), ct);
                        break;

                    case ConvertKind.Media:
                        await TranscodeMediaAsync(src, outPath, target, filePct =>
                        {
                            var overall = (index + filePct / 100.0) / inputs.Count;
                            onProgress?.Invoke(index, inputs.Count, overall);
                        }, ct);
                        break;

                    default:
                        throw new NotSupportedException($"Unsupported conversion kind: {target.Kind}");
                }

                results.Add(new ConvertResultItem(src, outPath));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(new ConvertResultItem(src, null, FriendlyError(ex)));
            }

            onProgress?.Invoke(i + 1, inputs.Count, (double)(i + 1) / inputs.Count);
        }

        return results;
    }

    private static async Task TranscodeMediaAsync(
        string inputPath,
        string outputPath,
        ConvertFormat target,
        Action<double> onPercent,
        CancellationToken ct)
    {
        var fullInput = Path.GetFullPath(inputPath);
        var fullOutput = Path.GetFullPath(outputPath);
        var outDir = Path.GetDirectoryName(fullOutput)
            ?? throw new InvalidOperationException("Invalid output directory.");
        Directory.CreateDirectory(outDir);

        var srcFile = await StorageFile.GetFileFromPathAsync(fullInput);
        var profile = await BuildMediaProfileAsync(srcFile, target);

        var folder = await StorageFolder.GetFolderFromPathAsync(outDir);
        var tempName = $".knob-{Guid.NewGuid():N}{target.Extension}";
        var tempFile = await folder.CreateFileAsync(tempName, CreationCollisionOption.ReplaceExisting);
        var tempPath = tempFile.Path;

        try
        {
            var transcoder = new MediaTranscoder();
            var prep = await transcoder.PrepareFileTranscodeAsync(srcFile, tempFile, profile);
            if (!prep.CanTranscode)
            {
                throw new InvalidOperationException(prep.FailureReason switch
                {
                    TranscodeFailureReason.CodecNotFound => "Windows lacks a codec for this media format.",
                    TranscodeFailureReason.InvalidProfile => "Unsupported media encoding profile.",
                    _ => $"Cannot transcode this file ({prep.FailureReason}).",
                });
            }

            var progress = new Progress<double>(onPercent);
            await prep.TranscodeAsync().AsTask(ct, progress);
            File.Move(tempPath, fullOutput, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<MediaEncodingProfile> BuildMediaProfileAsync(StorageFile srcFile, ConvertFormat target)
    {
        switch (target.Id)
        {
            case "mp3":
                return MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High);
            case "wav":
                return MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
            case "m4a":
                return MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High);
            case "mp4":
            {
                var quality = VideoEncodingQuality.HD1080p;
                try
                {
                    var vp = await srcFile.Properties.GetVideoPropertiesAsync();
                    if (vp.Height > 0 && vp.Height <= 720) quality = VideoEncodingQuality.HD720p;
                    else if (vp.Height >= 2160) quality = VideoEncodingQuality.Uhd2160p;
                }
                catch
                {
                    // Audio-only or unknown metadata: keep default profile.
                }
                return MediaEncodingProfile.CreateMp4(quality);
            }
            default:
                throw new NotSupportedException($"Unsupported media format: {target.Label}");
        }
    }

    /// <summary>
    /// Fallback decoder using WinRT <see cref="BitmapDecoder"/> so formats supported by Windows codecs
    /// (such as TIFF or HEIC when the HEIF extension is installed) also convert seamlessly.
    /// </summary>
    private static SKBitmap? DecodeWithWinRtWic(string path)
    {
        try
        {
            return DecodeWinRtCoreAsync(Path.GetFullPath(path)).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<SKBitmap?> DecodeWinRtCoreAsync(string fullPath)
    {
        var file = await StorageFile.GetFileFromPathAsync(fullPath);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var soft = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var width = soft.PixelWidth;
        var height = soft.PixelHeight;
        var bytes = new byte[width * height * 4];
        soft.CopyToBuffer(bytes.AsBuffer());

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return bitmap;
    }

    private static string FriendlyError(Exception ex)
    {
        var msg = ex.Message;
        if (string.IsNullOrWhiteSpace(msg)) return ex.GetType().Name;
        var line = msg.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return line.Length > 90 ? line[..87] + "…" : line;
    }
}
