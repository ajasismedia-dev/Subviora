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
/// <param name="OverriddenBuiltInIds">Ids of built-in profiles replaced by a user profile.</param>
public sealed record GameProfileCatalog(
    IReadOnlyList<GameProfile> Profiles,
    IReadOnlyList<ProfileLoadIssue> Issues,
    IReadOnlyList<string> OverriddenBuiltInIds);

/// <summary>
/// Loads profiles from the built-in and user directories. A user profile whose <c>gameId</c> matches a
/// built-in profile replaces it entirely (whole-profile override). Built-in files are never modified.
/// Profiles are re-read on every call; the files are small and this avoids stale state.
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
        var builtIn = await LoadDirectoryAsync(_options.BuiltInDirectory, ProfileSource.BuiltIn, issues, cancellationToken).ConfigureAwait(false);
        var user = await LoadDirectoryAsync(_options.UserDirectory, ProfileSource.User, issues, cancellationToken).ConfigureAwait(false);

        var effective = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in builtIn)
        {
            effective[profile.GameId] = profile;
        }

        var overridden = new List<string>();
        foreach (var profile in user)
        {
            if (effective.ContainsKey(profile.GameId))
            {
                overridden.Add(profile.GameId);
                LogUserOverride(profile.GameId, profile.SourcePath);
            }

            effective[profile.GameId] = profile;
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

        // User profiles were applied last, so a user profile always wins over a built-in one with the same id.
        // If two different ids claim the same executable, prefer user profiles, then the first by display name.
        return profiles
            .Where(p => p.MatchesExecutable(executablePathOrName))
            .OrderByDescending(p => p.Source == ProfileSource.User)
            .FirstOrDefault();
    }

    private async Task<List<GameProfile>> LoadDirectoryAsync(
        string directory,
        ProfileSource source,
        List<ProfileLoadIssue> issues,
        CancellationToken cancellationToken)
    {
        var result = new List<GameProfile>();
        if (!Directory.Exists(directory))
        {
            if (source == ProfileSource.BuiltIn)
            {
                LogBuiltInDirectoryMissing(directory);
                issues.Add(new ProfileLoadIssue(directory, source, ["Built-in profile directory does not exist."]));
            }

            return result;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(directory, "*" + GameProfileSchema.FileExtension, SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string json;
            try
            {
                json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogProfileReadFailed(ex, file);
                issues.Add(new ProfileLoadIssue(file, source, [$"File could not be read: {ex.Message}"]));
                continue;
            }

            var parsed = GameProfileSerializer.Parse(json, source, file);
            if (!parsed.IsSuccess)
            {
                LogProfileInvalid(file, string.Join(" | ", parsed.Errors));
                issues.Add(new ProfileLoadIssue(file, source, parsed.Errors));
                continue;
            }

            var profile = parsed.Profile!;
            if (!seenIds.Add(profile.GameId))
            {
                var message = $"gameId '{profile.GameId}' is already defined by another {source} profile; this file was skipped.";
                LogProfileInvalid(file, message);
                issues.Add(new ProfileLoadIssue(file, source, [message]));
                continue;
            }

            result.Add(profile);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {ProfileCount} game profile(s) with {IssueCount} issue(s)")]
    private partial void LogCatalogLoaded(int profileCount, int issueCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "User profile overrides built-in profile {GameId} ({Path})")]
    private partial void LogUserOverride(string gameId, string? path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Built-in profile directory not found: {Directory}")]
    private partial void LogBuiltInDirectoryMissing(string directory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read game profile {Path}")]
    private partial void LogProfileReadFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped invalid game profile {Path}: {Errors}")]
    private partial void LogProfileInvalid(string path, string errors);
}
