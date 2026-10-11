using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Subviora.Infrastructure.Configuration;
using Subviora.Infrastructure.Paths;

namespace Subviora.Infrastructure.Logging;

/// <summary>Creates the structured (compact JSON) rolling file logger.</summary>
public static class SubvioraLogging
{
    public const string LogFilePrefix = "subviora-";

    /// <summary>
    /// Creates a Serilog logger writing newline-delimited compact JSON (CLEF) to
    /// <c>Logs\subviora-YYYYMMDD.log</c>.
    /// </summary>
    public static Serilog.Core.Logger CreateLogger(AppPaths paths, LoggingSettings settings, string applicationVersion)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settings);

        return new LoggerConfiguration()
            .MinimumLevel.Is(ToSerilogLevel(settings.MinimumLevel))
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "Subviora")
            .Enrich.WithProperty("Version", applicationVersion)
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(paths.LogsDirectory, LogFilePrefix + ".log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: settings.RetainedFileCount,
                fileSizeLimitBytes: settings.FileSizeLimitMegabytes * 1024L * 1024L,
                rollOnFileSizeLimit: true,
                shared: false,
                flushToDiskInterval: TimeSpan.FromSeconds(2))
            .CreateLogger();
    }

    public static LogEventLevel ToSerilogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unsupported log level."),
    };
}
