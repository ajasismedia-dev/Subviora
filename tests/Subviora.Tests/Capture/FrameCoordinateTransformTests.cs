using Subviora.Core.Capture;
using Subviora.Core.Geometry;

namespace Subviora.Tests.Capture;

public sealed class FrameCoordinateTransformTests
{
    [Fact]
    public void CapturedFrame_Crop_ExtractsCorrectSubRegion()
    {
        // Create an 8x8 frame with 4 bytes per pixel (BGRA32)
        int width = 8;
        int height = 8;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        // Fill each pixel with (y * 10 + x) as the blue channel
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * stride) + (x * 4);
                pixels[offset] = (byte)((y * 10) + x); // B
                pixels[offset + 1] = 0;                // G
                pixels[offset + 2] = 0;                // R
                pixels[offset + 3] = 255;              // A
            }
        }

        var sourceFrame = new CapturedFrame(
            Width: width,
            Height: height,
            Stride: stride,
            PixelFormat: FramePixelFormat.Bgra32,
            Pixels: pixels,
            CapturedAt: DateTimeOffset.UtcNow);

        // Crop the central 4x4 region: x in [2..5], y in [2..5]
        // Normalized: x = 2/8 = 0.25, y = 2/8 = 0.25, width = 4/8 = 0.5, height = 4/8 = 0.5
        var cropRect = new NormalizedRect(0.25, 0.25, 0.5, 0.5);
        var cropped = sourceFrame.Crop(cropRect);

        Assert.Equal(4, cropped.Width);
        Assert.Equal(4, cropped.Height);
        Assert.Equal(4 * 4, cropped.Stride);
        Assert.Equal(FramePixelFormat.Bgra32, cropped.PixelFormat);

        var croppedSpan = cropped.Pixels.Span;
        for (int cy = 0; cy < cropped.Height; cy++)
        {
            for (int cx = 0; cx < cropped.Width; cx++)
            {
                int origX = 2 + cx;
                int origY = 2 + cy;
                byte expectedB = (byte)((origY * 10) + origX);

                int cropOffset = (cy * cropped.Stride) + (cx * 4);
                Assert.Equal(expectedB, croppedSpan[cropOffset]);
                Assert.Equal(255, croppedSpan[cropOffset + 3]); // Alpha
            }
        }
    }

    [Fact]
    public void CapturedFrame_Crop_FullFrame_PreservesDimensions()
    {
        var frame = CapturedFrame.CreateEmpty(1920, 1080);
        var cropped = frame.Crop(new NormalizedRect(0.0, 0.0, 1.0, 1.0));

        Assert.Equal(1920, cropped.Width);
        Assert.Equal(1080, cropped.Height);
    }

    [Fact]
    public void CapturedFrame_Crop_ZeroOrInvalidRect_ReturnsEmptyFrame()
    {
        var frame = CapturedFrame.CreateEmpty(100, 100);
        var cropped = frame.Crop(new NormalizedRect(0.5, 0.5, 0.0, 0.0));

        Assert.Equal(0, cropped.Width);
        Assert.Equal(0, cropped.Height);
        Assert.True(cropped.Pixels.IsEmpty);
    }

    [Fact]
    public void CoordinateConversion_ScreenToNormalizedAndBack()
    {
        int windowWidth = 2560;
        int windowHeight = 1440;

        // Subtitle box at bottom center in pixels
        int screenX = 512;
        int screenY = 1152;
        int screenW = 1536;
        int screenH = 216;

        // Convert to normalized coordinates
        double normX = (double)screenX / windowWidth;
        double normY = (double)screenY / windowHeight;
        double normW = (double)screenW / windowWidth;
        double normH = (double)screenH / windowHeight;

        var rect = new NormalizedRect(normX, normY, normW, normH);
        Assert.Equal(0.2, rect.X, 3);
        Assert.Equal(0.8, rect.Y, 3);
        Assert.Equal(0.6, rect.Width, 3);
        Assert.Equal(0.15, rect.Height, 3);

        // Convert back to pixel coordinates
        int backX = (int)Math.Round(rect.X * windowWidth);
        int backY = (int)Math.Round(rect.Y * windowHeight);
        int backW = (int)Math.Round(rect.Width * windowWidth);
        int backH = (int)Math.Round(rect.Height * windowHeight);

        Assert.Equal(screenX, backX);
        Assert.Equal(screenY, backY);
        Assert.Equal(screenW, backW);
        Assert.Equal(screenH, backH);
    }
}
