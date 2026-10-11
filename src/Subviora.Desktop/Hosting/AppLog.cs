using Microsoft.Extensions.Logging;

namespace Subviora.Desktop.Hosting;

/// <summary>Source-generated, allocation-free log messages for the application lifecycle.</summary>
internal static partial class AppLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Subviora {Version} ({Configuration}) starting. Data root: {DataRoot}")]
    public static partial void Starting(this ILogger logger, string version, string configuration, string dataRoot);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Main window shown")]
    public static partial void MainWindowShown(this ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Stopping host")]
    public static partial void StoppingHost(this ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Subviora exited with code {ExitCode}")]
    public static partial void Exited(this ILogger logger, int exitCode);

    [LoggerMessage(EventId = 10, Level = LogLevel.Critical, Message = "Invalid configuration: {Errors}")]
    public static partial void InvalidConfiguration(this ILogger logger, IReadOnlyList<string> errors);

    [LoggerMessage(EventId = 11, Level = LogLevel.Critical, Message = "Startup failed: {Reason}")]
    public static partial void StartupFailed(this ILogger logger, Exception exception, string reason);

    [LoggerMessage(EventId = 12, Level = LogLevel.Critical, Message = "Fatal error: {UserMessage}")]
    public static partial void FatalError(this ILogger logger, Exception exception, string userMessage);

    [LoggerMessage(EventId = 13, Level = LogLevel.Critical, Message = "Unhandled exception on a background thread (terminating: {IsTerminating})")]
    public static partial void UnhandledBackgroundException(this ILogger logger, Exception? exception, bool isTerminating);

    [LoggerMessage(EventId = 14, Level = LogLevel.Error, Message = "Unobserved task exception")]
    public static partial void UnobservedTaskException(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 15, Level = LogLevel.Error, Message = "Error during shutdown")]
    public static partial void ShutdownError(this ILogger logger, Exception exception);
}
