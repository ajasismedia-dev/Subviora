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
    DateTimeOffset CapturedAt)
{
    /// <summary>Creates an empty, 1x1 black frame (used in fallbacks and tests).</summary>
    public static CapturedFrame CreateEmpty(int width = 1, int height = 1) =>
        new(width, height, width * 4, FramePixelFormat.Bgra32, new byte[width * height * 4], DateTimeOffset.UtcNow);

    /// <summary>
    /// Crops a sub-region from this frame specified in normalized coordinates [0, 1].
    /// </summary>
    public CapturedFrame Crop(Geometry.NormalizedRect region)
    {
        if (Width <= 0 || Height <= 0 || Pixels.IsEmpty || region.Width <= 0.0 || region.Height <= 0.0)
        {
            return new CapturedFrame(0, 0, 0, PixelFormat, ReadOnlyMemory<byte>.Empty, CapturedAt);
        }

        int cropX = Math.Clamp((int)Math.Round(region.X * Width), 0, Width - 1);
        int cropY = Math.Clamp((int)Math.Round(region.Y * Height), 0, Height - 1);
        int cropW = Math.Clamp((int)Math.Round(region.Width * Width), 1, Width - cropX);
        int cropH = Math.Clamp((int)Math.Round(region.Height * Height), 1, Height - cropY);

        int bytesPerPixel = PixelFormat == FramePixelFormat.Bgra32 ? 4 : 4;
        int newStride = cropW * bytesPerPixel;
        byte[] croppedBytes = new byte[newStride * cropH];
        var srcSpan = Pixels.Span;

        for (int row = 0; row < cropH; row++)
        {
            int srcOffset = (cropY + row) * Stride + cropX * bytesPerPixel;
            int dstOffset = row * newStride;
            int bytesToCopy = Math.Min(newStride, Math.Max(0, srcSpan.Length - srcOffset));
            if (bytesToCopy > 0)
            {
                srcSpan.Slice(srcOffset, bytesToCopy).CopyTo(croppedBytes.AsSpan(dstOffset, bytesToCopy));
            }
        }

        return new CapturedFrame(cropW, cropH, newStride, PixelFormat, croppedBytes, CapturedAt);
    }
}

/// <summary>Acquires frames from a game window. Planned implementation: Windows Graphics Capture.</summary>
public interface IScreenCaptureProvider : IAsyncDisposable
{
    /// <summary>Stable provider identifier, e.g. "windows-graphics-capture".</summary>
    string Id { get; }

    /// <summary>Captures the current contents of <paramref name="target"/>.</summary>
    ValueTask<CapturedFrame> CaptureFrameAsync(CaptureTarget target, CancellationToken cancellationToken);
}
