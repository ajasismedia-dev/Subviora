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

    internal static GameProfileParseResult Failure(IEnumerable<string> errors) => new(null, [.. errors]);
}

/// <summary>Reads and writes game profile JSON documents (schema version 1), supporting layered overrides.</summary>
public static class GameProfileSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>
    /// Parses a profile document. If <paramref name="baseProfile"/> is provided, the document is treated as a layered
    /// user override on top of the built-in base profile. Otherwise, it is parsed as a standalone full profile.
    /// Never throws for malformed input; problems are returned as errors.
    /// </summary>
    public static GameProfileParseResult Parse(
        string json,
        ProfileSource source,
        string? sourcePath = null,
        GameProfile? baseProfile = null)
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

        if (string.IsNullOrWhiteSpace(document.GameId))
        {
            return GameProfileParseResult.Failure("Profile document must declare a non-empty 'gameId'.");
        }

        if (document.CoordinateSpace?.Mode is not null
            && !string.Equals(document.CoordinateSpace.Mode, GameProfileSchema.NormalizedCoordinateMode, StringComparison.Ordinal))
        {
            return GameProfileParseResult.Failure(
                $"coordinateSpace.mode '{document.CoordinateSpace.Mode}' is not supported (expected '{GameProfileSchema.NormalizedCoordinateMode}').");
        }

        if (baseProfile is not null)
        {
            return MergeOverride(document, baseProfile, sourcePath);
        }

        return ParseStandalone(document, source, sourcePath);
    }

    /// <summary>
    /// Attempts to read the <c>gameId</c> property from a JSON string without full deserialization.
    /// </summary>
    public static string? TryReadGameId(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("gameId", out var idElement)
                && idElement.ValueKind == JsonValueKind.String)
            {
                return idElement.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>Serializes a profile to an indented schema version 1 document.</summary>
    public static string Serialize(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return JsonSerializer.Serialize(ToDocument(profile), Options);
    }

    private static GameProfileParseResult ParseStandalone(GameProfileDocument document, ProfileSource source, string? sourcePath)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(document.ProfileVersion))
        {
            errors.Add("profileVersion is required for standalone profiles.");
        }

        if (string.IsNullOrWhiteSpace(document.DisplayName))
        {
            errors.Add("displayName is required for standalone profiles.");
        }

        if (document.Executables is null || document.Executables.Count == 0)
        {
            errors.Add("executables must contain at least one executable file name.");
        }

        if (document.Languages is null)
        {
            errors.Add("languages section is required for standalone profiles.");
        }

        if (document.CoordinateSpace is null)
        {
            errors.Add("coordinateSpace section is required for standalone profiles.");
        }

        if (document.Calibration is null)
        {
            errors.Add("calibration section is required for standalone profiles.");
        }

        if (document.SubtitleRegions is null || document.SubtitleRegions.Count == 0)
        {
            errors.Add("subtitleRegions must contain at least one region.");
        }

        if (errors.Count > 0)
        {
            return GameProfileParseResult.Failure(errors);
        }

        var profile = ToDomain(document, source, sourcePath);
        var validationErrors = GameProfileValidator.Validate(profile);
        return validationErrors.Count == 0
            ? new GameProfileParseResult(profile, [])
            : new GameProfileParseResult(null, validationErrors);
    }

    private static GameProfileParseResult MergeOverride(
        GameProfileDocument overrideDoc,
        GameProfile baseProfile,
        string? sourcePath)
    {
        var errors = new List<string>();

        if (!string.Equals(overrideDoc.GameId, baseProfile.GameId, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"User override gameId '{overrideDoc.GameId}' does not match target built-in profile '{baseProfile.GameId}'.");
            return GameProfileParseResult.Failure(errors);
        }

        if (!string.IsNullOrWhiteSpace(overrideDoc.TargetProfileVersion)
            && !GameProfileValidator.IsVersionCompatible(overrideDoc.TargetProfileVersion, baseProfile.ProfileVersion))
        {
            errors.Add(
                $"User override targetProfileVersion '{overrideDoc.TargetProfileVersion}' is incompatible with built-in profile version '{baseProfile.ProfileVersion}'.");
            return GameProfileParseResult.Failure(errors);
        }

        var subtitleRegions = MergeRegions("subtitleRegions", baseProfile.SubtitleRegions, overrideDoc.SubtitleRegions, errors);
        var exclusionRegions = MergeRegions("exclusionRegions", baseProfile.ExclusionRegions, overrideDoc.ExclusionRegions, errors);

        var coordinateSpace = overrideDoc.CoordinateSpace is not null
            ? new CoordinateSpace(
                overrideDoc.CoordinateSpace.ReferenceWidth ?? baseProfile.CoordinateSpace.ReferenceWidth,
                overrideDoc.CoordinateSpace.ReferenceHeight ?? baseProfile.CoordinateSpace.ReferenceHeight)
            : baseProfile.CoordinateSpace;

        var overlay = new OverlayPreferences(
            overrideDoc.Overlay?.Anchor ?? baseProfile.Overlay.Anchor,
            overrideDoc.Overlay?.FontScale ?? baseProfile.Overlay.FontScale,
            overrideDoc.Overlay?.MaxLines ?? baseProfile.Overlay.MaxLines,
            overrideDoc.Overlay?.BackgroundOpacity ?? baseProfile.Overlay.BackgroundOpacity,
            overrideDoc.Overlay?.HideSourceSubtitle ?? baseProfile.Overlay.HideSourceSubtitle);

        var merged = new GameProfile
        {
            SchemaVersion = overrideDoc.SchemaVersion,
            ProfileVersion = overrideDoc.ProfileVersion ?? baseProfile.ProfileVersion,
            GameId = baseProfile.GameId,
            DisplayName = overrideDoc.DisplayName ?? baseProfile.DisplayName,
            Executables = overrideDoc.Executables is { Count: > 0 } ? [.. overrideDoc.Executables] : baseProfile.Executables,
            SourceLanguage = overrideDoc.Languages?.Source ?? baseProfile.SourceLanguage,
            TargetLanguage = overrideDoc.Languages?.Target ?? baseProfile.TargetLanguage,
            CoordinateSpace = coordinateSpace,
            Calibration = overrideDoc.Calibration?.Status ?? baseProfile.Calibration,
            CalibrationNotes = overrideDoc.Calibration?.Notes ?? baseProfile.CalibrationNotes,
            SubtitleRegions = subtitleRegions,
            ExclusionRegions = exclusionRegions,
            Overlay = overlay,
            Source = ProfileSource.User,
            SourcePath = sourcePath,
            IsUserOverride = true,
            BaseProfileVersion = baseProfile.ProfileVersion,
        };

        var validationErrors = GameProfileValidator.Validate(merged);
        errors.AddRange(validationErrors);

        return errors.Count == 0
            ? new GameProfileParseResult(merged, [])
            : new GameProfileParseResult(null, errors);
    }

    private static List<ProfileRegion> MergeRegions(
        string collectionName,
        IReadOnlyList<ProfileRegion> baseRegions,
        List<RegionDocument>? overrides,
        List<string> errors)
    {
        var map = new Dictionary<string, ProfileRegion>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var r in baseRegions)
        {
            map[r.Id] = r;
            order.Add(r.Id);
        }

        if (overrides is null || overrides.Count == 0)
        {
            return [.. baseRegions];
        }

        foreach (var ov in overrides)
        {
            if (string.IsNullOrWhiteSpace(ov.Id))
            {
                errors.Add($"{collectionName}: region override must specify an 'id'.");
                continue;
            }

            var op = ov.Op ?? (ov.X.HasValue || ov.Width.HasValue ? "replace" : "remove");

            if (string.Equals(op, "remove", StringComparison.OrdinalIgnoreCase)
                || string.Equals(op, "delete", StringComparison.OrdinalIgnoreCase))
            {
                if (!map.Remove(ov.Id))
                {
                    errors.Add($"{collectionName}: cannot remove region '{ov.Id}' because it does not exist in the base profile.");
                }
                else
                {
                    order.RemoveAll(id => string.Equals(id, ov.Id, StringComparison.OrdinalIgnoreCase));
                }
            }
            else if (string.Equals(op, "replace", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(op, "upsert", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(op, "add", StringComparison.OrdinalIgnoreCase))
            {
                if (!ov.X.HasValue || !ov.Y.HasValue || !ov.Width.HasValue || !ov.Height.HasValue)
                {
                    errors.Add($"{collectionName}: region '{ov.Id}' with op '{op}' must provide x, y, width, and height.");
                    continue;
                }

                var bounds = new NormalizedRect(ov.X.Value, ov.Y.Value, ov.Width.Value, ov.Height.Value);
                var desc = ov.Description;

                if (map.TryGetValue(ov.Id, out var existing))
                {
                    desc ??= existing.Description;
                    map[ov.Id] = new ProfileRegion(ov.Id, bounds, desc);
                }
                else
                {
                    map[ov.Id] = new ProfileRegion(ov.Id, bounds, desc);
                    order.Add(ov.Id);
                }
            }
            else
            {
                errors.Add($"{collectionName}: region '{ov.Id}' has unsupported op '{ov.Op}' (expected 'replace', 'upsert', or 'remove').");
            }
        }

        var result = new List<ProfileRegion>();
        foreach (var id in order)
        {
            if (map.TryGetValue(id, out var r))
            {
                result.Add(r);
            }
        }

        return result;
    }

    private static string? CheckSchemaVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

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
        ProfileVersion = d.ProfileVersion!,
        GameId = d.GameId,
        DisplayName = d.DisplayName!,
        Executables = [.. d.Executables!],
        SourceLanguage = d.Languages!.Source!,
        TargetLanguage = d.Languages.Target!,
        CoordinateSpace = new CoordinateSpace(d.CoordinateSpace!.ReferenceWidth!.Value, d.CoordinateSpace.ReferenceHeight!.Value),
        Calibration = d.Calibration!.Status!.Value,
        CalibrationNotes = d.Calibration.Notes,
        SubtitleRegions = [.. (d.SubtitleRegions ?? []).Select(ToRegion)],
        ExclusionRegions = [.. (d.ExclusionRegions ?? []).Select(ToRegion)],
        Overlay = new OverlayPreferences(
            d.Overlay?.Anchor ?? OverlayPreferences.Default.Anchor,
            d.Overlay?.FontScale ?? OverlayPreferences.Default.FontScale,
            d.Overlay?.MaxLines ?? OverlayPreferences.Default.MaxLines,
            d.Overlay?.BackgroundOpacity ?? OverlayPreferences.Default.BackgroundOpacity,
            d.Overlay?.HideSourceSubtitle ?? OverlayPreferences.Default.HideSourceSubtitle),
        Source = source,
        SourcePath = sourcePath,
        IsUserOverride = false,
        BaseProfileVersion = null,
    };

    private static ProfileRegion ToRegion(RegionDocument r) =>
        new(r.Id, new NormalizedRect(r.X!.Value, r.Y!.Value, r.Width!.Value, r.Height!.Value), r.Description);

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
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            RespectNullableAnnotations = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
