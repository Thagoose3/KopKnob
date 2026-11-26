namespace Knob.Core.Convert;

public enum ConvertKind { Image, Media, Data, Markdown }

public sealed record ConvertFormat(
    string Id,
    string Label,
    string Extension,
    ConvertKind Kind,
    bool SupportsQuality = false,
    bool CanMergeImages = false);

/// <summary>
/// Maps input files to the target formats Knob can convert them into, and picks safe output file names
/// next to the originals (never overwriting an existing file).
/// </summary>
public static class ConvertPlan
{
    public static readonly ConvertFormat Webp = new("webp", "WEBP", ".webp", ConvertKind.Image, SupportsQuality: true);
    public static readonly ConvertFormat Png = new("png", "PNG", ".png", ConvertKind.Image);
    public static readonly ConvertFormat Jpg = new("jpg", "JPG", ".jpg", ConvertKind.Image, SupportsQuality: true);
    public static readonly ConvertFormat Pdf = new("pdf", "PDF", ".pdf", ConvertKind.Image, CanMergeImages: true);
    public static readonly ConvertFormat Ico = new("ico", "ICO", ".ico", ConvertKind.Image);
    public static readonly ConvertFormat Bmp = new("bmp", "BMP", ".bmp", ConvertKind.Image);

    public static readonly ConvertFormat Mp3 = new("mp3", "MP3", ".mp3", ConvertKind.Media);
    public static readonly ConvertFormat Wav = new("wav", "WAV", ".wav", ConvertKind.Media);
    public static readonly ConvertFormat M4a = new("m4a", "M4A", ".m4a", ConvertKind.Media);
    public static readonly ConvertFormat Mp4 = new("mp4", "MP4", ".mp4", ConvertKind.Media);

    public static readonly ConvertFormat Csv = new("csv", "CSV", ".csv", ConvertKind.Data);
    public static readonly ConvertFormat Tsv = new("tsv", "TSV", ".tsv", ConvertKind.Data);
    public static readonly ConvertFormat Json = new("json", "JSON", ".json", ConvertKind.Data);

    public static readonly ConvertFormat Html = new("html", "HTML", ".html", ConvertKind.Markdown);
    public static readonly ConvertFormat Txt = new("txt", "TXT", ".txt", ConvertKind.Markdown);

    private static readonly ConvertFormat[] ImageTargets = [Webp, Png, Jpg, Pdf, Ico, Bmp];
    private static readonly ConvertFormat[] VideoTargets = [Mp3, Wav, M4a, Mp4];
    private static readonly ConvertFormat[] AudioTargets = [Mp3, Wav, M4a];

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".webp", ".bmp", ".gif", ".ico", ".tif", ".tiff", ".heic", ".heif", ".avif",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".m4v",
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".flac", ".wma", ".aac", ".ogg",
    };

    public static bool IsSupported(string path) => TargetsForFile(path).Count > 0;

    /// <summary>Targets valid for a single file (excluding its own current format).</summary>
    public static IReadOnlyList<ConvertFormat> TargetsForFile(string path)
    {
        var all = RawTargetsForExtension(Path.GetExtension(path));
        if (all.Count == 0) return [];
        var selfId = CanonicalFormatId(Path.GetExtension(path));
        return all.Where(f => f.Id != selfId).ToList();
    }

    /// <summary>
    /// Targets valid across all <paramref name="files"/>. A format is hidden only when every file in the batch
    /// is already in that format (so dropping a PNG+JPG mix still offers both PNG and JPG).
    /// </summary>
    public static IReadOnlyList<ConvertFormat> AvailableTargets(IReadOnlyList<string> files)
    {
        if (files is null || files.Count == 0) return [];
        IReadOnlyList<ConvertFormat>? common = null;
        foreach (var file in files)
        {
            var raw = RawTargetsForExtension(Path.GetExtension(file));
            if (raw.Count == 0) return [];
            common = common is null ? raw : common.Where(f => raw.Any(r => r.Id == f.Id)).ToList();
            if (common.Count == 0) return [];
        }
        if (common is null || common.Count == 0) return [];

        return common
            .Where(f => !files.All(file => CanonicalFormatId(Path.GetExtension(file)) == f.Id))
            .ToList();
    }

    private static IReadOnlyList<ConvertFormat> RawTargetsForExtension(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return [];
        if (ImageExtensions.Contains(ext)) return ImageTargets;
        if (VideoExtensions.Contains(ext)) return VideoTargets;
        if (AudioExtensions.Contains(ext)) return AudioTargets;
        return ext.ToLowerInvariant() switch
        {
            ".json" => [Csv, Tsv],
            ".csv" => [Json, Tsv],
            ".tsv" => [Json, Csv],
            ".md" or ".markdown" => [Html, Txt],
            _ => [],
        };
    }

    public static string? CanonicalFormatId(string? ext) => ext?.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".jfif" => "jpg",
        ".png" => "png",
        ".webp" => "webp",
        ".bmp" => "bmp",
        ".ico" => "ico",
        ".pdf" => "pdf",
        ".mp3" => "mp3",
        ".wav" => "wav",
        ".m4a" => "m4a",
        ".mp4" => "mp4",
        ".csv" => "csv",
        ".tsv" => "tsv",
        ".json" => "json",
        ".html" or ".htm" => "html",
        ".txt" => "txt",
        ".md" or ".markdown" => "md",
        _ => ext?.TrimStart('.').ToLowerInvariant(),
    };

    /// <summary>
    /// Pick an output path next to <paramref name="sourceFile"/> with <paramref name="targetExtension"/>,
    /// adding " (2)", " (3)", … if a file or folder is already there.
    /// </summary>
    public static string NextOutputPath(string sourceFile, string targetExtension, Func<string, bool>? exists = null)
    {
        exists ??= static p => File.Exists(p) || Directory.Exists(p);
        var dir = Path.GetDirectoryName(sourceFile) ?? "";
        var stem = Path.GetFileNameWithoutExtension(sourceFile);
        if (string.IsNullOrEmpty(stem)) stem = "converted";
        var ext = targetExtension.StartsWith('.') ? targetExtension : "." + targetExtension;

        bool Taken(string candidate) =>
            string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(sourceFile), StringComparison.OrdinalIgnoreCase)
            || exists(candidate);

        var first = Path.Combine(dir, stem + ext);
        if (!Taken(first)) return first;

        for (var n = 2; ; n++)
        {
            var next = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!Taken(next)) return next;
        }
    }

    /// <summary>Output path when merging one or more images into a single PDF next to the first image.</summary>
    public static string NextMergedPdfPath(IReadOnlyList<string> sourceFiles, Func<string, bool>? exists = null)
    {
        if (sourceFiles.Count == 0) throw new ArgumentException("At least one source file is required.", nameof(sourceFiles));
        if (sourceFiles.Count == 1) return NextOutputPath(sourceFiles[0], ".pdf", exists);

        var first = sourceFiles[0];
        var dir = Path.GetDirectoryName(first) ?? "";
        var stem = Path.GetFileNameWithoutExtension(first);
        if (string.IsNullOrEmpty(stem)) stem = "images";
        var synthetic = Path.Combine(dir, $"{stem}-merged.img");
        return NextOutputPath(synthetic, ".pdf", exists);
    }
}
