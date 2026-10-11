using System.Text.Json.Serialization;
using Subviora.Core.Profiles;

namespace Subviora.GameProfiles.Serialization;

// JSON document shapes for schema version 1. These are deliberately separate from the domain model
// so the on-disk format can evolve (with migrations) without leaking into Subviora.Core.
// Supports both full standalone profiles and layered user overrides.

internal sealed class GameProfileDocument
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    public required int SchemaVersion { get; set; }

    public string? ProfileVersion { get; set; }

    /// <summary>Optional target profile version to validate compatibility against a built-in base profile.</summary>
    public string? TargetProfileVersion { get; set; }

    public required string GameId { get; set; }

    public string? DisplayName { get; set; }

    public List<string>? Executables { get; set; }

    public LanguagesDocument? Languages { get; set; }

    public CoordinateSpaceDocument? CoordinateSpace { get; set; }

    public CalibrationDocument? Calibration { get; set; }

    public List<RegionDocument>? SubtitleRegions { get; set; }

    public List<RegionDocument>? ExclusionRegions { get; set; }

    public OverlayDocument? Overlay { get; set; }
}

internal sealed class LanguagesDocument
{
    public string? Source { get; set; }

    public string? Target { get; set; }
}

internal sealed class CoordinateSpaceDocument
{
    /// <summary>Only "normalized" is supported in schema version 1.</summary>
    public string? Mode { get; set; }

    public int? ReferenceWidth { get; set; }

    public int? ReferenceHeight { get; set; }
}

internal sealed class CalibrationDocument
{
    public CalibrationStatus? Status { get; set; }

    public string? Notes { get; set; }
}

internal sealed class RegionDocument
{
    public required string Id { get; set; }

    /// <summary>Operation to perform: "replace" (default), "upsert", or "remove".</summary>
    public string? Op { get; set; }

    public string? Description { get; set; }

    public double? X { get; set; }

    public double? Y { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }
}

internal sealed class OverlayDocument
{
    public OverlayAnchor? Anchor { get; set; }

    public double? FontScale { get; set; }

    public int? MaxLines { get; set; }

    public double? BackgroundOpacity { get; set; }

    public bool? HideSourceSubtitle { get; set; }
}
