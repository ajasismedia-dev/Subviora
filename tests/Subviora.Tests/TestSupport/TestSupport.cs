namespace Subviora.Tests.TestSupport;

/// <summary>A unique temporary directory deleted on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "subviora-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateSubdirectory(string name)
    {
        var dir = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal static class TestFiles
{
    public static string BuiltInProfilesDirectory =>
        Path.Combine(AppContext.BaseDirectory, "profiles", "builtin");

    public static string Gta5ProfilePath => Path.Combine(BuiltInProfilesDirectory, "gta5-story.json");

    public static string AppSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    /// <summary>A minimal valid schema v1 profile. Use <paramref name="gameId"/> / <paramref name="exe"/> to vary it.</summary>
    public static string ValidProfileJson(string gameId = "test-game", string exe = "Test.exe", string displayName = "Test Game") => $$"""
        {
          "schemaVersion": 1,
          "profileVersion": "1.0.0",
          "gameId": "{{gameId}}",
          "displayName": "{{displayName}}",
          "executables": [ "{{exe}}" ],
          "languages": { "source": "en", "target": "tr" },
          "coordinateSpace": { "mode": "normalized", "referenceWidth": 1920, "referenceHeight": 1080 },
          "calibration": { "status": "uncalibrated" },
          "subtitleRegions": [ { "id": "bottom", "x": 0.1, "y": 0.8, "width": 0.8, "height": 0.15 } ]
        }
        """;

    /// <summary>A minimal valid schema v1 partial override.</summary>
    public static string ValidOverrideJson(
        string gameId = "test-game",
        string? displayName = null,
        string? targetProfileVersion = null,
        double? fontScale = null)
    {
        var props = new List<string>
        {
            "\"schemaVersion\": 1",
            $"\"gameId\": \"{gameId}\"",
        };

        if (displayName is not null)
        {
            props.Add($"\"displayName\": \"{displayName}\"");
        }

        if (targetProfileVersion is not null)
        {
            props.Add($"\"targetProfileVersion\": \"{targetProfileVersion}\"");
        }

        if (fontScale.HasValue)
        {
            props.Add($"\"overlay\": {{ \"fontScale\": {fontScale.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}");
        }

        return "{\n  " + string.Join(",\n  ", props) + "\n}";
    }
}
