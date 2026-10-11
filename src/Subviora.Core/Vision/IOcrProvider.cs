using Subviora.Core.Capture;
using Subviora.Core.Geometry;

namespace Subviora.Core.Vision;

/// <summary>Input for an OCR pass.</summary>
/// <param name="Frame">Frame to analyse.</param>
/// <param name="Regions">Regions to read. An empty list means the whole frame.</param>
/// <param name="LanguageTag">BCP-47 tag of the expected text language, e.g. "en".</param>
public sealed record OcrRequest(CapturedFrame Frame, IReadOnlyList<NormalizedRect> Regions, string LanguageTag);

/// <summary>A line of text recognised by an OCR provider.</summary>
/// <param name="Text">Recognised text.</param>
/// <param name="Bounds">Line bounds in normalized frame coordinates.</param>
/// <param name="Confidence">Provider confidence in the range [0, 1].</param>
public sealed record OcrTextLine(string Text, NormalizedRect Bounds, double Confidence);

/// <summary>Result of an OCR pass.</summary>
public sealed record OcrResult(IReadOnlyList<OcrTextLine> Lines);

/// <summary>Recognises text in a captured frame. Implementations must be replaceable.</summary>
public interface IOcrProvider
{
    /// <summary>Stable provider identifier.</summary>
    string Id { get; }

    Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken);
}
