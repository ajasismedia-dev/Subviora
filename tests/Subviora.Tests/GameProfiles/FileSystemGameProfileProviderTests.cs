using Microsoft.Extensions.Logging.Abstractions;
using Subviora.Core.Profiles;
using Subviora.GameProfiles.Storage;
using Subviora.Tests.TestSupport;

namespace Subviora.Tests.GameProfiles;

public sealed class FileSystemGameProfileProviderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _builtIn;
    private readonly string _user;
    private readonly FileSystemGameProfileProvider _provider;

    public FileSystemGameProfileProviderTests()
    {
        _builtIn = _temp.CreateSubdirectory("builtin");
        _user = _temp.CreateSubdirectory("user");
        _provider = new FileSystemGameProfileProvider(
            new GameProfileStoreOptions(_builtIn, _user),
            NullLogger<FileSystemGameProfileProvider>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UserProfile_OverridesBuiltInProfileWithSameId()
    {
        await File.WriteAllTextAsync(Path.Combine(_builtIn, "game.json"), TestFiles.ValidProfileJson(displayName: "Built-in"), Ct);
        await File.WriteAllTextAsync(Path.Combine(_user, "game.json"), TestFiles.ValidOverrideJson(displayName: "Customised"), Ct);

        var catalog = await _provider.LoadCatalogAsync(Ct);

        var profile = Assert.Single(catalog.Profiles);
        Assert.Equal("Customised", profile.DisplayName);
        Assert.Equal(ProfileSource.User, profile.Source);
        Assert.True(profile.IsUserOverride);
        Assert.Equal("1.0.0", profile.BaseProfileVersion);
        Assert.Equal(["test-game"], catalog.OverriddenBuiltInIds);

        // The built-in file itself is untouched.
        Assert.Contains("Built-in", await File.ReadAllTextAsync(Path.Combine(_builtIn, "game.json"), Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserProfile_PartialOverride_MergesOntoBuiltIn_AndPreservesUnmodifiedFields()
    {
        var builtInJson = """
            {
              "schemaVersion": 1,
              "profileVersion": "1.0.0",
              "gameId": "layered-game",
              "displayName": "Layered Game",
              "executables": [ "Game.exe" ],
              "languages": { "source": "en", "target": "tr" },
              "coordinateSpace": { "mode": "normalized", "referenceWidth": 1920, "referenceHeight": 1080 },
              "calibration": { "status": "uncalibrated" },
              "subtitleRegions": [ { "id": "bottom", "x": 0.1, "y": 0.8, "width": 0.8, "height": 0.15 } ],
              "exclusionRegions": [ { "id": "hud", "x": 0.0, "y": 0.0, "width": 0.2, "height": 0.2 } ],
              "overlay": { "fontScale": 1.0, "backgroundOpacity": 0.6 }
            }
            """;
        var userOverrideJson = """
            {
              "schemaVersion": 1,
              "gameId": "layered-game",
              "overlay": { "fontScale": 1.5 }
            }
            """;

        await File.WriteAllTextAsync(Path.Combine(_builtIn, "game.json"), builtInJson, Ct);
        await File.WriteAllTextAsync(Path.Combine(_user, "game.json"), userOverrideJson, Ct);

        var catalog = await _provider.LoadCatalogAsync(Ct);

        Assert.Empty(catalog.Issues);
        var profile = Assert.Single(catalog.Profiles);
        Assert.Equal("Layered Game", profile.DisplayName);
        Assert.Equal(["Game.exe"], profile.Executables);
        Assert.Equal(1.5, profile.Overlay.FontScale);
        Assert.Equal(0.6, profile.Overlay.BackgroundOpacity); // preserved from base
        Assert.Single(profile.SubtitleRegions); // preserved from base
        Assert.Single(profile.ExclusionRegions); // preserved from base
        Assert.True(profile.IsUserOverride);
    }

    [Fact]
    public async Task UserProfile_BuiltInUpdate_PropagatesToEffectiveProfile_WithoutErasingUserCustomization()
    {
        var builtInV1 = """
            {
              "schemaVersion": 1,
              "profileVersion": "1.0.0",
              "gameId": "update-game",
              "displayName": "Update Game",
              "executables": [ "Game.exe" ],
              "languages": { "source": "en", "target": "tr" },
              "coordinateSpace": { "mode": "normalized", "referenceWidth": 1920, "referenceHeight": 1080 },
              "calibration": { "status": "uncalibrated" },
              "subtitleRegions": [ { "id": "bottom", "x": 0.1, "y": 0.8, "width": 0.8, "height": 0.15 } ],
              "overlay": { "fontScale": 1.0 }
            }
            """;
        var userOverride = """
            {
              "schemaVersion": 1,
              "gameId": "update-game",
              "overlay": { "fontScale": 1.75 }
            }
            """;

        await File.WriteAllTextAsync(Path.Combine(_builtIn, "game.json"), builtInV1, Ct);
        await File.WriteAllTextAsync(Path.Combine(_user, "game.json"), userOverride, Ct);

        var catalog1 = await _provider.LoadCatalogAsync(Ct);
        Assert.Equal(["Game.exe"], catalog1.Profiles[0].Executables);
        Assert.Equal(1.75, catalog1.Profiles[0].Overlay.FontScale);

        // Built-in profile is updated in a future release with a new executable and new exclusion region
        var builtInV2 = """
            {
              "schemaVersion": 1,
              "profileVersion": "1.1.0",
              "gameId": "update-game",
              "displayName": "Update Game",
              "executables": [ "Game.exe", "GameNew.exe" ],
              "languages": { "source": "en", "target": "tr" },
              "coordinateSpace": { "mode": "normalized", "referenceWidth": 1920, "referenceHeight": 1080 },
              "calibration": { "status": "verified" },
              "subtitleRegions": [ { "id": "bottom", "x": 0.1, "y": 0.8, "width": 0.8, "height": 0.15 } ],
              "exclusionRegions": [ { "id": "map", "x": 0.0, "y": 0.7, "width": 0.2, "height": 0.3 } ],
              "overlay": { "fontScale": 1.0 }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_builtIn, "game.json"), builtInV2, Ct);

        var catalog2 = await _provider.LoadCatalogAsync(Ct);
        var effective = catalog2.Profiles[0];

        // Built-in additions are received automatically
        Assert.Equal(["Game.exe", "GameNew.exe"], effective.Executables);
        Assert.Single(effective.ExclusionRegions);
        Assert.Equal("map", effective.ExclusionRegions[0].Id);
        Assert.Equal(CalibrationStatus.Verified, effective.Calibration);
        // User customization is preserved
        Assert.Equal(1.75, effective.Overlay.FontScale);
    }

    [Fact]
    public async Task UserProfile_RegionOperations_ReplaceAndRemove()
    {
        var builtInJson = """
            {
              "schemaVersion": 1,
              "profileVersion": "1.0.0",
              "gameId": "region-op-game",
              "displayName": "Region Op Game",
              "executables": [ "Game.exe" ],
              "languages": { "source": "en", "target": "tr" },
              "coordinateSpace": { "mode": "normalized", "referenceWidth": 1920, "referenceHeight": 1080 },
              "calibration": { "status": "uncalibrated" },
              "subtitleRegions": [
                { "id": "sub1", "x": 0.1, "y": 0.8, "width": 0.8, "height": 0.15, "description": "Original sub" }
              ],
              "exclusionRegions": [
                { "id": "exc1", "x": 0.0, "y": 0.0, "width": 0.2, "height": 0.2 },
                { "id": "exc2", "x": 0.8, "y": 0.0, "width": 0.2, "height": 0.2 }
              ]
            }
            """;
        var userOverride = """
            {
              "schemaVersion": 1,
              "gameId": "region-op-game",
              "subtitleRegions": [
                { "id": "sub1", "op": "replace", "x": 0.2, "y": 0.85, "width": 0.6, "height": 0.10 }
              ],
              "exclusionRegions": [
                { "id": "exc1", "op": "remove" }
              ]
            }
            """;

        await File.WriteAllTextAsync(Path.Combine(_builtIn, "game.json"), builtInJson, Ct);
        await File.WriteAllTextAsync(Path.Combine(_user, "game.json"), userOverride, Ct);

        var catalog = await _provider.LoadCatalogAsync(Ct);
        Assert.Empty(catalog.Issues);

        var profile = Assert.Single(catalog.Profiles);
        // Subtitle region was replaced with new coordinates, preserving description
        var sub = Assert.Single(profile.SubtitleRegions);
        Assert.Equal("sub1", sub.Id);
        Assert.Equal(0.2, sub.Bounds.X);
        Assert.Equal(0.85, sub.Bounds.Y);
        Assert.Equal("Original sub", sub.Description);

        // Exclusion region exc1 was removed, exc2 was kept
        var exc = Assert.Single(profile.ExclusionRegions);
        Assert.Equal("exc2", exc.Id);
    }

    [Fact]
    public async Task UserProfile_RegionOperation_RemoveNonExistentRegion_IsReportedAsError()
    {
        await File.WriteAllTextAsync(Path.Combine(_builtIn, "game.json"), TestFiles.ValidProfileJson(), Ct);
        var overrideJson = """
            {
              "schemaVersion": 1,
              "gameId": "test-game",
              "exclusionRegions": [
                { "id": "non-existent-region", "op": "remove" }
              ]
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_user, "game.json"), overrideJson, Ct);

        var catalog = await _provider.LoadCatalogAsync(Ct);

        // Built-in remains active and unharmed
        Assert.Single(catalog.Profiles);
        var issue = Assert.Single(catalog.Issues);
        Assert.Contains(issue.Errors, e => e.Contains("cannot remove region 'non-existent-region'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UserProfile_RegionOperation_RemoveAllSubtitleRegions_FailsValidation()
    {
        await File.WriteAllTextAsync(Path.Combine(_builtIn, "game.json"), TestFiles.ValidProfileJson(), Ct);
        var overrideJson = """
            {
              "schemaVersion": 1,
              "gameId": "test-game",
              "subtitleRegions": [
                { "id": "bottom", "op": "remove" }
              ]
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_user, "game.json"), overrideJson, Ct);

        var catalog = await _provider.LoadCatalogAsync(Ct);

        // Built-in remains active
        Assert.Single(catalog.Profiles);
        var issue = Assert.Single(catalog.Issues);
        Assert.Contains(issue.Errors, e => e.Contains("subtitleRegions must contain at least one region", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UserProfile_IncompatibleTargetProfileVersion_IsReportedAsIssue()
    {
        await File.WriteAllTextAsync(Path.Combine(_builtIn, "game.json"), TestFiles.ValidProfileJson(), Ct);
        var overrideJson = """
            {
              "schemaVersion": 1,
              "gameId": "test-game",
              "targetProfileVersion": "2.0.0",
              "overlay": { "fontScale": 1.2 }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_user, "game.json"), overrideJson, Ct);

        var catalog = await _provider.LoadCatalogAsync(Ct);

        Assert.Single(catalog.Profiles);
        var issue = Assert.Single(catalog.Issues);
        Assert.Contains(issue.Errors, e => e.Contains("is incompatible with built-in profile version '1.0.0'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UserProfile_StandaloneProfile_LoadsWhenNoBuiltInExists()
    {
        var standaloneJson = TestFiles.ValidProfileJson(gameId: "custom-game", exe: "Custom.exe", displayName: "Custom Game");
        await File.WriteAllTextAsync(Path.Combine(_user, "custom.json"), standaloneJson, Ct);

        var catalog = await _provider.LoadCatalogAsync(Ct);

        Assert.Empty(catalog.Issues);
        var profile = Assert.Single(catalog.Profiles);
        Assert.Equal("custom-game", profile.GameId);
        Assert.Equal(ProfileSource.User, profile.Source);
        Assert.False(profile.IsUserOverride);
    }

    [Fact]
    public async Task UserProfile_FullProfileBackwardCompatibility_LoadsWithoutMigrationBreakage()
    {
        await File.WriteAllTextAsync(Path.Combine(_builtIn, "game.json"), TestFiles.ValidProfileJson(displayName: "Built-in Name"), Ct);
        // User created a full whole-profile JSON under the AG-01 format
        await File.WriteAllTextAsync(Path.Combine(_user, "game.json"), TestFiles.ValidProfileJson(displayName: "AG01 Whole Profile"), Ct);

        var catalog = await _provider.LoadCatalogAsync(Ct);

        Assert.Empty(catalog.Issues);
        var profile = Assert.Single(catalog.Profiles);
        Assert.Equal("AG01 Whole Profile", profile.DisplayName);
        Assert.True(profile.IsUserOverride);
    }

    [Fact]
    public async Task InvalidFile_IsReportedAsIssue_AndValidProfilesStillLoad()
    {
        await File.WriteAllTextAsync(Path.Combine(_builtIn, "good.json"), TestFiles.ValidProfileJson(), Ct);
        await File.WriteAllTextAsync(Path.Combine(_user, "broken.json"), "{ \"schemaVersion\": 1 ", Ct);

        var catalog = await _provider.LoadCatalogAsync(Ct);

        Assert.Single(catalog.Profiles);
        var issue = Assert.Single(catalog.Issues);
        Assert.Equal(ProfileSource.User, issue.Source);
        Assert.EndsWith("broken.json", issue.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateIdWithinSameSource_SecondFileIsSkipped()
    {
        await File.WriteAllTextAsync(Path.Combine(_builtIn, "a.json"), TestFiles.ValidProfileJson(displayName: "First"), Ct);
        await File.WriteAllTextAsync(Path.Combine(_builtIn, "b.json"), TestFiles.ValidProfileJson(displayName: "Second"), Ct);

        var catalog = await _provider.LoadCatalogAsync(Ct);

        Assert.Equal("First", Assert.Single(catalog.Profiles).DisplayName);
        Assert.Single(catalog.Issues);
    }

    [Fact]
    public async Task MissingBuiltInDirectory_IsReported_MissingUserDirectoryIsNot()
    {
        var provider = new FileSystemGameProfileProvider(
            new GameProfileStoreOptions(Path.Combine(_temp.Path, "nope"), Path.Combine(_temp.Path, "nope-user")),
            NullLogger<FileSystemGameProfileProvider>.Instance);

        var catalog = await provider.LoadCatalogAsync(Ct);

        Assert.Empty(catalog.Profiles);
        Assert.Equal(ProfileSource.BuiltIn, Assert.Single(catalog.Issues).Source);
    }

    [Fact]
    public async Task FindByExecutable_MatchesFileNameCaseInsensitively()
    {
        await File.WriteAllTextAsync(Path.Combine(_builtIn, "game.json"), TestFiles.ValidProfileJson(exe: "Game.exe"), Ct);

        Assert.NotNull(await _provider.FindByExecutableAsync(@"D:\Steam\common\Game\GAME.EXE", Ct));
        Assert.Null(await _provider.FindByExecutableAsync("Other.exe", Ct));
        Assert.NotNull(await _provider.GetByIdAsync("TEST-GAME", Ct));
    }

    [Fact]
    public async Task FindByExecutable_SupportsUniversalMode_WithExactMatchPrecedence()
    {
        var specificJson = TestFiles.ValidProfileJson(gameId: "specific-game", exe: "Specific.exe", displayName: "Specific Game");
        var universalJson = TestFiles.ValidProfileJson(gameId: "universal-game", exe: "*", displayName: "Universal Fallback");

        await File.WriteAllTextAsync(Path.Combine(_builtIn, "specific.json"), specificJson, Ct);
        await File.WriteAllTextAsync(Path.Combine(_builtIn, "universal.json"), universalJson, Ct);

        // Specific executable matches specific profile
        var matchSpecific = await _provider.FindByExecutableAsync(@"C:\Games\Specific.exe", Ct);
        Assert.NotNull(matchSpecific);
        Assert.Equal("specific-game", matchSpecific.GameId);

        // Any other executable falls back to universal profile
        var matchUniversal = await _provider.FindByExecutableAsync(@"C:\Games\UnregisteredGame.exe", Ct);
        Assert.NotNull(matchUniversal);
        Assert.Equal("universal-game", matchUniversal.GameId);
    }

    [Fact]
    public async Task ShippedBuiltInDirectory_LoadsGta5WithoutIssues()
    {
        var provider = new FileSystemGameProfileProvider(
            new GameProfileStoreOptions(TestFiles.BuiltInProfilesDirectory, _user),
            NullLogger<FileSystemGameProfileProvider>.Instance);

        var catalog = await provider.LoadCatalogAsync(Ct);

        Assert.Empty(catalog.Issues);
        Assert.Contains(catalog.Profiles, p => p.GameId == "gta5-story");
    }
}
