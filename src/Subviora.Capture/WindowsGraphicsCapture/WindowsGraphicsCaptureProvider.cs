using Subviora.Capture.Interop;
using Subviora.Core.Capture;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace Subviora.Capture.WindowsGraphicsCapture;

/// <summary>
/// Hardware-accelerated screen capture provider using the Windows Graphics Capture API.
/// Suitable for windowed and borderless windowed game capture with minimal performance impact.
/// </summary>
public sealed class WindowsGraphicsCaptureProvider : IScreenCaptureProvider
{
    private IDirect3DDevice? _device;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private nint _activeWindowHandle = nint.Zero;
    private readonly object _syncLock = new();
    private bool _isDisposed;

    public string Id => "windows-graphics-capture";

    /// <summary>
    /// Checks whether Windows Graphics Capture is supported on the current operating system.
    /// </summary>
    public static bool IsSupported() => GraphicsCaptureSession.IsSupported();

    public async ValueTask<CapturedFrame> CaptureFrameAsync(CaptureTarget target, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!IsSupported())
        {
            throw new PlatformNotSupportedException("Windows Graphics Capture is not supported on this version of Windows.");
        }

        if (!CaptureInterop.IsWindow(target.WindowHandle))
        {
            throw new ArgumentException($"Target window handle 0x{target.WindowHandle:X8} is invalid or closed.", nameof(target));
        }

        if (CaptureInterop.IsIconic(target.WindowHandle))
        {
            throw new InvalidOperationException("Target window is minimized.");
        }

        EnsureCaptureSession(target.WindowHandle);

        var framePool = _framePool ?? throw new InvalidOperationException("Frame pool is not initialized.");
        var tcs = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            var nextFrame = sender.TryGetNextFrame();
            if (nextFrame is not null)
            {
                tcs.TrySetResult(nextFrame);
            }
        }

        framePool.FrameArrived += OnFrameArrived;

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            using var reg = linkedCts.Token.Register(() => tcs.TrySetCanceled(linkedCts.Token));

            var capturedFrame = await tcs.Task.ConfigureAwait(false);
            using (capturedFrame)
            {
                return await ExtractCpuFrameAsync(capturedFrame).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for frame from Windows Graphics Capture. The game window may be idle or occluded.");
        }
        finally
        {
            framePool.FrameArrived -= OnFrameArrived;
        }
    }

    private void EnsureCaptureSession(nint windowHandle)
    {
        lock (_syncLock)
        {
            if (_activeWindowHandle == windowHandle && _session is not null && _framePool is not null)
            {
                return;
            }

            TeardownCaptureSession();

            _device = CaptureInterop.CreateDirect3DDevice();
            _item = CaptureInterop.CreateItemForWindow(windowHandle);

            _item.Closed += OnItemClosed;

            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                _item.Size);

            _session = _framePool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = false;

            _session.StartCapture();
            _activeWindowHandle = windowHandle;
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        lock (_syncLock)
        {
            TeardownCaptureSession();
        }
    }

    private static async Task<CapturedFrame> ExtractCpuFrameAsync(Direct3D11CaptureFrame d3dFrame)
    {
        using var surface = d3dFrame.Surface;
        using var softwareBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(surface).AsTask().ConfigureAwait(false);

        int width = softwareBitmap.PixelWidth;
        int height = softwareBitmap.PixelHeight;
        int stride = width * 4;
        uint totalBytes = (uint)(stride * height);

        var winrtBuffer = new Windows.Storage.Streams.Buffer(totalBytes);
        softwareBitmap.CopyToBuffer(winrtBuffer);

        byte[] buffer = new byte[totalBytes];
        using (var dataReader = Windows.Storage.Streams.DataReader.FromBuffer(winrtBuffer))
        {
            dataReader.ReadBytes(buffer);
        }

        return new CapturedFrame(
            Width: width,
            Height: height,
            Stride: stride,
            PixelFormat: FramePixelFormat.Bgra32,
            Pixels: buffer,
            CapturedAt: DateTimeOffset.UtcNow);
    }

    private void TeardownCaptureSession()
    {
        if (_item is not null)
        {
            _item.Closed -= OnItemClosed;
            _item = null;
        }

        _session?.Dispose();
        _session = null;

        _framePool?.Dispose();
        _framePool = null;

        _device?.Dispose();
        _device = null;

        _activeWindowHandle = nint.Zero;
    }

    public ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            lock (_syncLock)
            {
                TeardownCaptureSession();
            }
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
