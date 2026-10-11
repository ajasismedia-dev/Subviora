using Microsoft.Extensions.Configuration;
using Subviora.Infrastructure.Paths;

namespace Subviora.Infrastructure.Configuration;

/// <summary>Outcome of binding and validating configuration.</summary>
public sealed record OptionsLoadResult(SubvioraOptions? Options, IReadOnlyList<string> Errors)
{
    public bool IsValid => Options is not null && Errors.Count == 0;
}

/// <summary>Builds the layered configuration and binds <see cref="SubvioraOptions"/>.</summary>
public static class SubvioraConfiguration
{
    public const string AppSettingsFileName = "appsettings.json";
    public const string EnvironmentVariablePrefix = "SUBVIORA_";

    /// <summary>
    /// Adds configuration sources in increasing precedence:
    /// shipped appsettings.json → per-user settings.json → SUBVIORA_-prefixed environment variables.
    /// </summary>
    public static IConfigurationBuilder AddSubvioraSources(
        this IConfigurationBuilder builder,
        string applicationDirectory,
        AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentNullException.ThrowIfNull(paths);

        return builder
            .AddJsonFile(Path.Combine(applicationDirectory, AppSettingsFileName), optional: false, reloadOnChange: false)
            .AddJsonFile(paths.UserSettingsFile, optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(EnvironmentVariablePrefix);
    }

    /// <summary>Binds and validates the Subviora section. Binding errors are returned, not thrown.</summary>
    public static OptionsLoadResult Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = new SubvioraOptions();
        try
        {
            configuration.GetSection(SubvioraOptions.SectionName).Bind(options, o => o.ErrorOnUnknownConfiguration = true);
        }
        catch (InvalidOperationException ex)
        {
            return new OptionsLoadResult(null, [$"Configuration could not be read: {ex.Message}"]);
        }

        var errors = SubvioraOptionsValidator.GetErrors(options);
        return new OptionsLoadResult(errors.Count == 0 ? options : null, errors);
    }
}
