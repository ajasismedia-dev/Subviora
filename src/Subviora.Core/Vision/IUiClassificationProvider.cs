using Subviora.Core.Capture;
using Subviora.Core.Profiles;

namespace Subviora.Core.Vision;

/// <summary>Categories used by Smart UI Detection to separate subtitles from other on-screen text.</summary>
public enum UiElementKind
{
    Unknown = 0,
    Subtitle = 1,
    Menu = 2,
    Hud = 3,
    Notification = 4,
    Other = 5,
}

/// <summary>Input for UI classification.</summary>
/// <param name="Frame">The frame the lines were recognised from.</param>
/// <param name="Lines">OCR lines to classify.</param>
/// <param name="Profile">Active game profile, if one is known.</param>
public sealed record UiClassificationRequest(CapturedFrame Frame, IReadOnlyList<OcrTextLine> Lines, GameProfile? Profile);

/// <summary>Classification of a single OCR line.</summary>
/// <param name="Line">The classified line.</param>
/// <param name="Kind">Assigned category.</param>
/// <param name="Confidence">Classifier confidence in the range [0, 1].</param>
public sealed record UiElementClassification(OcrTextLine Line, UiElementKind Kind, double Confidence);

/// <summary>Decides which recognised text lines are real subtitles (hybrid Smart UI Detection).</summary>
public interface IUiClassificationProvider
{
    /// <summary>Stable provider identifier.</summary>
    string Id { get; }

    Task<IReadOnlyList<UiElementClassification>> ClassifyAsync(
        UiClassificationRequest request,
        CancellationToken cancellationToken);
}
