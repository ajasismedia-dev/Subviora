namespace Subviora.Core.Capture;

/// <summary>
/// Information about an eligible top-level application window discovered on Windows.
/// </summary>
/// <param name="Handle">Native window handle (HWND).</param>
/// <param name="ProcessId">Process identifier owning the window.</param>
/// <param name="ProcessName">Process base name, e.g. "GTA5".</param>
/// <param name="ExecutableName">Executable file name, e.g. "GTA5.exe".</param>
/// <param name="Title">Window caption / title bar text.</param>
/// <param name="X">Window client area X position in screen coordinates.</param>
/// <param name="Y">Window client area Y position in screen coordinates.</param>
/// <param name="Width">Window client area width in pixels.</param>
/// <param name="Height">Window client area height in pixels.</param>
/// <param name="IsLikelyGame">True when the executable matches a known game profile or game criteria.</param>
/// <param name="MatchedGameId">Identifier of the matched game profile, or null if unprofiled.</param>
/// <param name="IsUniversalFallback">True if matched via Universal Mode wildcard profile.</param>
public sealed record WindowInfo(
    nint Handle,
    int ProcessId,
    string ProcessName,
    string ExecutableName,
    string Title,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsLikelyGame,
    string? MatchedGameId = null,
    bool IsUniversalFallback = false)
{
    /// <summary>Creates a <see cref="CaptureTarget"/> for this window.</summary>
    public CaptureTarget ToCaptureTarget() => new(Handle, ProcessId);

    /// <summary>Display string for UI selection lists.</summary>
    public string DisplayText =>
        string.IsNullOrEmpty(MatchedGameId) || IsUniversalFallback
            ? $"{Title} ({ExecutableName} · PID {ProcessId})"
            : $"[{MatchedGameId.ToUpperInvariant()}] {Title} ({ExecutableName} · PID {ProcessId})";

    /// <inheritdoc />
    public override string ToString() => DisplayText;
}
