using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Subviora.Desktop.Hosting;
using Subviora.Desktop.Mvvm;
using Subviora.Desktop.Services;
using Subviora.Desktop.ViewModels;
using Subviora.Infrastructure;
using Subviora.Infrastructure.Configuration;
using Subviora.Infrastructure.Logging;
using Subviora.Infrastructure.Paths;

namespace Subviora.Desktop;

/// <summary>
/// Application lifecycle: single-instance guard → data directories → configuration (validated) →
/// structured logging → generic host + DI → main window and tray icon. Shutdown reverses the order.
/// </summary>
/// <remarks>
/// The fatal-error path (fields, <see cref="ReportFatal"/>, <see cref="OnExit"/>) deliberately depends only on
/// BCL, WPF and Microsoft.Extensions abstractions. If one of Subviora's own assemblies cannot be loaded
/// (missing file, or blocked by Windows Application Control / Smart App Control), the user still gets a
/// clear error dialog instead of a silent crash.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF Application instances are never disposed; owned resources are released in OnExit.")]
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Subviora.Desktop.SingleInstance";
    private const string DialogTitle = "Subviora";

    private Mutex? _instanceMutex;
    private IHost? _host;
    private Microsoft.Extensions.Logging.ILogger? _log;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Abstraction keeps Serilog out of the fatal-error path.")]
    private ILoggerFactory? _loggerFactory;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Abstraction keeps Serilog out of the fatal-error path.")]
    private IDisposable? _logOwner;
    private string? _logsDirectory;
    private int _fatalErrorReported;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalExceptionHandlers();

        _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            MessageBox.Show("Subviora zaten çalışıyor. Bildirim alanındaki Subviora simgesini kullanabilirsiniz.",
                DialogTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        try
        {
            StartApplication();
        }
        catch (StartupException ex)
        {
            _log?.StartupFailed(ex, ex.Message);
            ShowError(ex.Message);
            Shutdown(2);
        }
        catch (Exception ex)
        {
            ReportFatal(ex, "Subviora başlatılamadı.");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        try
        {
            if (_host is not null)
            {
                _log?.StoppingHost();
                _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                // Disposes singletons, including the tray icon (we are on the UI thread here).
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            // The process is exiting; record the failure rather than masking it.
            _log?.ShutdownError(ex);
        }
        finally
        {
            _log?.Exited(e.ApplicationExitCode);
            _log = null;
            _loggerFactory?.Dispose();
            _loggerFactory = null;
            _logOwner?.Dispose(); // Flushes the log file.
            _logOwner = null;

            if (_instanceMutex is not null)
            {
                _instanceMutex.ReleaseMutex();
                _instanceMutex.Dispose();
            }

            base.OnExit(e);
        }
    }

    private void StartApplication()
    {
        var appInfo = new ApplicationInfo(DateTimeOffset.Now);

        // 1. Local data directories (per-user, no elevation required).
        AppPaths paths;
        try
        {
            paths = AppPaths.CreateDefault();
            paths.EnsureCreated();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            throw new StartupException($"Uygulama veri klasörü oluşturulamadı.\n\n{ex.Message}", ex);
        }

        _logsDirectory = paths.LogsDirectory;

        // 2. Configuration: appsettings.json → user settings.json → SUBVIORA_ environment variables.
        IConfigurationRoot configuration;
        try
        {
            configuration = new ConfigurationBuilder()
                .AddSubvioraSources(appInfo.ApplicationDirectory, paths)
                .Build();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException)
        {
            InitializeLogging(paths, new LoggingSettings(), appInfo);
            throw new StartupException(
                $"Yapılandırma dosyası okunamadı.\n\n{ex.Message}\n\nKullanıcı ayarları: {paths.UserSettingsFile}", ex);
        }

        var load = SubvioraConfiguration.Load(configuration);

        // 3. Structured logging. If configuration is invalid, log with defaults so the failure is recorded.
        var serilog = InitializeLogging(paths, load.Options?.Logging ?? new LoggingSettings(), appInfo);
        var log = _log!;
        log.Starting(
            appInfo.Version, appInfo.BuildConfiguration, paths.RootDirectory);

        if (!load.IsValid)
        {
            log.InvalidConfiguration(load.Errors);
            throw new StartupException(
                "Yapılandırma geçersiz:\n\n• " + string.Join("\n• ", load.Errors)
                + $"\n\nKullanıcı ayarları: {paths.UserSettingsFile}");
        }

        var options = load.Options!;

        // 4. Generic host + dependency injection.
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Subviora",
            ContentRootPath = appInfo.ApplicationDirectory,
            EnvironmentName = Environments.Production,
        });
        builder.Configuration.AddConfiguration(configuration);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));
        builder.Services
            .AddSubvioraInfrastructure(builder.Configuration, paths, serilog)
            .AddSubvioraDesktop(appInfo, options, paths);

        _host = builder.Build();
        _host.Start(); // Runs options ValidateOnStart.

        // 5. Main window and tray icon.
        var services = _host.Services;
        var window = services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Closed += (_, _) => Shutdown(0);

        var shell = services.GetRequiredService<IOptions<SubvioraOptions>>().Value.Shell;
        if (shell.EnableTrayIcon)
        {
            var brandMark = (ImageSource)FindResource("BrandMarkImage");
            services.GetRequiredService<TrayIconService>().Show(brandMark, window.ShowAndActivate, window.CloseForExit);
        }

        window.Show();
        log.MainWindowShown();

        services.GetRequiredService<MainViewModel>().InitializeAsync().SurfaceExceptions();
    }

    private Serilog.Core.Logger InitializeLogging(AppPaths paths, LoggingSettings settings, ApplicationInfo appInfo)
    {
        var serilog = SubvioraLogging.CreateLogger(paths, settings, appInfo.Version);
        _logOwner = serilog;
        _loggerFactory = new Serilog.Extensions.Logging.SerilogLoggerFactory(serilog, dispose: false);
        _log = _loggerFactory.CreateLogger("Subviora.Desktop.App");
        return serilog;
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Marked handled only so the app can shut down in an orderly way (flush logs, remove the tray icon).
        // The error is always logged and shown to the user; the application does not continue running.
        e.Handled = true;
        ReportFatal(e.Exception, "Beklenmeyen bir hata oluştu ve Subviora kapatılacak.");
        Shutdown(1);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _log?.UnhandledBackgroundException(e.ExceptionObject as Exception, e.IsTerminating);
        if (e.IsTerminating)
        {
            _logOwner?.Dispose(); // Flush before the runtime terminates the process.
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Logged as an error; marking observed prevents a background fault from escalating to process termination.
        _log?.UnobservedTaskException(e.Exception);
        e.SetObserved();
    }

    private void ReportFatal(Exception exception, string userMessage)
    {
        _log?.FatalError(exception, userMessage);
        if (Interlocked.Exchange(ref _fatalErrorReported, 1) == 1)
        {
            return;
        }

        var hint = exception is FileLoadException { HResult: AppControlBlockedHResult }
            ? "\n\nWindows Uygulama Denetimi (ör. Akıllı Uygulama Denetimi) imzasız bir Subviora bileşenini engelledi. "
              + "Geliştirme derlemeleri imzalı değildir; resmî sürümler dijital olarak imzalanacaktır."
            : string.Empty;
        var logHint = _logsDirectory is null ? string.Empty : $"\n\nAyrıntılar günlük dosyasına yazıldı:\n{_logsDirectory}";
        ShowError($"{userMessage}\n\n{exception.GetType().Name}: {exception.Message}{hint}{logHint}");
    }

    /// <summary>HRESULT_FROM_WIN32(ERROR_SYSTEM_INTEGRITY_POLICY_VIOLATION) — blocked by Application Control.</summary>
    private const int AppControlBlockedHResult = unchecked((int)0x800711C7);

    private static void ShowError(string message) =>
        MessageBox.Show(message, DialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
}
