using System.Runtime.InteropServices;
using Subviora.Capture.Interop;
using Subviora.Core.Capture;

namespace Subviora.Capture.Gdi;

/// <summary>
/// Screen capture provider using Win32 GDI / PrintWindow and BitBlt.
/// Provides a zero-GPU fallback for windows or test environments where WGC is unavailable.
/// </summary>
public sealed class Win32BitBltCaptureProvider : IScreenCaptureProvider
{
    private bool _isDisposed;

    public string Id => "win32-bitblt";

    public ValueTask<CapturedFrame> CaptureFrameAsync(CaptureTarget target, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CaptureInterop.IsWindow(target.WindowHandle))
        {
            throw new ArgumentException($"Target window handle 0x{target.WindowHandle:X8} is invalid or closed.", nameof(target));
        }

        if (CaptureInterop.IsIconic(target.WindowHandle))
        {
            throw new InvalidOperationException("Target window is minimized.");
        }

        if (!CaptureInterop.GetClientRect(target.WindowHandle, out var clientRect) || clientRect.Width <= 0 || clientRect.Height <= 0)
        {
            throw new InvalidOperationException("Target window has no client area.");
        }

        int width = clientRect.Width;
        int height = clientRect.Height;

        nint hWindowDc = CaptureInterop.GetDC(target.WindowHandle);
        if (hWindowDc == nint.Zero)
        {
            throw new InvalidOperationException("Failed to acquire window device context.");
        }

        nint hMemDc = CaptureInterop.CreateCompatibleDC(hWindowDc);
        nint hBitmap = CaptureInterop.CreateCompatibleBitmap(hWindowDc, width, height);
        nint hOldBitmap = CaptureInterop.SelectObject(hMemDc, hBitmap);

        try
        {
            // First attempt PrintWindow with PW_RENDERFULLCONTENT
            bool captured = CaptureInterop.PrintWindow(target.WindowHandle, hMemDc, CaptureInterop.PW_RENDERFULLCONTENT);
            if (!captured)
            {
                // Fallback to BitBlt from screen
                var origin = new POINT { X = clientRect.Left, Y = clientRect.Top };
                CaptureInterop.ClientToScreen(target.WindowHandle, ref origin);
                nint hScreenDc = CaptureInterop.GetDC(nint.Zero);
                try
                {
                    captured = CaptureInterop.BitBlt(hMemDc, 0, 0, width, height, hScreenDc, origin.X, origin.Y, CaptureInterop.SRCCOPY);
                }
                finally
                {
                    _ = CaptureInterop.ReleaseDC(nint.Zero, hScreenDc);
                }
            }

            if (!captured)
            {
                throw new InvalidOperationException("Screen capture failed via PrintWindow and BitBlt.");
            }

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height; // Top-down DIB
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = CaptureInterop.BI_RGB;

            int stride = width * 4;
            byte[] pixels = new byte[stride * height];

            int linesCopied = CaptureInterop.GetDIBits(
                hMemDc,
                hBitmap,
                0,
                (uint)height,
                pixels,
                ref bmi,
                CaptureInterop.DIB_RGB_COLORS);

            if (linesCopied == 0)
            {
                throw new InvalidOperationException("Failed to read bitmap pixels from device context.");
            }

            return ValueTask.FromResult(new CapturedFrame(
                Width: width,
                Height: height,
                Stride: stride,
                PixelFormat: FramePixelFormat.Bgra32,
                Pixels: pixels,
                CapturedAt: DateTimeOffset.UtcNow));
        }
        finally
        {
            _ = CaptureInterop.SelectObject(hMemDc, hOldBitmap);
            _ = CaptureInterop.DeleteObject(hBitmap);
            _ = CaptureInterop.DeleteDC(hMemDc);
            _ = CaptureInterop.ReleaseDC(target.WindowHandle, hWindowDc);
        }
    }

    public ValueTask DisposeAsync()
    {
        _isDisposed = true;
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
