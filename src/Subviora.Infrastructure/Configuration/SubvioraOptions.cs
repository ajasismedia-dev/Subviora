using Microsoft.Extensions.Logging;

namespace Subviora.Infrastructure.Configuration;

/// <summary>Root application settings, bound from the "Subviora" configuration section.</summary>
public sealed class SubvioraOptions
{
    public const string SectionName = "Subviora";

    public LoggingSettings Logging { get; set; } = new();

    public ShellSettings Shell { get; set; } = new();

    public ProfileSettings Profiles { get; set; } = new();
}

public sealed class LoggingSettings
{
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>Number of rolled log files to keep.</summary>
    public int RetainedFileCount { get; set; } = 14;

    /// <summary>Maximum size of a single log file before it rolls over.</summary>
    public int FileSizeLimitMegabytes { get; set; } = 20;
}

public sealed class ShellSettings
{
    /// <summary>Show an icon in the Windows notification area.</summary>
    public bool EnableTrayIcon { get; set; } = true;

    /// <summary>Closing the main window hides it to the tray instead of exiting. Requires <see cref="EnableTrayIcon"/>.</summary>
    public bool CloseToTray { get; set; }
}

public sealed class ProfileSettings
{
    /// <summary>Built-in profile directory, relative to the application installation directory.</summary>
    public string BuiltInDirectory { get; set; } = Path.Combine("profiles", "builtin");
}
