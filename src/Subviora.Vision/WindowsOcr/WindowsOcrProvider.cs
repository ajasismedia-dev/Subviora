using Subviora.Core.Capture;
using Subviora.Core.Geometry;
using Subviora.Core.Vision;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using CoreOcrResult = Subviora.Core.Vision.OcrResult;

namespace Subviora.Vision.WindowsOcr;

/// <summary>
/// Native Windows OCR provider using Windows.Media.Ocr.OcrEngine.
/// Built into Windows 10/11, Microsoft-signed, 0 MB download size, and zero extra unmanaged dependencies.
/// </summary>
public sealed class WindowsOcrProvider : IOcrProvider
{
    private readonly object _engineLock = new();
    private OcrEngine? _cachedEngine;
    private string? _cachedLanguageTag;

    public string Id => "windows-ocr";

    public string DisplayName => "Windows OCR (Yerel)";

    public bool IsAvailable => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    public string? AvailabilityMessage =>
        IsAvailable
            ? null
            : "Sistemde yüklü Windows OCR dil paketi bulunamadı. Lütfen Windows Ayarları > Dil ve Bölge bölümünden bir OCR dil paketi yükleyin.";

    public async Task<CoreOcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Frame.Width <= 0 || request.Frame.Height <= 0 || request.Frame.Pixels.IsEmpty)
        {
            return CoreOcrResult.Empty;
        }

        var engine = GetOrCreateEngine(request.LanguageTag);
        if (engine is null)
        {
            throw new PlatformNotSupportedException(
                $"No compatible Windows OCR engine could be initialized for language '{request.LanguageTag}'.");
        }

        var regionsToProcess = request.Regions.Count > 0
            ? request.Regions
            : [new NormalizedRect(0, 0, 1, 1)];

        var recognizedLines = new List<OcrTextLine>();

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

            using var softwareBitmap = CreateSoftwareBitmapFromFrame(croppedFrame);
            var ocrResult = await engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken).ConfigureAwait(false);

            if (ocrResult?.Lines is null || ocrResult.Lines.Count == 0)
            {
                continue;
            }

            foreach (var line in ocrResult.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.Text))
                {
                    continue;
                }

                // Compute bounding box for the line from its words or bounds
                double minX = double.MaxValue;
                double minY = double.MaxValue;
                double maxX = double.MinValue;
                double maxY = double.MinValue;

                if (line.Words.Count > 0)
                {
                    foreach (var word in line.Words)
                    {
                        var rect = word.BoundingRect;
                        if (rect.X < minX)
                        {
                            minX = rect.X;
                        }

                        if (rect.Y < minY)
                        {
                            minY = rect.Y;
                        }

                        if (rect.X + rect.Width > maxX)
                        {
                            maxX = rect.X + rect.Width;
                        }

                        if (rect.Y + rect.Height > maxY)
                        {
                            maxY = rect.Y + rect.Height;
                        }
                    }
                }
                else
                {
                    minX = 0;
                    minY = 0;
                    maxX = croppedFrame.Width;
                    maxY = croppedFrame.Height;
                }

                double localX = Math.Max(0, minX);
                double localY = Math.Max(0, minY);
                double localW = Math.Max(1, maxX - minX);
                double localH = Math.Max(1, maxY - minY);

                // Map local pixel rect within cropped frame to normalized coordinates of the parent frame
                double normX = Math.Clamp(region.X + (localX / croppedFrame.Width) * region.Width, 0.0, 1.0);
                double normY = Math.Clamp(region.Y + (localY / croppedFrame.Height) * region.Height, 0.0, 1.0);
                double normW = Math.Clamp((localW / croppedFrame.Width) * region.Width, 0.001, 1.0 - normX);
                double normH = Math.Clamp((localH / croppedFrame.Height) * region.Height, 0.001, 1.0 - normY);

                var bounds = new NormalizedRect(normX, normY, normW, normH);
                recognizedLines.Add(new OcrTextLine(line.Text.Trim(), bounds, Confidence: null));
            }
        }

        return new CoreOcrResult(recognizedLines);
    }

    private OcrEngine? GetOrCreateEngine(string requestedLanguage)
    {
        lock (_engineLock)
        {
            if (_cachedEngine is not null && string.Equals(_cachedLanguageTag, requestedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedEngine;
            }

            OcrEngine? engine = null;

            // 1. Try exact requested language
            try
            {
                var lang = new Language(requestedLanguage);
                if (OcrEngine.IsLanguageSupported(lang))
                {
                    engine = OcrEngine.TryCreateFromLanguage(lang);
                }
            }
            catch
            {
                // Fallback on invalid tag
            }

            // 2. Try common English tags if "en" was requested
            if (engine is null && requestedLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string candidate in new[] { "en-US", "en-GB" })
                {
                    try
                    {
                        var lang = new Language(candidate);
                        if (OcrEngine.IsLanguageSupported(lang))
                        {
                            engine = OcrEngine.TryCreateFromLanguage(lang);
                            if (engine is not null)
                            {
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // Continue to next candidate
                    }
                }
            }

            // 3. Fallback to user profile languages
            engine ??= OcrEngine.TryCreateFromUserProfileLanguages();

            // 4. Fallback to first available recognizer
            if (engine is null && OcrEngine.AvailableRecognizerLanguages.Count > 0)
            {
                engine = OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]);
            }

            if (engine is not null)
            {
                _cachedEngine = engine;
                _cachedLanguageTag = requestedLanguage;
            }

            return engine;
        }
    }

    private static SoftwareBitmap CreateSoftwareBitmapFromFrame(CapturedFrame frame)
    {
        var softwareBitmap = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            frame.Width,
            frame.Height,
            BitmapAlphaMode.Premultiplied);

        using (var dataWriter = new Windows.Storage.Streams.DataWriter())
        {
            dataWriter.WriteBytes(frame.Pixels.ToArray());
            var winrtBuffer = dataWriter.DetachBuffer();
            softwareBitmap.CopyFromBuffer(winrtBuffer);
        }

        return softwareBitmap;
    }
}
