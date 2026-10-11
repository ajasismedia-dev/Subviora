using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using Subviora.Desktop.Services;
using Subviora.GameProfiles.Storage;
using Subviora.Infrastructure.Paths;

namespace Subviora.Desktop.ViewModels;

/// <summary>Status of a product module. Only real, verifiable states are shown.</summary>
public sealed record ModuleStatus(string Name, string Description, string StatusText, bool IsImplemented);

public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly FileSystemGameProfileProvider _profiles;
    private readonly ILogger<DashboardViewModel> _logger;
    private string _profileSummary = "Yükleniyor…";
    private string _profileIssueSummary = string.Empty;

    public DashboardViewModel(
        ApplicationInfo appInfo,
        AppPaths paths,
        FileSystemGameProfileProvider profiles,
        ILogger<DashboardViewModel> logger)
        : base("Kontrol Paneli", "Uygulamanın durumu ve modüllerin geliştirme aşaması.")
    {
        _profiles = profiles;
        _logger = logger;

        ApplicationDetails =
        [
            new DetailItem("Sürüm", $"{appInfo.Version} ({appInfo.BuildConfiguration})"),
            new DetailItem("Geliştirme aşaması", $"{appInfo.Stage} — temel altyapı"),
            new DetailItem("Yapılandırma", "Yüklendi ve doğrulandı"),
            new DetailItem("Günlük kaydı", "Etkin (yapılandırılmış JSON)"),
            new DetailItem("Başlatılma", appInfo.StartedAt.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("tr-TR"))),
            new DetailItem("Veri klasörü", paths.RootDirectory, IsPath: true),
        ];

        Modules =
        [
            new ModuleStatus("Ekran yakalama", "Windows Graphics Capture ile oyun penceresinin yakalanması.", "Uygulanmadı", false),
            new ModuleStatus("Metin tanıma (OCR)", "Değiştirilebilir OCR sağlayıcıları ile ekrandaki metnin okunması.", "Uygulanmadı", false),
            new ModuleStatus("Akıllı Arayüz Algılama", "Gerçek altyazıların menü ve gösterge metinlerinden ayrılması.", "Uygulanmadı", false),
            new ModuleStatus("Çevrimdışı çeviri", "İngilizce → Türkçe çeviri, internet bağlantısı gerektirmeden.", "Uygulanmadı", false),
            new ModuleStatus("Altyazı katmanı", "Türkçe altyazıların oyunun üzerinde yerel olarak gösterilmesi.", "Uygulanmadı", false),
            new ModuleStatus("Oyun profilleri", "JSON profillerin yüklenmesi ve doğrulanması. Oyun algılama henüz yok.", "Temel altyapı", true),
        ];
    }

    public IReadOnlyList<DetailItem> ApplicationDetails { get; }

    public IReadOnlyList<ModuleStatus> Modules { get; }

    public ObservableCollection<string> ProfileIssues { get; } = [];

    public string ProfileSummary
    {
        get => _profileSummary;
        private set => SetProperty(ref _profileSummary, value);
    }

    public string ProfileIssueSummary
    {
        get => _profileIssueSummary;
        private set => SetProperty(ref _profileIssueSummary, value);
    }

    public override async Task OnNavigatedToAsync()
    {
        try
        {
            var catalog = await _profiles.LoadCatalogAsync(CancellationToken.None);
            ProfileSummary = $"{catalog.Profiles.Count} profil yüklendi";
            ProfileIssueSummary = catalog.Issues.Count == 0
                ? "Sorun bulunmadı"
                : $"{catalog.Issues.Count} profil dosyası yüklenemedi (Oyun Profilleri sayfasına bakın)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogProfileSummaryFailed(ex);
            ProfileSummary = "Profiller okunamadı";
            ProfileIssueSummary = ex.Message;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load profile summary for dashboard")]
    private partial void LogProfileSummaryFailed(Exception exception);
}
