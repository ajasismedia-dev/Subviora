using Subviora.Core.Geometry;

namespace Subviora.Core.Profiles;

/// <summary>Where a profile was loaded from.</summary>
public enum ProfileSource
{
    /// <summary>Shipped with the application. Read-only.</summary>
    BuiltIn = 0,

    /// <summary>Created or customised by the user in the local application data folder.</summary>
    User = 1,
}

/// <summary>Whether a profile's regions have been verified against real gameplay.</summary>
public enum CalibrationStatus
{
    /// <summary>Regions are illustrative examples and have not been verified in-game.</summary>
    Uncalibrated = 0,

    /// <summary>Regions have been verified against real gameplay footage or sessions.</summary>
    Verified = 1,
}

/// <summary>Preferred overlay placement relative to the game client area.</summary>
public enum OverlayAnchor
{
    /// <summary>Draw translated text over the original subtitle region.</summary>
    SourceRegion = 0,
    BottomCenter = 1,
    TopCenter = 2,
}

/// <summary>Operation to perform on a profile region when applying an override.</summary>
public enum RegionOverrideOperation
{
    /// <summary>Replaces the region if it exists, or adds it if it does not.</summary>
    Replace = 0,

    /// <summary>Removes the region with the specified ID from the profile.</summary>
    Remove = 1,
}

/// <summary>A named region of the game client area.</summary>
public sealed record ProfileRegion(string Id, NormalizedRect Bounds, string? Description = null);

/// <summary>An individual region customization within a profile override.</summary>
public sealed record ProfileRegionOverride(
    string Id,
    RegionOverrideOperation Operation = RegionOverrideOperation.Replace,
    NormalizedRect? Bounds = null,
    string? Description = null);

/// <summary>
/// Describes how coordinates in the profile were authored. All region coordinates are normalized
/// to [0, 1]; the reference resolution documents the resolution used when authoring them.
/// </summary>
public sealed record CoordinateSpace(int ReferenceWidth, int ReferenceHeight);

/// <summary>Presentation preferences for translated subtitles.</summary>
public sealed record OverlayPreferences(
    OverlayAnchor Anchor,
    double FontScale,
    int MaxLines,
    double BackgroundOpacity,
    bool HideSourceSubtitle)
{
    public static OverlayPreferences Default { get; } = new(OverlayAnchor.SourceRegion, 1.0, 2, 0.6, false);
}

/// <summary>A game profile: how Subviora recognises a game and where its subtitles appear.</summary>
public sealed record GameProfile
{
    /// <summary>Version of the profile JSON schema this profile was written against.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>Content version of this specific profile (semantic version string).</summary>
    public required string ProfileVersion { get; init; }

    /// <summary>Stable, lowercase identifier, e.g. "gta5-story".</summary>
    public required string GameId { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Executable file names (e.g. "GTA5.exe") used to recognise the game.</summary>
    public required IReadOnlyList<string> Executables { get; init; }

    public required string SourceLanguage { get; init; }

    public required string TargetLanguage { get; init; }

    public required CoordinateSpace CoordinateSpace { get; init; }

    public required CalibrationStatus Calibration { get; init; }

    public string? CalibrationNotes { get; init; }

    public required IReadOnlyList<ProfileRegion> SubtitleRegions { get; init; }

    public required IReadOnlyList<ProfileRegion> ExclusionRegions { get; init; }

    public required OverlayPreferences Overlay { get; init; }

    /// <summary>Set by the loader; not part of the JSON document.</summary>
    public ProfileSource Source { get; init; }

    /// <summary>File the profile was loaded from, if any. Set by the loader.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Whether this profile is a layered user override merged onto a built-in profile.</summary>
    public bool IsUserOverride { get; init; }

    /// <summary>Version of the built-in base profile this override layered onto, if any.</summary>
    public string? BaseProfileVersion { get; init; }

    /// <summary>Whether this profile is a universal fallback matching any executable (*).</summary>
    public bool IsUniversal => Executables.Any(e => e == "*");

    /// <summary>Case-insensitive match against the executable file name (directory components are ignored). Supports universal wildcard (*).</summary>
    public bool MatchesExecutable(string executablePathOrName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePathOrName);
        var fileName = Path.GetFileName(executablePathOrName);
        return Executables.Any(e => e == "*" || string.Equals(e, fileName, StringComparison.OrdinalIgnoreCase));
    }
}
