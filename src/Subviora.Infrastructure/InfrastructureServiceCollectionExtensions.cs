using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Subviora.Infrastructure.Configuration;
using Subviora.Infrastructure.Paths;

namespace Subviora.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers data paths, validated options and Serilog-backed <see cref="ILogger{T}"/>.
    /// The caller owns <paramref name="serilogLogger"/> and must dispose/flush it on shutdown.
    /// </summary>
    public static IServiceCollection AddSubvioraInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        AppPaths paths,
        Serilog.ILogger serilogLogger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(serilogLogger);

        services.AddSingleton(paths);

        services.AddSingleton<IValidateOptions<SubvioraOptions>, SubvioraOptionsValidator>();
        services.AddOptions<SubvioraOptions>()
            .Bind(configuration.GetSection(SubvioraOptions.SectionName), o => o.ErrorOnUnknownConfiguration = true)
            .ValidateOnStart();

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace); // Filtering is performed by Serilog.
            logging.AddSerilog(serilogLogger, dispose: false);
        });

        return services;
    }
}
