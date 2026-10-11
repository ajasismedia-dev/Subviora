using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Subviora.Infrastructure.Configuration;

/// <summary>Validates <see cref="SubvioraOptions"/>. Invalid configuration stops startup with a clear message.</summary>
public sealed class SubvioraOptionsValidator : IValidateOptions<SubvioraOptions>
{
    public const int MaxRetainedFileCount = 365;
    public const int MaxFileSizeLimitMegabytes = 1024;

    public ValidateOptionsResult Validate(string? name, SubvioraOptions options)
    {
        var errors = GetErrors(options);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    public static IReadOnlyList<string> GetErrors(SubvioraOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        if (options.Logging is null)
        {
            errors.Add("Subviora:Logging section is missing.");
        }
        else
        {
            if (!Enum.IsDefined(options.Logging.MinimumLevel) || options.Logging.MinimumLevel == LogLevel.None)
            {
                errors.Add($"Subviora:Logging:MinimumLevel '{options.Logging.MinimumLevel}' is not a valid level (Trace, Debug, Information, Warning, Error, Critical).");
            }

            if (options.Logging.RetainedFileCount is < 1 or > MaxRetainedFileCount)
            {
                errors.Add($"Subviora:Logging:RetainedFileCount must be between 1 and {MaxRetainedFileCount}.");
            }

            if (options.Logging.FileSizeLimitMegabytes is < 1 or > MaxFileSizeLimitMegabytes)
            {
                errors.Add($"Subviora:Logging:FileSizeLimitMegabytes must be between 1 and {MaxFileSizeLimitMegabytes}.");
            }
        }

        if (options.Shell is null)
        {
            errors.Add("Subviora:Shell section is missing.");
        }
        else if (options.Shell.CloseToTray && !options.Shell.EnableTrayIcon)
        {
            errors.Add("Subviora:Shell:CloseToTray requires Subviora:Shell:EnableTrayIcon to be true.");
        }

        if (options.Profiles is null)
        {
            errors.Add("Subviora:Profiles section is missing.");
        }
        else
        {
            var dir = options.Profiles.BuiltInDirectory;
            if (string.IsNullOrWhiteSpace(dir))
            {
                errors.Add("Subviora:Profiles:BuiltInDirectory must not be empty.");
            }
            else if (Path.IsPathRooted(dir) || dir.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                errors.Add("Subviora:Profiles:BuiltInDirectory must be a valid path relative to the installation directory.");
            }
        }

        return errors;
    }
}
