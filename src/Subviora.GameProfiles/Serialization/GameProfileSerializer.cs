using System.Text.Json;
using System.Text.Json.Serialization;
using Subviora.Core.Geometry;
using Subviora.Core.Profiles;
using Subviora.GameProfiles.Validation;

namespace Subviora.GameProfiles.Serialization;

/// <summary>Outcome of parsing a profile document.</summary>
public sealed record GameProfileParseResult(GameProfile? Profile, IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Profile is not null && Errors.Count == 0;

    internal static GameProfileParseResult Failure(params string[] errors) => new(null, errors);
}

/// <summary>Reads and writes game profile JSON documents (schema version 1).</summary>
public static class GameProfileSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>
    /// Parses and validates a profile document. Never throws for malformed input; problems are returned as errors.
    /// </summary>
    public static GameProfileParseResult Parse(string json, ProfileSource source, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        var versionError = CheckSchemaVersion(json);
        if (versionError is not null)
        {
            return GameProfileParseResult.Failure(versionError);
        }

        GameProfileDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<GameProfileDocument>(json, Options);
        }
        catch (JsonException ex)
        {
            return GameProfileParseResult.Failure(DescribeJsonError(ex));
        }

        if (document is null)
        {
            return GameProfileParseResult.Failure("Profile document is empty (null).");
        }

        if (!string.Equals(document.CoordinateSpace.Mode, GameProfileSchema.NormalizedCoordinateMode, StringComparison.Ordinal))
        {
            return GameProfileParseResult.Failure(
                $"coordinateSpace.mode '{document.CoordinateSpace.Mode}' is not supported (expected '{GameProfileSchema.NormalizedCoordinateMode}').");
        }

        var profile = ToDomain(document, source, sourcePath);
        var errors = GameProfileValidator.Validate(profile);
        return errors.Count == 0 ? new GameProfileParseResult(profile, []) : new GameProfileParseResult(null, errors);
    }

    /// <summary>Serializes a profile to an indented schema version 1 document.</summary>
    public static string Serialize(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return JsonSerializer.Serialize(ToDocument(profile), Options);
    }

    private static string? CheckSchemaVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return "Profile document must be a JSON object.";
            }

            if (!doc.RootElement.TryGetProperty("schemaVersion", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out var version))
            {
                return "Profile document must declare an integer 'schemaVersion'.";
            }

            return version == GameProfileSchema.CurrentVersion
                ? null
                : $"schemaVersion {version} is not supported by this version of Subviora (expected {GameProfileSchema.CurrentVersion}).";
        }
        catch (JsonException ex)
        {
            return DescribeJsonError(ex);
        }
    }

    private static string DescribeJsonError(JsonException ex)
    {
        var location = ex.LineNumber is { } line ? $" (line {line + 1}, byte {ex.BytePositionInLine + 1})" : string.Empty;
        var path = string.IsNullOrEmpty(ex.Path) ? string.Empty : $" at '{ex.Path}'";
        return $"Invalid profile JSON{path}{location}: {ex.Message}";
    }

    private static GameProfile ToDomain(GameProfileDocument d, ProfileSource source, string? sourcePath) => new()
    {
        SchemaVersion = d.SchemaVersion,
        ProfileVersion = d.ProfileVersion,
        GameId = d.GameId,
        DisplayName = d.DisplayName,
        Executables = [.. d.Executables],
        SourceLanguage = d.Languages.Source,
        TargetLanguage = d.Languages.Target,
        CoordinateSpace = new CoordinateSpace(d.CoordinateSpace.ReferenceWidth, d.CoordinateSpace.ReferenceHeight),
        Calibration = d.Calibration.Status,
        CalibrationNotes = d.Calibration.Notes,
        SubtitleRegions = [.. d.SubtitleRegions.Select(ToRegion)],
        ExclusionRegions = [.. d.ExclusionRegions.Select(ToRegion)],
        Overlay = new OverlayPreferences(
            d.Overlay.Anchor,
            d.Overlay.FontScale,
            d.Overlay.MaxLines,
            d.Overlay.BackgroundOpacity,
            d.Overlay.HideSourceSubtitle),
        Source = source,
        SourcePath = sourcePath,
    };

    // Bounds are not validated here; GameProfileValidator reports invalid rectangles with context.
    private static ProfileRegion ToRegion(RegionDocument r) =>
        new(r.Id, new NormalizedRect(r.X, r.Y, r.Width, r.Height), r.Description);

    private static GameProfileDocument ToDocument(GameProfile p) => new()
    {
        SchemaVersion = p.SchemaVersion,
        ProfileVersion = p.ProfileVersion,
        GameId = p.GameId,
        DisplayName = p.DisplayName,
        Executables = [.. p.Executables],
        Languages = new LanguagesDocument { Source = p.SourceLanguage, Target = p.TargetLanguage },
        CoordinateSpace = new CoordinateSpaceDocument
        {
            Mode = GameProfileSchema.NormalizedCoordinateMode,
            ReferenceWidth = p.CoordinateSpace.ReferenceWidth,
            ReferenceHeight = p.CoordinateSpace.ReferenceHeight,
        },
        Calibration = new CalibrationDocument { Status = p.Calibration, Notes = p.CalibrationNotes },
        SubtitleRegions = [.. p.SubtitleRegions.Select(ToRegionDocument)],
        ExclusionRegions = [.. p.ExclusionRegions.Select(ToRegionDocument)],
        Overlay = new OverlayDocument
        {
            Anchor = p.Overlay.Anchor,
            FontScale = p.Overlay.FontScale,
            MaxLines = p.Overlay.MaxLines,
            BackgroundOpacity = p.Overlay.BackgroundOpacity,
            HideSourceSubtitle = p.Overlay.HideSourceSubtitle,
        },
    };

    private static RegionDocument ToRegionDocument(ProfileRegion r) => new()
    {
        Id = r.Id,
        Description = r.Description,
        X = r.Bounds.X,
        Y = r.Bounds.Y,
        Width = r.Bounds.Width,
        Height = r.Bounds.Height,
    };

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            // Reject unknown properties so typos in hand-edited profiles are reported instead of ignored.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            RespectNullableAnnotations = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
