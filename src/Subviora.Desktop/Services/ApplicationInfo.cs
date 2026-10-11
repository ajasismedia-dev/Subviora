using System.Reflection;

namespace Subviora.Desktop.Services;

/// <summary>Immutable facts about the running application instance.</summary>
public sealed class ApplicationInfo
{
    public ApplicationInfo(DateTimeOffset startedAt)
    {
        StartedAt = startedAt;
        var assembly = typeof(ApplicationInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // Strip SourceLink commit metadata ("+<sha>") for display.
        Version = informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
        ApplicationDirectory = AppContext.BaseDirectory;
#if DEBUG
        BuildConfiguration = "Debug";
#else
        BuildConfiguration = "Release";
#endif
    }

    public string ProductName { get; } = "Subviora";

    /// <summary>Development stage of this build.</summary>
    public string Stage { get; } = "AG-01";

    public string Version { get; }

    public string BuildConfiguration { get; }

    public string ApplicationDirectory { get; }

    public DateTimeOffset StartedAt { get; }
}
