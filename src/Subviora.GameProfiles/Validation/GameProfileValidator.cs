using System.Text.RegularExpressions;
using Subviora.Core.Profiles;

namespace Subviora.GameProfiles.Validation;

/// <summary>Validates game profiles against the rules of the supported schema version.</summary>
public static partial class GameProfileValidator
{
    public const double MinFontScale = 0.5;
    public const double MaxFontScale = 3.0;
    public const int MinMaxLines = 1;
    public const int MaxMaxLines = 5;

    /// <summary>Returns a list of human-readable validation errors. An empty list means the profile is valid.</summary>
    public static IReadOnlyList<string> Validate(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<string>();

        if (profile.SchemaVersion != GameProfileSchema.CurrentVersion)
        {
            errors.Add($"schemaVersion {profile.SchemaVersion} is not supported (expected {GameProfileSchema.CurrentVersion}).");
        }

        if (string.IsNullOrWhiteSpace(profile.ProfileVersion) || !SemanticVersionRegex().IsMatch(profile.ProfileVersion))
        {
            errors.Add($"profileVersion '{profile.ProfileVersion}' must be a semantic version such as 1.0.0.");
        }

        if (string.IsNullOrWhiteSpace(profile.GameId) || !GameIdRegex().IsMatch(profile.GameId))
        {
            errors.Add($"gameId '{profile.GameId}' must be lowercase letters, digits and single hyphens (e.g. 'gta5-story').");
        }

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            errors.Add("displayName must not be empty.");
        }

        ValidateExecutables(profile.Executables, errors);
        ValidateLanguages(profile.SourceLanguage, profile.TargetLanguage, errors);

        if (profile.CoordinateSpace.ReferenceWidth <= 0 || profile.CoordinateSpace.ReferenceHeight <= 0)
        {
            errors.Add("coordinateSpace.referenceWidth and referenceHeight must be positive.");
        }

        if (profile.SubtitleRegions.Count == 0)
        {
            errors.Add("subtitleRegions must contain at least one region.");
        }

        var regionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ValidateRegions("subtitleRegions", profile.SubtitleRegions, regionIds, errors);
        ValidateRegions("exclusionRegions", profile.ExclusionRegions, regionIds, errors);
        ValidateOverlay(profile.Overlay, errors);

        return errors;
    }

    private static void ValidateExecutables(IReadOnlyList<string> executables, List<string> errors)
    {
        if (executables.Count == 0)
        {
            errors.Add("executables must contain at least one executable file name.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var exe in executables)
        {
            if (string.IsNullOrWhiteSpace(exe))
            {
                errors.Add("executable name must not be empty.");
            }
            else if (exe == "*")
            {
                if (!seen.Add(exe))
                {
                    errors.Add("universal executable '*' is listed more than once.");
                }
            }
            else if (exe.IndexOfAny(['\\', '/', ':']) >= 0 || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"executable '{exe}' must be a bare file name ending in .exe (or '*' for Universal Mode).");
            }
            else if (!seen.Add(exe))
            {
                errors.Add($"executable '{exe}' is listed more than once.");
            }
        }
    }

    private static void ValidateLanguages(string source, string target, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(source) || !LanguageTagRegex().IsMatch(source))
        {
            errors.Add($"languages.source '{source}' must be a BCP-47 language tag such as 'en'.");
        }

        if (string.IsNullOrWhiteSpace(target) || !LanguageTagRegex().IsMatch(target))
        {
            errors.Add($"languages.target '{target}' must be a BCP-47 language tag such as 'tr'.");
        }

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("languages.source and languages.target must differ.");
        }
    }

    private static void ValidateRegions(
        string collectionName,
        IReadOnlyList<ProfileRegion> regions,
        HashSet<string> regionIds,
        List<string> errors)
    {
        foreach (var region in regions)
        {
            if (string.IsNullOrWhiteSpace(region.Id) || !GameIdRegex().IsMatch(region.Id))
            {
                errors.Add($"{collectionName}: region id '{region.Id}' must be lowercase letters, digits and hyphens.");
            }
            else if (!regionIds.Add(region.Id))
            {
                errors.Add($"{collectionName}: region id '{region.Id}' is not unique within the profile.");
            }

            if (!region.Bounds.IsValid)
            {
                var b = region.Bounds;
                errors.Add(
                    $"{collectionName}: region '{region.Id}' ({b.X}, {b.Y}, {b.Width}, {b.Height}) must have positive size and lie within normalized [0, 1] coordinates.");
            }
        }
    }

    private static void ValidateOverlay(OverlayPreferences overlay, List<string> errors)
    {
        if (!Enum.IsDefined(overlay.Anchor))
        {
            errors.Add($"overlay.anchor '{overlay.Anchor}' is not a supported value.");
        }

        if (!double.IsFinite(overlay.FontScale) || overlay.FontScale < MinFontScale || overlay.FontScale > MaxFontScale)
        {
            errors.Add($"overlay.fontScale must be between {MinFontScale} and {MaxFontScale}.");
        }

        if (overlay.MaxLines < MinMaxLines || overlay.MaxLines > MaxMaxLines)
        {
            errors.Add($"overlay.maxLines must be between {MinMaxLines} and {MaxMaxLines}.");
        }

        if (!double.IsFinite(overlay.BackgroundOpacity) || overlay.BackgroundOpacity < 0 || overlay.BackgroundOpacity > 1)
        {
            errors.Add("overlay.backgroundOpacity must be between 0 and 1.");
        }
    }

    /// <summary>
    /// Checks whether an override targeting <paramref name="targetVersion"/> is compatible
    /// with the base profile's <paramref name="baseVersion"/>.
    /// </summary>
    public static bool IsVersionCompatible(string targetVersion, string baseVersion)
    {
        if (string.IsNullOrWhiteSpace(targetVersion) || string.IsNullOrWhiteSpace(baseVersion))
        {
            return false;
        }

        targetVersion = targetVersion.Trim();
        baseVersion = baseVersion.Trim();

        if (string.Equals(targetVersion, baseVersion, StringComparison.OrdinalIgnoreCase) || targetVersion == "*")
        {
            return true;
        }

        if (targetVersion.StartsWith('^'))
        {
            targetVersion = targetVersion[1..].Trim();
        }

        if (targetVersion.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = targetVersion[..^2];
            return baseVersion.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (Version.TryParse(NormalizeVersionString(targetVersion), out var tVer)
            && Version.TryParse(NormalizeVersionString(baseVersion), out var bVer))
        {
            if (tVer.Major >= 1 || bVer.Major >= 1)
            {
                return tVer.Major == bVer.Major && bVer >= tVer;
            }

            return tVer.Major == 0 && bVer.Major == 0 && tVer.Minor == bVer.Minor && bVer.Build >= tVer.Build;
        }

        return string.Equals(targetVersion, baseVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersionString(string v)
    {
        var hyphen = v.IndexOf('-');
        if (hyphen >= 0)
        {
            v = v[..hyphen];
        }

        var parts = v.Split('.');
        return parts.Length switch
        {
            1 => $"{parts[0]}.0.0.0",
            2 => $"{parts[0]}.{parts[1]}.0.0",
            3 => $"{parts[0]}.{parts[1]}.{parts[2]}.0",
            _ => v,
        };
    }

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex GameIdRegex();

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionRegex();

    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTagRegex();
}
