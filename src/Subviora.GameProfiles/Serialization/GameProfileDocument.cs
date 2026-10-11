using System.Text.Json.Serialization;
using Subviora.Core.Profiles;

namespace Subviora.GameProfiles.Serialization;

// JSON document shapes for schema version 1. These are deliberately separate from the domain model
// so the on-disk format can evolve (with migrations) without leaking into Subviora.Core.

internal sealed class GameProfileDocument
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    public required int SchemaVersion { get; set; }

    public required string ProfileVersion { get; set; }

    public required string GameId { get; set; }

    public required string DisplayName { get; set; }

    public required List<string> Executables { get; set; }

    public required LanguagesDocument Languages { get; set; }

    public required CoordinateSpaceDocument CoordinateSpace { get; set; }

    public required CalibrationDocument Calibration { get; set; }

    public required List<RegionDocument> SubtitleRegions { get; set; }

    public List<RegionDocument> ExclusionRegions { get; set; } = [];

    public OverlayDocument Overlay { get; set; } = new();
}

internal sealed class LanguagesDocument
{
    public required string Source { get; set; }

    public required string Target { get; set; }
}

internal sealed class CoordinateSpaceDocument
{
    /// <summary>Only "normalized" is supported in schema version 1.</summary>
    public required string Mode { get; set; }

    public required int ReferenceWidth { get; set; }

    public required int ReferenceHeight { get; set; }
}

internal sealed class CalibrationDocument
{
    public required CalibrationStatus Status { get; set; }

    public string? Notes { get; set; }
}

internal sealed class RegionDocument
{
    public required string Id { get; set; }

    public string? Description { get; set; }

    public required double X { get; set; }

    public required double Y { get; set; }

    public required double Width { get; set; }

    public required double Height { get; set; }
}

internal sealed class OverlayDocument
{
    public OverlayAnchor Anchor { get; set; } = OverlayPreferences.Default.Anchor;

    public double FontScale { get; set; } = OverlayPreferences.Default.FontScale;

    public int MaxLines { get; set; } = OverlayPreferences.Default.MaxLines;

    public double BackgroundOpacity { get; set; } = OverlayPreferences.Default.BackgroundOpacity;

    public bool HideSourceSubtitle { get; set; } = OverlayPreferences.Default.HideSourceSubtitle;
}
