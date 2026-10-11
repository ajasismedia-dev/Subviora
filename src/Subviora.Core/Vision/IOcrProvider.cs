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
public sealed record OcrResult(IReadOnlyList<OcrTextLine> Lines)
{
    /// <summary>An empty OCR result with no recognised lines.</summary>
    public static OcrResult Empty { get; } = new([]);

    /// <summary>All lines joined by newlines.</summary>
    public string FullText => string.Join(Environment.NewLine, Lines.Select(l => l.Text));
}

/// <summary>Recognises text in a captured frame. Implementations must be replaceable.</summary>
public interface IOcrProvider
{
    /// <summary>Stable provider identifier.</summary>
    string Id { get; }

    /// <summary>Human-readable display name for UI dropdowns.</summary>
    string DisplayName => Id;

    /// <summary>True when the engine and its dependencies/models are available on this system.</summary>
    bool IsAvailable => true;

    /// <summary>Explanation if the engine is unavailable (e.g. missing language pack or model).</summary>
    string? AvailabilityMessage => null;

    Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken);
}
