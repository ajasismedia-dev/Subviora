namespace Subviora.Infrastructure.Paths;

/// <summary>
/// Per-user local application data locations. Everything Subviora writes at runtime lives here;
/// the installation directory is treated as read-only so no administrator rights are needed.
/// </summary>
public sealed class AppPaths
{
    /// <summary>Folder name under %LOCALAPPDATA%.</summary>
    public const string ApplicationFolderName = "Subviora";

    /// <summary>Optional environment variable that relocates the data root (absolute path), e.g. for testing.</summary>
    public const string DataDirectoryEnvironmentVariable = "SUBVIORA_DATA_DIR";

    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (!Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException($"Data root '{rootDirectory}' must be an absolute path.", nameof(rootDirectory));
        }

        RootDirectory = Path.GetFullPath(rootDirectory);
        LogsDirectory = Path.Combine(RootDirectory, "Logs");
        ConfigDirectory = Path.Combine(RootDirectory, "Config");
        UserProfilesDirectory = Path.Combine(RootDirectory, "Profiles");
        CacheDirectory = Path.Combine(RootDirectory, "Cache");
        UserSettingsFile = Path.Combine(ConfigDirectory, "settings.json");
    }

    public string RootDirectory { get; }

    public string LogsDirectory { get; }

    public string ConfigDirectory { get; }

    /// <summary>User-created and user-overridden game profiles (separate from built-in profiles).</summary>
    public string UserProfilesDirectory { get; }

    /// <summary>Reserved for the future translation cache (SQLite).</summary>
    public string CacheDirectory { get; }

    /// <summary>Optional per-user settings file layered over the shipped appsettings.json.</summary>
    public string UserSettingsFile { get; }

    /// <summary>
    /// Resolves the default data root: <c>%SUBVIORA_DATA_DIR%</c> if set to an absolute path,
    /// otherwise <c>%LOCALAPPDATA%\Subviora</c>.
    /// </summary>
    public static AppPaths CreateDefault()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return new AppPaths(overrideRoot);
        }

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("The local application data folder could not be resolved for the current user.");
        }

        return new AppPaths(Path.Combine(localAppData, ApplicationFolderName));
    }

    /// <summary>Creates all directories if they do not exist. Throws if a directory cannot be created.</summary>
    public void EnsureCreated()
    {
        foreach (var directory in AllDirectories)
        {
            Directory.CreateDirectory(directory);
        }
    }

    public IReadOnlyList<string> AllDirectories =>
        [RootDirectory, LogsDirectory, ConfigDirectory, UserProfilesDirectory, CacheDirectory];
}
