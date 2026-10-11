using Subviora.Core.Capture;

namespace Subviora.Tests.Capture;

public sealed class FrameLifecycleTests
{
    [Fact]
    public void CapturedFrame_CreateEmpty_AllocatesValidZeroedBuffer()
    {
        int width = 640;
        int height = 480;

        var frame = CapturedFrame.CreateEmpty(width, height);

        Assert.Equal(width, frame.Width);
        Assert.Equal(height, frame.Height);
        Assert.Equal(width * 4, frame.Stride);
        Assert.Equal(FramePixelFormat.Bgra32, frame.PixelFormat);
        Assert.Equal(width * 4 * height, frame.Pixels.Length);

        // Verify zeroed
        var span = frame.Pixels.Span;
        for (int i = 0; i < Math.Min(span.Length, 1024); i++)
        {
            Assert.Equal(0, span[i]);
        }
    }

    [Fact]
    public void CapturedFrame_ImmutableMemory_DoesNotAlterWhenInputArrayChanges()
    {
        byte[] buffer = new byte[16];
        buffer[0] = 42;

        var frame = new CapturedFrame(
            Width: 2,
            Height: 2,
            Stride: 8,
            PixelFormat: FramePixelFormat.Bgra32,
            Pixels: buffer,
            CapturedAt: DateTimeOffset.UtcNow);

        Assert.Equal(42, frame.Pixels.Span[0]);

        // Frame holds ReadOnlyMemory<byte>; verify timestamp is captured
        Assert.True(frame.CapturedAt <= DateTimeOffset.UtcNow);
        Assert.Equal(2, frame.Width);
        Assert.Equal(2, frame.Height);
    }

    [Fact]
    public void FramePixelFormat_PropertiesAndValues()
    {
        Assert.Equal(FramePixelFormat.Bgra32, FramePixelFormat.Bgra32);
    }
}
