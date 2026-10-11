namespace Subviora.Core.Profiles;

/// <summary>Provides game profiles. User profiles take precedence over built-in profiles with the same id.</summary>
public interface IGameProfileProvider
{
    /// <summary>Returns all effective profiles (after user overrides are applied), ordered by display name.</summary>
    Task<IReadOnlyList<GameProfile>> GetProfilesAsync(CancellationToken cancellationToken);

    /// <summary>Returns the effective profile with the given id, or null.</summary>
    Task<GameProfile?> GetByIdAsync(string gameId, CancellationToken cancellationToken);

    /// <summary>Returns the effective profile that matches an executable name or path, or null.</summary>
    Task<GameProfile?> FindByExecutableAsync(string executablePathOrName, CancellationToken cancellationToken);
}
