using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Subviora.Infrastructure.Configuration;
using Subviora.Infrastructure.Paths;
using Subviora.Tests.TestSupport;

namespace Subviora.Tests.Infrastructure;

public sealed class SubvioraOptionsValidatorTests
{
    [Fact]
    public void Defaults_AreValid() => Assert.Empty(SubvioraOptionsValidator.GetErrors(new SubvioraOptions()));

    [Fact]
    public void CloseToTray_WithoutTrayIcon_IsInvalid()
    {
        var options = new SubvioraOptions { Shell = { EnableTrayIcon = false, CloseToTray = true } };
        Assert.Single(SubvioraOptionsValidator.GetErrors(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void RetainedFileCount_OutOfRange_IsInvalid(int count)
    {
        var options = new SubvioraOptions { Logging = { RetainedFileCount = count } };
        Assert.NotEmpty(SubvioraOptionsValidator.GetErrors(options));
    }

    [Fact]
    public void LogLevelNone_IsInvalid()
    {
        var options = new SubvioraOptions { Logging = { MinimumLevel = LogLevel.None } };
        Assert.NotEmpty(SubvioraOptionsValidator.GetErrors(options));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"C:\Profiles")]
    public void BuiltInDirectory_EmptyOrAbsolute_IsInvalid(string dir)
    {
        var options = new SubvioraOptions { Profiles = { BuiltInDirectory = dir } };
        Assert.NotEmpty(SubvioraOptionsValidator.GetErrors(options));
    }

    [Fact]
    public void Validate_ReturnsFailedResultForInvalidOptions()
    {
        var result = new SubvioraOptionsValidator().Validate(null, new SubvioraOptions { Logging = { FileSizeLimitMegabytes = 0 } });
        Assert.True(result.Failed);
    }
}

public sealed class SubvioraConfigurationTests
{
    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void ShippedAppSettings_LoadAndValidate()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(TestFiles.AppSettingsPath, optional: false).Build();

        var result = SubvioraConfiguration.Load(configuration);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(LogLevel.Information, result.Options!.Logging.MinimumLevel);
        Assert.True(result.Options.Shell.EnableTrayIcon);
    }

    [Fact]
    public void EmptyConfiguration_UsesValidDefaults() =>
        Assert.True(SubvioraConfiguration.Load(Build([])).IsValid);

    [Fact]
    public void ValuesAreBound()
    {
        var result = SubvioraConfiguration.Load(Build(new()
        {
            ["Subviora:Logging:MinimumLevel"] = "Debug",
            ["Subviora:Logging:RetainedFileCount"] = "3",
        }));

        Assert.True(result.IsValid);
        Assert.Equal(LogLevel.Debug, result.Options!.Logging.MinimumLevel);
        Assert.Equal(3, result.Options.Logging.RetainedFileCount);
    }

    [Fact]
    public void UnknownKey_IsReportedNotIgnored()
    {
        var result = SubvioraConfiguration.Load(Build(new() { ["Subviora:Logging:MinimumLevl"] = "Debug" }));

        Assert.False(result.IsValid);
        Assert.Null(result.Options);
    }

    [Fact]
    public void UnparsableValue_IsReportedNotThrown()
    {
        var result = SubvioraConfiguration.Load(Build(new() { ["Subviora:Logging:RetainedFileCount"] = "many" }));

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void InvalidValue_IsReported()
    {
        var result = SubvioraConfiguration.Load(Build(new() { ["Subviora:Shell:EnableTrayIcon"] = "false", ["Subviora:Shell:CloseToTray"] = "true" }));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void UserSettingsFile_OverridesShippedDefaults()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();
        File.WriteAllText(paths.UserSettingsFile, """{ "Subviora": { "Logging": { "MinimumLevel": "Warning" } } }""");

        var configuration = new ConfigurationBuilder().AddSubvioraSources(AppContext.BaseDirectory, paths).Build();
        var result = SubvioraConfiguration.Load(configuration);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(LogLevel.Warning, result.Options!.Logging.MinimumLevel);
    }
}

public sealed class AppPathsTests
{
    [Fact]
    public void RelativeRoot_IsRejected() => Assert.Throws<ArgumentException>(() => new AppPaths("relative\\path"));

    [Fact]
    public void EnsureCreated_CreatesAllDirectoriesUnderRoot()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(Path.Combine(temp.Path, "Subviora"));

        paths.EnsureCreated();

        Assert.All(paths.AllDirectories, d => Assert.True(Directory.Exists(d), d));
        Assert.All(paths.AllDirectories, d => Assert.StartsWith(paths.RootDirectory, d, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith(paths.ConfigDirectory, paths.UserSettingsFile, StringComparison.OrdinalIgnoreCase);
    }
}
