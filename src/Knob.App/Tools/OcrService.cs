using System.IO;
using Tesseract;

namespace Knob.App.Tools;

public interface IOcrService
{
    /// <summary>Recognise text in a PNG. Throws <see cref="OcrDataMissingException"/> if language data is missing.</summary>
    Task<string> RecognizeAsync(byte[] png);
}

public sealed class OcrDataMissingException(string message) : Exception(message);

/// <summary>
/// Tesseract, Thai + English, LSTM only. The engine is created on first use and released after
/// 5 idle minutes to give back its ~50–80 MB. Runs on a background thread.
/// </summary>
public sealed class TesseractOcrService : IOcrService, IDisposable
{
    private const string Languages = "tha+eng";
    private static readonly TimeSpan IdleRelease = TimeSpan.FromMinutes(5);

    private readonly string _tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata");
    private readonly object _lock = new();
    private readonly Timer _idleTimer;
    private TesseractEngine? _engine;

    public TesseractOcrService()
    {
        _idleTimer = new Timer(_ => ReleaseEngine(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public Task<string> RecognizeAsync(byte[] png) => Task.Run(() =>
    {
        lock (_lock)
        {
            _idleTimer.Change(Timeout.Infinite, Timeout.Infinite);
            try
            {
                _engine ??= CreateEngine();
                using var pix = Pix.LoadFromMemory(png);
                using var page = _engine.Process(pix, PageSegMode.Auto);
                return page.GetText() ?? "";
            }
            finally
            {
                _idleTimer.Change(IdleRelease, Timeout.InfiniteTimeSpan);
            }
        }
    });

    private TesseractEngine CreateEngine()
    {
        foreach (var lang in Languages.Split('+'))
            if (!File.Exists(Path.Combine(_tessdata, lang + ".traineddata")))
                throw new OcrDataMissingException($"{lang}.traineddata not found in {_tessdata}");
        return new TesseractEngine(_tessdata, Languages, EngineMode.LstmOnly);
    }

    private void ReleaseEngine()
    {
        lock (_lock)
        {
            _engine?.Dispose();
            _engine = null;
        }
    }

    public void Dispose()
    {
        _idleTimer.Dispose();
        ReleaseEngine();
    }
}
