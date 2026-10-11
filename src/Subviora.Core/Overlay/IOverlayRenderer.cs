using Subviora.Core.Geometry;
using Subviora.Core.Profiles;

namespace Subviora.Core.Overlay;

/// <summary>A subtitle to draw on the overlay.</summary>
/// <param name="Text">Text to display (typically the translation).</param>
/// <param name="Region">Normalized region of the original subtitle, used for placement.</param>
/// <param name="Preferences">Presentation preferences, usually taken from the active game profile.</param>
public sealed record OverlaySubtitle(string Text, NormalizedRect Region, OverlayPreferences Preferences);

/// <summary>
/// Renders translated subtitles above the game. Planned implementation: native Win32 layered window
/// with Direct2D/DirectWrite.
/// </summary>
public interface IOverlayRenderer : IAsyncDisposable
{
    /// <summary>Replaces the currently displayed subtitles with <paramref name="subtitles"/>.</summary>
    Task ShowAsync(IReadOnlyList<OverlaySubtitle> subtitles, CancellationToken cancellationToken);

    /// <summary>Removes all subtitles from the overlay.</summary>
    Task ClearAsync(CancellationToken cancellationToken);
}
