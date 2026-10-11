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
        await File.WriteAllTextAsync(Path.Combine(_user, "game.json"), TestFiles.ValidProfileJson(displayName: "Customised"), Ct);

        var catalog = await _provider.LoadCatalogAsync(Ct);

        var profile = Assert.Single(catalog.Profiles);
        Assert.Equal("Customised", profile.DisplayName);
        Assert.Equal(ProfileSource.User, profile.Source);
        Assert.Equal(["test-game"], catalog.OverriddenBuiltInIds);

        // The built-in file itself is untouched.
        Assert.Contains("Built-in", await File.ReadAllTextAsync(Path.Combine(_builtIn, "game.json"), Ct), StringComparison.Ordinal);
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
