namespace Subviora.Core.Capture;

/// <summary>Identifies what a capture provider should capture.</summary>
/// <param name="WindowHandle">Native window handle (HWND) of the target game window.</param>
/// <param name="ProcessId">Process identifier of the game, used for diagnostics and validation.</param>
public sealed record CaptureTarget(nint WindowHandle, int ProcessId);

/// <summary>Pixel layouts a capture provider may produce.</summary>
public enum FramePixelFormat
{
    /// <summary>8 bits per channel, blue-green-red-alpha byte order (Windows Graphics Capture default).</summary>
    Bgra32 = 0,
}

/// <summary>
/// A single captured frame held in CPU memory.
/// This shape is provisional: AG-02+ may introduce GPU-resident frames for performance.
/// </summary>
public sealed record CapturedFrame(
    int Width,
    int Height,
    int Stride,
    FramePixelFormat PixelFormat,
    ReadOnlyMemory<byte> Pixels,
    DateTimeOffset CapturedAt);

/// <summary>Acquires frames from a game window. Planned implementation: Windows Graphics Capture.</summary>
public interface IScreenCaptureProvider : IAsyncDisposable
{
    /// <summary>Stable provider identifier, e.g. "windows-graphics-capture".</summary>
    string Id { get; }

    /// <summary>Captures the current contents of <paramref name="target"/>.</summary>
    ValueTask<CapturedFrame> CaptureFrameAsync(CaptureTarget target, CancellationToken cancellationToken);
}
