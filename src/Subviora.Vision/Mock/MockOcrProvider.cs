using Subviora.Core.Geometry;
using Subviora.Core.Vision;

namespace Subviora.Vision.Mock;

/// <summary>
/// Deterministic OCR provider for automated unit tests and benchmark harness validation.
/// Does not depend on external models or operating system language packs.
/// </summary>
public sealed class MockOcrProvider : IOcrProvider
{
    public string Id => "mock-ocr";

    public string DisplayName => "Mock / Test OCR";

    public bool IsAvailable => true;

    public string? AvailabilityMessage => null;

    /// <summary>Custom lines to return if specified.</summary>
    public IReadOnlyList<OcrTextLine>? ConfiguredLines { get; set; }

    /// <summary>Simulated execution delay in milliseconds.</summary>
    public int LatencyMs { get; set; } = 10;

    /// <summary>When true, throws an InvalidOperationException to test error recovery.</summary>
    public bool ShouldThrow { get; set; }

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ShouldThrow)
        {
            throw new InvalidOperationException("Simulated MockOcrProvider fault.");
        }

        if (LatencyMs > 0)
        {
            await Task.Delay(LatencyMs, cancellationToken).ConfigureAwait(false);
        }

        if (ConfiguredLines is not null)
        {
            return new OcrResult(ConfiguredLines);
        }

        if (request.Frame.Width <= 0 || request.Frame.Height <= 0 || request.Frame.Pixels.IsEmpty)
        {
            return OcrResult.Empty;
        }

        var bounds = request.Regions.Count > 0
            ? request.Regions[0]
            : new NormalizedRect(0.1, 0.8, 0.8, 0.1);

        return new OcrResult(
        [
            new OcrTextLine("What are you doing here, Michael?", bounds, 0.98)
        ]);
    }
}
