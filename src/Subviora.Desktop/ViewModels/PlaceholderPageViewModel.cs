namespace Subviora.Desktop.ViewModels;

/// <summary>A page for a feature that is planned but not implemented. Never shows simulated functionality.</summary>
public sealed class PlaceholderPageViewModel : PageViewModel
{
    public PlaceholderPageViewModel(
        string title,
        string subtitle,
        string glyph,
        string description,
        IReadOnlyList<string> plannedCapabilities,
        string plannedStage)
        : base(title, subtitle)
    {
        Glyph = glyph;
        Description = description;
        PlannedCapabilities = plannedCapabilities;
        PlannedStage = plannedStage;
    }

    public string Glyph { get; }

    public string Description { get; }

    public IReadOnlyList<string> PlannedCapabilities { get; }

    public string PlannedStage { get; }

    public static PlaceholderPageViewModel CreateTranslation() => new(
        "Çeviri",
        "Tanınan İngilizce metnin Türkçeye çevrilmesi.",
        "\uE774",
        "Çeviri motoru bu sürümde bulunmuyor. Bu sayfa, çeviri sağlayıcısı entegre edildiğinde etkinleşecek.",
        [
            "Çevrimdışı İngilizce → Türkçe çeviri motoru (varsayılan)",
            "SQLite tabanlı çeviri önbelleği",
            "İsteğe bağlı çevrimiçi çeviri sağlayıcıları (yalnızca kullanıcı onayıyla)",
            "Oyuna özgü terim sözlükleri",
        ],
        "Yol haritası: Aşama 4");

    public static PlaceholderPageViewModel CreateOverlay() => new(
        "Altyazı Katmanı",
        "Türkçe altyazıların oyunun üzerinde gösterilmesi.",
        "\uE7F0",
        "Altyazı katmanı bu sürümde bulunmuyor. Oyunun üzerinde hiçbir şey çizilmez.",
        [
            "Yerel Win32 + Direct2D/DirectWrite katmanı",
            "Tıklamaları geçiren, odak çalmayan pencere",
            "Profile göre konum, yazı boyutu ve arka plan ayarları",
            "Kenarlıksız pencere modunda çalışma",
        ],
        "Yol haritası: Aşama 5");
}
