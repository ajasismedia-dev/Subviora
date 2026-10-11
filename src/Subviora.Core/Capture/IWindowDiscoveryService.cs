namespace Subviora.Core.Capture;

/// <summary>
/// Discovers and enumerates candidate application and game windows for screen capture.
/// Implementations must not read game process memory, inject DLLs, or interact with anti-cheat.
/// </summary>
public interface IWindowDiscoveryService
{
    /// <summary>
    /// Enumerates visible, non-minimized, top-level application windows eligible for capture.
    /// </summary>
    IReadOnlyList<WindowInfo> EnumerateEligibleWindows();

    /// <summary>
    /// Looks up an active window by its native handle, or returns null if not found or ineligible.
    /// </summary>
    WindowInfo? FindWindowByHandle(nint handle);
}
