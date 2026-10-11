using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Subviora.Core.Profiles;
using Subviora.Desktop.Mvvm;
using Subviora.Desktop.Services;
using Subviora.GameProfiles.Storage;

namespace Subviora.Desktop.ViewModels;

/// <summary>Display model for a loaded profile.</summary>
public sealed record ProfileListItem(
    string DisplayName,
    string SourceText,
    bool IsUserProfile,
    string CalibrationText,
    bool IsCalibrated,
    string? CalibrationNotes,
    IReadOnlyList<DetailItem> Details);

/// <summary>Display model for a profile file that failed to load.</summary>
public sealed record ProfileIssueItem(string FilePath, string SourceText, string Errors);

public sealed partial class GameProfilesViewModel : PageViewModel
{
    private readonly FileSystemGameProfileProvider _provider;
    private readonly ShellLauncher _shell;
    private readonly ILogger<GameProfilesViewModel> _logger;
    private bool _isLoading;
    private string? _loadError;

    public GameProfilesViewModel(
        FileSystemGameProfileProvider provider,
        ShellLauncher shell,
        ILogger<GameProfilesViewModel> logger)
        : base("Oyun Profilleri", "Oyunların nasıl tanınacağını ve altyazıların nerede aranacağını tanımlayan profiller.")
    {
        _provider = provider;
        _shell = shell;
        _logger = logger;
        ReloadCommand = new RelayCommand(() => LoadAsync().SurfaceExceptions(), () => !IsLoading);
        OpenUserFolderCommand = new RelayCommand(() => _shell.OpenFolder(_provider.Options.UserDirectory));
    }

    public ObservableCollection<ProfileListItem> Profiles { get; } = [];

    public ObservableCollection<ProfileIssueItem> Issues { get; } = [];

    public ICommand ReloadCommand { get; }

    public ICommand OpenUserFolderCommand { get; }

    public string UserDirectory => _provider.Options.UserDirectory;

    public string BuiltInDirectory => _provider.Options.BuiltInDirectory;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string? LoadError
    {
        get => _loadError;
        private set
        {
            if (SetProperty(ref _loadError, value))
            {
                OnPropertyChanged(nameof(HasLoadError));
            }
        }
    }

    public bool HasLoadError => LoadError is not null;

    public bool HasNoProfiles => !IsLoading && Profiles.Count == 0 && !HasLoadError;

    public override Task OnNavigatedToAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        LoadError = null;
        try
        {
            var catalog = await _provider.LoadCatalogAsync(CancellationToken.None);
            Profiles.Clear();
            foreach (var profile in catalog.Profiles)
            {
                Profiles.Add(ToItem(profile, catalog.OverriddenBuiltInIds.Contains(profile.GameId, StringComparer.OrdinalIgnoreCase)));
            }

            Issues.Clear();
            foreach (var issue in catalog.Issues)
            {
                Issues.Add(new ProfileIssueItem(issue.FilePath, SourceText(issue.Source), string.Join(Environment.NewLine, issue.Errors)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogLoadFailed(ex);
            LoadError = $"Profiller okunamadı: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoProfiles));
        }
    }

    private static ProfileListItem ToItem(GameProfile p, bool overridesBuiltIn)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var sourceText = SourceText(p.Source) + (overridesBuiltIn ? " (yerleşik profili geçersiz kılar)" : string.Empty);
        var details = new List<DetailItem>
        {
            new("Oyun kimliği", p.GameId),
            new("Çalıştırılabilir dosyalar", string.Join(", ", p.Executables)),
            new("Diller", $"{p.SourceLanguage.ToUpper(tr)} → {p.TargetLanguage.ToUpper(tr)}"),
            new("Profil sürümü", $"{p.ProfileVersion} (şema v{p.SchemaVersion})"),
            new("Bölgeler", $"{p.SubtitleRegions.Count} altyazı bölgesi · {p.ExclusionRegions.Count} hariç tutma bölgesi"),
            new("Referans çözünürlük", $"{p.CoordinateSpace.ReferenceWidth} × {p.CoordinateSpace.ReferenceHeight} (normalize koordinatlar)"),
        };
        if (p.SourcePath is not null)
        {
            details.Add(new DetailItem("Dosya", p.SourcePath, IsPath: true));
        }

        var calibrated = p.Calibration == CalibrationStatus.Verified;
        return new ProfileListItem(
            p.DisplayName,
            sourceText,
            p.Source == ProfileSource.User,
            calibrated ? "Doğrulandı" : "Kalibre edilmedi",
            calibrated,
            p.CalibrationNotes,
            details);
    }

    private static string SourceText(ProfileSource source) => source switch
    {
        ProfileSource.BuiltIn => "Yerleşik",
        ProfileSource.User => "Kullanıcı",
        _ => source.ToString(),
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load game profiles")]
    private partial void LogLoadFailed(Exception exception);
}
