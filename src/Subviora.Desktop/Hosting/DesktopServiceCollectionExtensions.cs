using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Subviora.Core.Capture;
using Subviora.Core.Profiles;
using Subviora.Core.Vision;
using Subviora.Desktop.Services;
using Subviora.Desktop.ViewModels;
using Subviora.GameProfiles.Storage;
using Subviora.Infrastructure.Configuration;
using Subviora.Infrastructure.Paths;

namespace Subviora.Desktop.Hosting;

internal static class DesktopServiceCollectionExtensions
{
    public static IServiceCollection AddSubvioraDesktop(
        this IServiceCollection services,
        ApplicationInfo appInfo,
        SubvioraOptions options,
        AppPaths paths)
    {
        services.AddSingleton(appInfo);

        // The WPF Application owns the process lifetime; the generic host must not hook console events.
        services.AddSingleton<IHostLifetime, DesktopHostLifetime>();

        // Game profiles: read-only built-ins next to the executable, user profiles in local app data.
        var builtInDirectory = Path.GetFullPath(Path.Combine(appInfo.ApplicationDirectory, options.Profiles.BuiltInDirectory));
        services.AddSingleton(new GameProfileStoreOptions(builtInDirectory, paths.UserProfilesDirectory));
        services.AddSingleton<FileSystemGameProfileProvider>();
        services.AddSingleton<IGameProfileProvider>(sp => sp.GetRequiredService<FileSystemGameProfileProvider>());

        // Shell services
        services.AddSingleton<ShellLauncher>();
        services.AddSingleton<TrayIconService>();

        // Window Discovery
        services.AddSingleton<IWindowDiscoveryService, Capture.Discovery.Win32WindowDiscoveryService>();

        // Screen Capture Providers
        services.AddSingleton<Capture.WindowsGraphicsCapture.WindowsGraphicsCaptureProvider>();
        services.AddSingleton<Capture.Gdi.Win32BitBltCaptureProvider>();
        services.AddSingleton<IScreenCaptureProvider>(sp =>
            Capture.WindowsGraphicsCapture.WindowsGraphicsCaptureProvider.IsSupported()
                ? sp.GetRequiredService<Capture.WindowsGraphicsCapture.WindowsGraphicsCaptureProvider>()
                : sp.GetRequiredService<Capture.Gdi.Win32BitBltCaptureProvider>());
        services.AddSingleton<IEnumerable<IScreenCaptureProvider>>(sp =>
        [
            sp.GetRequiredService<Capture.WindowsGraphicsCapture.WindowsGraphicsCaptureProvider>(),
            sp.GetRequiredService<Capture.Gdi.Win32BitBltCaptureProvider>()
        ]);

        // OCR Providers
        services.AddSingleton<Vision.WindowsOcr.WindowsOcrProvider>();
        services.AddSingleton<Vision.Tesseract.TesseractOcrProvider>();
        services.AddSingleton<Vision.PaddleOcr.PaddleOcrBenchmarkAdapter>();
        services.AddSingleton<Vision.Mock.MockOcrProvider>();
        services.AddSingleton<IOcrProvider>(sp => sp.GetRequiredService<Vision.WindowsOcr.WindowsOcrProvider>());
        services.AddSingleton<IEnumerable<IOcrProvider>>(sp =>
        [
            sp.GetRequiredService<Vision.WindowsOcr.WindowsOcrProvider>(),
            sp.GetRequiredService<Vision.Tesseract.TesseractOcrProvider>(),
            sp.GetRequiredService<Vision.PaddleOcr.PaddleOcrBenchmarkAdapter>(),
            sp.GetRequiredService<Vision.Mock.MockOcrProvider>()
        ]);

        // Processing Pipeline
        services.AddSingleton(sp => new Capture.Pipeline.CaptureProcessingPipeline(
            sp.GetRequiredService<IScreenCaptureProvider>(),
            sp.GetRequiredService<Vision.WindowsOcr.WindowsOcrProvider>()));

        // View models and windows
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<GameProfilesViewModel>();
        services.AddSingleton<OcrDiagnosticsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}

/// <summary>
/// Host lifetime for a desktop application: startup and shutdown are driven by the WPF
/// <see cref="System.Windows.Application"/>, not by console signals.
/// </summary>
internal sealed class DesktopHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>A startup failure with a message suitable for showing to the user (Turkish).</summary>
internal sealed class StartupException : Exception
{
    public StartupException(string userMessage, Exception? innerException = null)
        : base(userMessage, innerException)
    {
    }

    public StartupException()
    {
    }

    public StartupException(string message)
        : base(message)
    {
    }
}
