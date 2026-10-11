using Microsoft.Extensions.Logging;
using Subviora.Core.Profiles;
using Subviora.GameProfiles.Serialization;

namespace Subviora.GameProfiles.Storage;

/// <summary>Locations of profile documents.</summary>
/// <param name="BuiltInDirectory">Read-only profiles shipped with the application.</param>
/// <param name="UserDirectory">User-created or user-customised profiles (local application data).</param>
public sealed record GameProfileStoreOptions(string BuiltInDirectory, string UserDirectory);

/// <summary>A profile file that could not be loaded.</summary>
public sealed record ProfileLoadIssue(string FilePath, ProfileSource Source, IReadOnlyList<string> Errors);

/// <summary>The effective set of profiles plus any load problems.</summary>
/// <param name="Profiles">Effective profiles, user overrides applied, ordered by display name.</param>
/// <param name="Issues">Files that were skipped, with reasons.</param>
/// <param name="OverriddenBuiltInIds">Ids of built-in profiles customized by a layered user override.</param>
public sealed record GameProfileCatalog(
    IReadOnlyList<GameProfile> Profiles,
    IReadOnlyList<ProfileLoadIssue> Issues,
    IReadOnlyList<string> OverriddenBuiltInIds);

/// <summary>
/// Loads profiles using a layered architecture: built-in profiles are immutable and loaded first.
/// User profiles that match a built-in profile's <c>gameId</c> are applied as fine-grained overrides,
/// merging user customizations onto the built-in profile while preserving unmodified fields and built-in updates.
/// User profiles with new <c>gameId</c>s are loaded as standalone custom profiles.
/// Profiles are re-read on each call; the files are small and this avoids stale state.
/// </summary>
public sealed partial class FileSystemGameProfileProvider : IGameProfileProvider
{
    private readonly GameProfileStoreOptions _options;
    private readonly ILogger<FileSystemGameProfileProvider> _logger;

    public FileSystemGameProfileProvider(GameProfileStoreOptions options, ILogger<FileSystemGameProfileProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
    }

    public GameProfileStoreOptions Options => _options;

    /// <summary>Loads all profiles and reports problems without throwing for individual bad files.</summary>
    public async Task<GameProfileCatalog> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var issues = new List<ProfileLoadIssue>();

        // 1. Built-in profiles (immutable baseline)
        var builtInMap = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(_options.BuiltInDirectory))
        {
            var builtInFiles = Directory.EnumerateFiles(_options.BuiltInDirectory, "*" + GameProfileSchema.FileExtension, SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in builtInFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (content, readIssue) = await TryReadFileAsync(file, ProfileSource.BuiltIn, cancellationToken).ConfigureAwait(false);
                if (readIssue is not null)
                {
                    issues.Add(readIssue);
                    continue;
                }

                var parsed = GameProfileSerializer.Parse(content!, ProfileSource.BuiltIn, file, baseProfile: null);
                if (!parsed.IsSuccess)
                {
                    LogProfileInvalid(file, string.Join(" | ", parsed.Errors));
                    issues.Add(new ProfileLoadIssue(file, ProfileSource.BuiltIn, parsed.Errors));
                    continue;
                }

                var profile = parsed.Profile!;
                if (!builtInMap.TryAdd(profile.GameId, profile))
                {
                    var msg = $"gameId '{profile.GameId}' is already defined by another built-in profile; this file was skipped.";
                    LogProfileInvalid(file, msg);
                    issues.Add(new ProfileLoadIssue(file, ProfileSource.BuiltIn, [msg]));
                }
            }
        }
        else
        {
            LogBuiltInDirectoryMissing(_options.BuiltInDirectory);
            issues.Add(new ProfileLoadIssue(_options.BuiltInDirectory, ProfileSource.BuiltIn, ["Built-in profile directory does not exist."]));
        }

        // Initialize effective dictionary with built-in baseline
        var effective = new Dictionary<string, GameProfile>(builtInMap, StringComparer.OrdinalIgnoreCase);
        var overridden = new List<string>();

        // 2. User profiles and overrides
        if (Directory.Exists(_options.UserDirectory))
        {
            var userFiles = Directory.EnumerateFiles(_options.UserDirectory, "*" + GameProfileSchema.FileExtension, SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            var seenUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in userFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (content, readIssue) = await TryReadFileAsync(file, ProfileSource.User, cancellationToken).ConfigureAwait(false);
                if (readIssue is not null)
                {
                    issues.Add(readIssue);
                    continue;
                }

                var gameId = GameProfileSerializer.TryReadGameId(content!);
                if (gameId is not null && !seenUserIds.Add(gameId))
                {
                    var msg = $"gameId '{gameId}' is already customized by another user profile file; this file was skipped.";
                    LogProfileInvalid(file, msg);
                    issues.Add(new ProfileLoadIssue(file, ProfileSource.User, [msg]));
                    continue;
                }

                // Check if this targets a built-in profile for layered override
                builtInMap.TryGetValue(gameId ?? string.Empty, out var baseProfile);

                var parsed = GameProfileSerializer.Parse(content!, ProfileSource.User, file, baseProfile);
                if (!parsed.IsSuccess)
                {
                    LogProfileInvalid(file, string.Join(" | ", parsed.Errors));
                    issues.Add(new ProfileLoadIssue(file, ProfileSource.User, parsed.Errors));
                    // Note: If an override had errors, the built-in profile remains in effective dictionary.
                    continue;
                }

                var profile = parsed.Profile!;
                effective[profile.GameId] = profile;

                if (baseProfile is not null)
                {
                    overridden.Add(profile.GameId);
                    LogUserOverride(profile.GameId, file);
                }
            }
        }

        var ordered = effective.Values
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        LogCatalogLoaded(ordered.Count, issues.Count);
        return new GameProfileCatalog(ordered, issues, overridden);
    }

    public async Task<IReadOnlyList<GameProfile>> GetProfilesAsync(CancellationToken cancellationToken) =>
        (await LoadCatalogAsync(cancellationToken).ConfigureAwait(false)).Profiles;

    public async Task<GameProfile?> GetByIdAsync(string gameId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        var profiles = await GetProfilesAsync(cancellationToken).ConfigureAwait(false);
        return profiles.FirstOrDefault(p => string.Equals(p.GameId, gameId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<GameProfile?> FindByExecutableAsync(string executablePathOrName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePathOrName);
        var profiles = await GetProfilesAsync(cancellationToken).ConfigureAwait(false);

        // Precedence:
        // 1. Exact executable match (non-universal), preferring user profiles over built-in.
        // 2. Universal match (*), preferring user profiles over built-in.
        return profiles
            .Where(p => p.MatchesExecutable(executablePathOrName))
            .OrderBy(p => p.IsUniversal)
            .ThenByDescending(p => p.Source == ProfileSource.User)
            .FirstOrDefault();
    }

    private async Task<(string? Content, ProfileLoadIssue? Issue)> TryReadFileAsync(
        string file,
        ProfileSource source,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            return (content, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogProfileReadFailed(ex, file);
            return (null, new ProfileLoadIssue(file, source, [$"File could not be read: {ex.Message}"]));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {ProfileCount} game profile(s) with {IssueCount} issue(s)")]
    private partial void LogCatalogLoaded(int profileCount, int issueCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied layered user override for game profile {GameId} ({Path})")]
    private partial void LogUserOverride(string gameId, string? path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Built-in profile directory not found: {Directory}")]
    private partial void LogBuiltInDirectoryMissing(string directory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read game profile {Path}")]
    private partial void LogProfileReadFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped invalid game profile {Path}: {Errors}")]
    private partial void LogProfileInvalid(string path, string errors);
}
