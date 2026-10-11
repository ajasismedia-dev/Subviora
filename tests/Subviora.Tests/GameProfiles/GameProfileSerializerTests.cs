using Subviora.Core.Profiles;
using Subviora.GameProfiles.Serialization;
using Subviora.GameProfiles.Validation;
using Subviora.Tests.TestSupport;

namespace Subviora.Tests.GameProfiles;

public sealed class GameProfileSerializerTests
{
    [Fact]
    public void ShippedGta5Profile_IsValid_AndMarkedUncalibrated()
    {
        var json = File.ReadAllText(TestFiles.Gta5ProfilePath);

        var result = GameProfileSerializer.Parse(json, ProfileSource.BuiltIn, TestFiles.Gta5ProfilePath);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Errors));
        var profile = result.Profile!;
        Assert.Equal("gta5-story", profile.GameId);
        Assert.Equal(1, profile.SchemaVersion);
        Assert.Equal("en", profile.SourceLanguage);
        Assert.Equal("tr", profile.TargetLanguage);
        Assert.Equal(CalibrationStatus.Uncalibrated, profile.Calibration);
        Assert.NotEmpty(profile.SubtitleRegions);
        Assert.NotEmpty(profile.ExclusionRegions);
        Assert.True(profile.MatchesExecutable(@"C:\Games\GTAV\gta5.EXE"));
        Assert.Equal(ProfileSource.BuiltIn, profile.Source);
    }

    [Fact]
    public void AllShippedBuiltInProfiles_AreValid()
    {
        var files = Directory.GetFiles(TestFiles.BuiltInProfilesDirectory, "*.json");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var result = GameProfileSerializer.Parse(File.ReadAllText(file), ProfileSource.BuiltIn, file);
            Assert.True(result.IsSuccess, $"{Path.GetFileName(file)}: {string.Join("; ", result.Errors)}");
        }
    }

    [Fact]
    public void SerializeThenParse_RoundTripsAllFields()
    {
        var original = GameProfileSerializer.Parse(File.ReadAllText(TestFiles.Gta5ProfilePath), ProfileSource.BuiltIn).Profile!;

        var json = GameProfileSerializer.Serialize(original);
        var roundTripped = GameProfileSerializer.Parse(json, ProfileSource.BuiltIn).Profile!;

        Assert.Equal(original.GameId, roundTripped.GameId);
        Assert.Equal(original.ProfileVersion, roundTripped.ProfileVersion);
        Assert.Equal(original.Executables, roundTripped.Executables);
        Assert.Equal(original.SubtitleRegions, roundTripped.SubtitleRegions);
        Assert.Equal(original.ExclusionRegions, roundTripped.ExclusionRegions);
        Assert.Equal(original.Overlay, roundTripped.Overlay);
        Assert.Equal(original.CoordinateSpace, roundTripped.CoordinateSpace);
        Assert.Equal(original.CalibrationNotes, roundTripped.CalibrationNotes);
    }

    [Fact]
    public void Parse_UnsupportedSchemaVersion_IsRejected()
    {
        var json = TestFiles.ValidProfileJson().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);

        var result = GameProfileSerializer.Parse(json, ProfileSource.User);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("schemaVersion 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_UnknownProperty_IsRejected()
    {
        var json = TestFiles.ValidProfileJson().Replace("\"gameId\"", "\"subtitleRegionz\": [], \"gameId\"", StringComparison.Ordinal);

        var result = GameProfileSerializer.Parse(json, ProfileSource.User);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Parse_MissingRequiredProperty_IsRejected()
    {
        var json = TestFiles.ValidProfileJson().Replace("\"displayName\": \"Test Game\",", string.Empty, StringComparison.Ordinal);

        var result = GameProfileSerializer.Parse(json, ProfileSource.User);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Parse_MalformedInput_ReturnsErrorsWithoutThrowing(string json)
    {
        var result = GameProfileSerializer.Parse(json, ProfileSource.User);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Parse_UnsupportedCoordinateMode_IsRejected()
    {
        var json = TestFiles.ValidProfileJson().Replace("\"mode\": \"normalized\"", "\"mode\": \"pixels\"", StringComparison.Ordinal);

        var result = GameProfileSerializer.Parse(json, ProfileSource.User);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("coordinateSpace.mode", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_RegionOutsideUnitSquare_IsRejectedByValidator()
    {
        var json = TestFiles.ValidProfileJson().Replace("\"width\": 0.8", "\"width\": 1.8", StringComparison.Ordinal);

        var result = GameProfileSerializer.Parse(json, ProfileSource.User);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("'bottom'", StringComparison.Ordinal));
    }

    [Fact]
    public void TryReadGameId_ReturnsIdFromValidJson()
    {
        var json = """{ "schemaVersion": 1, "gameId": "extracted-id" }""";
        Assert.Equal("extracted-id", GameProfileSerializer.TryReadGameId(json));
    }

    [Fact]
    public void TryReadGameId_ReturnsNullForMalformedJson()
    {
        Assert.Null(GameProfileSerializer.TryReadGameId("{ not valid"));
        Assert.Null(GameProfileSerializer.TryReadGameId(string.Empty));
    }

    [Fact]
    public void Parse_WithBaseProfile_MergesPartialOverlaySuccessfully()
    {
        var baseProfile = GameProfileSerializer.Parse(TestFiles.ValidProfileJson(), ProfileSource.BuiltIn).Profile!;
        var overrideJson = """
            {
              "schemaVersion": 1,
              "gameId": "test-game",
              "overlay": { "fontScale": 2.5 }
            }
            """;

        var result = GameProfileSerializer.Parse(overrideJson, ProfileSource.User, baseProfile: baseProfile);

        Assert.True(result.IsSuccess);
        Assert.Equal(2.5, result.Profile!.Overlay.FontScale);
        Assert.Equal(baseProfile.Overlay.BackgroundOpacity, result.Profile.Overlay.BackgroundOpacity);
        Assert.True(result.Profile.IsUserOverride);
    }
}

public sealed class GameProfileValidatorTests
{
    private static GameProfile Valid() =>
        GameProfileSerializer.Parse(TestFiles.ValidProfileJson(), ProfileSource.User).Profile!;

    [Fact]
    public void Validate_ValidProfile_HasNoErrors() => Assert.Empty(GameProfileValidator.Validate(Valid()));

    [Fact]
    public void Validate_SameSourceAndTargetLanguage_IsError() =>
        Assert.NotEmpty(GameProfileValidator.Validate(Valid() with { TargetLanguage = "en" }));

    [Theory]
    [InlineData(@"C:\Games\Game.exe")]
    [InlineData("Game.bat")]
    [InlineData("")]
    public void Validate_InvalidExecutableName_IsError(string exe) =>
        Assert.NotEmpty(GameProfileValidator.Validate(Valid() with { Executables = [exe] }));

    [Theory]
    [InlineData("GTA5")]
    [InlineData("gta 5")]
    [InlineData("-gta5")]
    public void Validate_InvalidGameId_IsError(string id) =>
        Assert.NotEmpty(GameProfileValidator.Validate(Valid() with { GameId = id }));

    [Fact]
    public void Validate_DuplicateRegionIdsAcrossCollections_IsError()
    {
        var profile = Valid();
        var duplicate = profile with { ExclusionRegions = [profile.SubtitleRegions[0]] };

        Assert.Contains(GameProfileValidator.Validate(duplicate), e => e.Contains("not unique", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NoSubtitleRegions_IsError() =>
        Assert.NotEmpty(GameProfileValidator.Validate(Valid() with { SubtitleRegions = [] }));

    [Fact]
    public void Validate_OverlayOutOfRange_IsError() =>
        Assert.NotEmpty(GameProfileValidator.Validate(Valid() with { Overlay = OverlayPreferences.Default with { MaxLines = 0, BackgroundOpacity = 2 } }));

    [Fact]
    public void Validate_UniversalModeWildcardExecutable_IsValid() =>
        Assert.Empty(GameProfileValidator.Validate(Valid() with { Executables = ["*"] }));

    [Theory]
    [InlineData("1.0.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.0.0", "2.0.0", false)]
    [InlineData("0.1.0", "0.1.5", true)]
    [InlineData("0.1.0", "0.2.0", false)]
    [InlineData("^1.0.0", "1.2.3", true)]
    [InlineData("1.*", "1.4.0", true)]
    [InlineData("1.*", "2.0.0", false)]
    [InlineData("*", "any.version", true)]
    public void IsVersionCompatible_ValidatesSemVerRules(string target, string @base, bool expected)
    {
        Assert.Equal(expected, GameProfileValidator.IsVersionCompatible(target, @base));
    }
}
