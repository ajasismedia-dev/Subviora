using Microsoft.Extensions.Logging;
using Subviora.Desktop.Mvvm;
using Subviora.Desktop.Services;

namespace Subviora.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;
    private NavigationItem _selectedItem;

    public MainViewModel(
        DashboardViewModel dashboard,
        GameProfilesViewModel gameProfiles,
        SettingsViewModel settings,
        ApplicationInfo appInfo,
        ILogger<MainViewModel> logger)
    {
        _logger = logger;
        NavigationItems =
        [
            new NavigationItem("Kontrol Paneli", "\uE80F", dashboard, IsImplemented: true),
            new NavigationItem("Oyun Profilleri", "\uE7FC", gameProfiles, IsImplemented: true),
            new NavigationItem("Çeviri", "\uE774", PlaceholderPageViewModel.CreateTranslation(), IsImplemented: false),
            new NavigationItem("Altyazı Katmanı", "\uE7F0", PlaceholderPageViewModel.CreateOverlay(), IsImplemented: false),
            new NavigationItem("Ayarlar", "\uE713", settings, IsImplemented: true),
        ];
        _selectedItem = NavigationItems[0];
        VersionText = $"Sürüm {appInfo.Version} · {appInfo.Stage}";
    }

    public IReadOnlyList<NavigationItem> NavigationItems { get; }

    public string VersionText { get; }

    public string StatusText { get; } = "Temel altyapı hazır";

    public NavigationItem SelectedItem
    {
        get => _selectedItem;
        set
        {
            // ListBox may push null while its items are re-templated; keep the current page in that case.
            if (value is null || !SetProperty(ref _selectedItem, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CurrentPage));
            NavigateAsync(value).SurfaceExceptions();
        }
    }

    public PageViewModel CurrentPage => _selectedItem.Page;

    /// <summary>Activates the initial page. Called once after the main window is shown.</summary>
    public Task InitializeAsync() => NavigateAsync(_selectedItem);

    private async Task NavigateAsync(NavigationItem item)
    {
        LogNavigated(item.Title);
        await item.Page.OnNavigatedToAsync();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Navigated to {Page}")]
    private partial void LogNavigated(string page);
}
