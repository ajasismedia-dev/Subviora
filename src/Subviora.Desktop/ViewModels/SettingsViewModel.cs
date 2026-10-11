using System.Windows.Input;
using Microsoft.Extensions.Options;
using Subviora.Desktop.Mvvm;
using Subviora.Desktop.Services;
using Subviora.Infrastructure.Configuration;
using Subviora.Infrastructure.Paths;

namespace Subviora.Desktop.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    public SettingsViewModel(IOptions<SubvioraOptions> options, AppPaths paths, ApplicationInfo appInfo, ShellLauncher shell)
        : base("Ayarlar", "Etkin yapılandırma ve uygulama klasörleri.")
    {
        var o = options.Value;
        EffectiveSettings =
        [
            new DetailItem("Günlük düzeyi", o.Logging.MinimumLevel.ToString()),
            new DetailItem("Saklanan günlük dosyası", $"{o.Logging.RetainedFileCount} dosya, en fazla {o.Logging.FileSizeLimitMegabytes} MB"),
            new DetailItem("Bildirim alanı simgesi", o.Shell.EnableTrayIcon ? "Açık" : "Kapalı"),
            new DetailItem("Kapatınca simge durumuna küçült", o.Shell.CloseToTray ? "Açık" : "Kapalı"),
        ];

        Folders =
        [
            new DetailItem("Veri klasörü", paths.RootDirectory, IsPath: true),
            new DetailItem("Günlükler", paths.LogsDirectory, IsPath: true),
            new DetailItem("Kullanıcı ayarları dosyası", paths.UserSettingsFile, IsPath: true),
            new DetailItem("Kullanıcı profilleri", paths.UserProfilesDirectory, IsPath: true),
            new DetailItem("Uygulama klasörü", appInfo.ApplicationDirectory, IsPath: true),
        ];

        OpenDataFolderCommand = new RelayCommand(() => shell.OpenFolder(paths.RootDirectory));
        OpenLogsFolderCommand = new RelayCommand(() => shell.OpenFolder(paths.LogsDirectory));
    }

    public IReadOnlyList<DetailItem> EffectiveSettings { get; }

    public IReadOnlyList<DetailItem> Folders { get; }

    public ICommand OpenDataFolderCommand { get; }

    public ICommand OpenLogsFolderCommand { get; }
}
