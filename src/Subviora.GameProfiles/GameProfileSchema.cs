namespace Subviora.GameProfiles;

/// <summary>Constants describing the game profile JSON schema.</summary>
public static class GameProfileSchema
{
    /// <summary>The schema version this build reads and writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The only coordinate mode supported in schema version 1.</summary>
    public const string NormalizedCoordinateMode = "normalized";

    /// <summary>File extension used for profile documents.</summary>
    public const string FileExtension = ".json";
}
