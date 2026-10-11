using Subviora.Core.Capture;
using Subviora.Core.Geometry;
using Subviora.Core.Vision;
using Tesseract;

namespace Subviora.Vision.Tesseract;

/// <summary>
/// Tesseract OCR provider using the open-source libtesseract 5.2 engine (Apache-2.0 license).
/// Requires trained language models (e.g. eng.traineddata) in tessdata folder.
/// </summary>
public sealed class TesseractOcrProvider : IOcrProvider, IDisposable
{
    private readonly object _lock = new();
    private TesseractEngine? _engine;
    private string? _resolvedTessDataPath;
    private bool _initialized;
    private bool _isDisposed;

    public string Id => "tesseract";

    public string DisplayName => "Tesseract OCR (v5.2)";

    public bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return _engine is not null;
        }
    }

    public string? AvailabilityMessage
    {
        get
        {
            EnsureInitialized();
            if (_engine is not null)
            {
                return null;
            }

            return $"Tesseract dil verisi (eng.traineddata) bulunamadı. Lütfen modeli '{GetDefaultTessDataDir()}' dizinine yerleştirin.";
        }
    }

    public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Frame.Width <= 0 || request.Frame.Height <= 0 || request.Frame.Pixels.IsEmpty)
        {
            return Task.FromResult(OcrResult.Empty);
        }

        EnsureInitialized();
        if (_engine is null)
        {
            throw new InvalidOperationException(AvailabilityMessage ?? "Tesseract is not available.");
        }

        var regionsToProcess = request.Regions.Count > 0
            ? request.Regions
            : [new NormalizedRect(0, 0, 1, 1)];

        var recognizedLines = new List<OcrTextLine>();

        lock (_lock)
        {
            foreach (var region in regionsToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var croppedFrame = region.X == 0 && region.Y == 0 && region.Width == 1 && region.Height == 1
                    ? request.Frame
                    : request.Frame.Crop(region);

                if (croppedFrame.Width <= 0 || croppedFrame.Height <= 0 || croppedFrame.Pixels.IsEmpty)
                {
                    continue;
                }

                byte[] bmpBytes = CreateBmpMemoryImage(croppedFrame);
                using var pix = Pix.LoadFromMemory(bmpBytes);
                if (pix is null)
                {
                    continue;
                }

                using var page = _engine.Process(pix, PageSegMode.SingleBlock);
                using var iter = page.GetIterator();

                if (iter is null)
                {
                    continue;
                }

                iter.Begin();
                do
                {
                    string? lineText = iter.GetText(PageIteratorLevel.TextLine);
                    if (string.IsNullOrWhiteSpace(lineText))
                    {
                        continue;
                    }

                    float conf = iter.GetConfidence(PageIteratorLevel.TextLine);
                    double normalizedConfidence = Math.Clamp(conf / 100.0, 0.0, 1.0);

                    NormalizedRect bounds;
                    if (iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out var rect))
                    {
                        double normX = Math.Clamp(region.X + ((double)rect.X1 / croppedFrame.Width) * region.Width, 0.0, 1.0);
                        double normY = Math.Clamp(region.Y + ((double)rect.Y1 / croppedFrame.Height) * region.Height, 0.0, 1.0);
                        double normW = Math.Clamp(((double)rect.Width / croppedFrame.Width) * region.Width, 0.001, 1.0 - normX);
                        double normH = Math.Clamp(((double)rect.Height / croppedFrame.Height) * region.Height, 0.001, 1.0 - normY);
                        bounds = new NormalizedRect(normX, normY, normW, normH);
                    }
                    else
                    {
                        bounds = region;
                    }

                    recognizedLines.Add(new OcrTextLine(lineText.Trim(), bounds, normalizedConfidence));
                }
                while (iter.Next(PageIteratorLevel.TextLine));
            }
        }

        return Task.FromResult(new OcrResult(recognizedLines));
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_lock)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _resolvedTessDataPath = FindTessDataDirectory();

            if (!string.IsNullOrEmpty(_resolvedTessDataPath))
            {
                try
                {
                    _engine = new TesseractEngine(_resolvedTessDataPath, "eng", EngineMode.Default);
                }
                catch
                {
                    _engine = null;
                }
            }
        }
    }

    private static string? FindTessDataDirectory()
    {
        // 1. Check local app data
        string localAppDataDir = GetDefaultTessDataDir();
        if (File.Exists(Path.Combine(localAppDataDir, "eng.traineddata")))
        {
            return localAppDataDir;
        }

        // 2. Check application base directory
        string baseDir = Path.Combine(AppContext.BaseDirectory, "tessdata");
        if (File.Exists(Path.Combine(baseDir, "eng.traineddata")))
        {
            return baseDir;
        }

        // 3. Check TESSDATA_PREFIX environment variable
        string? envPrefix = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (!string.IsNullOrEmpty(envPrefix) && File.Exists(Path.Combine(envPrefix, "eng.traineddata")))
        {
            return envPrefix;
        }

        return null;
    }

    public static string GetDefaultTessDataDir()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Subviora", "tessdata");
    }

    private static byte[] CreateBmpMemoryImage(CapturedFrame frame)
    {
        int width = frame.Width;
        int height = frame.Height;
        int imageSize = width * height * 4;
        int fileSize = 54 + imageSize;

        byte[] bmp = new byte[fileSize];

        // BITMAPFILEHEADER
        bmp[0] = 0x42; // 'B'
        bmp[1] = 0x4D; // 'M'
        BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
        bmp[10] = 54; // Offset to pixel array

        // BITMAPINFOHEADER
        bmp[14] = 40; // Header size
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(-height).CopyTo(bmp, 22); // Top-down
        bmp[26] = 1;  // Color planes
        bmp[28] = 32; // 32 bits per pixel (BGRA)
        BitConverter.GetBytes(imageSize).CopyTo(bmp, 34);

        // Pixels (BGRA)
        frame.Pixels.Span.CopyTo(bmp.AsSpan(54));

        return bmp;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _engine?.Dispose();
            _engine = null;
        }

        GC.SuppressFinalize(this);
    }
}
