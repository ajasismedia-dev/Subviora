using Subviora.Desktop.Mvvm;

namespace Subviora.Desktop.ViewModels;

/// <summary>Base class for navigable pages.</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(string title, string subtitle)
    {
        Title = title;
        Subtitle = subtitle;
    }

    public string Title { get; }

    public string Subtitle { get; }

    /// <summary>Called each time the page becomes visible. Implementations must handle their own errors.</summary>
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;
}

/// <summary>A label/value pair shown in detail lists.</summary>
public sealed record DetailItem(string Label, string Value, bool IsPath = false);

/// <summary>An entry in the sidebar.</summary>
/// <param name="Id">Stable programmatic identifier for UI automation.</param>
/// <param name="Title">Turkish display title.</param>
/// <param name="Glyph">Segoe Fluent Icons / MDL2 glyph.</param>
/// <param name="Page">Page shown when selected.</param>
/// <param name="IsImplemented">False shows a "Yakında" (coming soon) marker.</param>
public sealed record NavigationItem(string Id, string Title, string Glyph, PageViewModel Page, bool IsImplemented);
