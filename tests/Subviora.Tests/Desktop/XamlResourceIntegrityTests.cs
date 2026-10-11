using System.Text.RegularExpressions;

namespace Subviora.Tests.Desktop;

/// <summary>
/// Regression protection for WPF XAML resources.
/// Prevents runtime XamlParseException crashes caused by missing StaticResource / DynamicResource keys,
/// such as the missing 'Button.Primary' or 'Font.Icon' typos.
/// </summary>
public sealed partial class XamlResourceIntegrityTests
{
    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Subviora.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Subviora.sln not found).");
    }

    [Fact]
    public void AllStaticAndDynamicResourceReferencesInDesktopXaml_MustResolveToDeclaredKeys()
    {
        var repoRoot = FindRepositoryRoot();
        var desktopDir = Path.Combine(repoRoot, "src", "Subviora.Desktop");
        Assert.True(Directory.Exists(desktopDir), $"Desktop project directory not found at {desktopDir}");

        var themeFile = Path.Combine(desktopDir, "Themes", "Theme.xaml");
        var sharedTemplatesFile = Path.Combine(desktopDir, "Views", "SharedTemplates.xaml");
        Assert.True(File.Exists(themeFile), $"Theme.xaml not found at {themeFile}");
        Assert.True(File.Exists(sharedTemplatesFile), $"SharedTemplates.xaml not found at {sharedTemplatesFile}");

        var globalKeys = new HashSet<string>(StringComparer.Ordinal);
        ExtractKeysFromFile(themeFile, globalKeys);
        ExtractKeysFromFile(sharedTemplatesFile, globalKeys);

        // Ensure baseline keys exist in theme
        Assert.Contains("Button.Primary", globalKeys);
        Assert.Contains("Button.Secondary", globalKeys);
        Assert.Contains("Button.Base", globalKeys);
        Assert.Contains("Font.Icons", globalKeys);
        Assert.Contains("BoolToVisibility", globalKeys);

        var xamlFiles = Directory.GetFiles(desktopDir, "*.xaml", SearchOption.AllDirectories);
        Assert.NotEmpty(xamlFiles);

        var missingReferences = new List<string>();

        foreach (var file in xamlFiles)
        {
            var relativePath = Path.GetRelativePath(repoRoot, file);
            var content = File.ReadAllText(file);

            // Extract local keys defined within this file
            var availableKeys = new HashSet<string>(globalKeys, StringComparer.Ordinal);
            ExtractKeysFromFile(file, availableKeys);

            // Match {StaticResource Key} and {DynamicResource Key}
            var matches = ResourceRegex().Matches(content);
            foreach (Match match in matches)
            {
                var resourceType = match.Groups[1].Value;
                var key = match.Groups[2].Value.Trim();

                if (!availableKeys.Contains(key))
                {
                    missingReferences.Add($"[{relativePath}] Missing {resourceType}: '{key}'");
                }
            }
        }

        Assert.Empty(missingReferences);
    }

    [Fact]
    public void ThemeXaml_ContainsRequiredButtonStyles()
    {
        var repoRoot = FindRepositoryRoot();
        var themeFile = Path.Combine(repoRoot, "src", "Subviora.Desktop", "Themes", "Theme.xaml");
        var content = File.ReadAllText(themeFile);

        Assert.Contains("x:Key=\"Button.Base\"", content, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Button.Secondary\"", content, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Button.Primary\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void OcrDiagnosticsView_DoesNotUseNullConverterOnVisibilityBindings()
    {
        var repoRoot = FindRepositoryRoot();
        var viewFile = Path.Combine(repoRoot, "src", "Subviora.Desktop", "Views", "OcrDiagnosticsView.xaml");
        var content = File.ReadAllText(viewFile);

        Assert.DoesNotContain("Converter={x:Null}", content, StringComparison.Ordinal);
        Assert.Contains("BoolToVisibility", content, StringComparison.Ordinal);
    }

    [Fact]
    public void OcrDiagnosticsView_UsesCorrectFontIconsResource()
    {
        var repoRoot = FindRepositoryRoot();
        var viewFile = Path.Combine(repoRoot, "src", "Subviora.Desktop", "Views", "OcrDiagnosticsView.xaml");
        var content = File.ReadAllText(viewFile);

        Assert.DoesNotContain("StaticResource Font.Icon}", content, StringComparison.Ordinal);
        Assert.Contains("StaticResource Font.Icons}", content, StringComparison.Ordinal);
    }

    private static void ExtractKeysFromFile(string filePath, HashSet<string> keySet)
    {
        var content = File.ReadAllText(filePath);
        var matches = KeyRegex().Matches(content);
        foreach (Match match in matches)
        {
            keySet.Add(match.Groups[1].Value.Trim());
        }
    }

    [GeneratedRegex(@"x:Key\s*=\s*""([^""]+)""")]
    private static partial Regex KeyRegex();

    [GeneratedRegex(@"\{(StaticResource|DynamicResource)\s+([^}]+)\}")]
    private static partial Regex ResourceRegex();
}
