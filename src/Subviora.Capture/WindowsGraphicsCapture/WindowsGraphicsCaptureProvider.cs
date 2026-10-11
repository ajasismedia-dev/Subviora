using Subviora.Capture.Interop;
using Subviora.Core.Capture;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace Subviora.Capture.WindowsGraphicsCapture;

/// <summary>
/// Hardware-accelerated screen capture provider using the Windows Graphics Capture API.
/// Refactored in AG-02.3 into a persistent, event-driven frame producer that maintains continuous
/// frame streaming, bounded buffering, and accurate idle/active window state tracking.
/// </summary>
public sealed class WindowsGraphicsCaptureProvider : IScreenCaptureProvider
{
    private readonly object _syncLock = new();
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly SemaphoreSlim _frameSignal = new(0, int.MaxValue);

    private IDirect3DDevice? _device;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;

    private nint _activeWindowHandle = nint.Zero;
    private SizeInt32 _lastContentSize;
    private CapturedFrame? _latestValidFrame;
    private DateTimeOffset _lastFrameReceivedUtc = DateTimeOffset.MinValue;

    private long _totalReceivedFrames;
    private long _droppedFramesCount;
    private int _isExtracting;
    private bool _isFreshFrameAvailable;
    private bool _isWindowClosed;
    private string? _lastError;
    private bool _isDisposed;

    public string Id => "windows-graphics-capture";

    /// <summary>Total frames received from the DWM composition pipeline.</summary>
    public long TotalReceivedFrames => Interlocked.Read(ref _totalReceivedFrames);

    /// <summary>Frames dropped due to bounded consumer processing capacity.</summary>
    public long DroppedFramesCount => Interlocked.Read(ref _droppedFramesCount);

    /// <summary>Timestamp of the most recent fresh frame arrival.</summary>
    public DateTimeOffset LastFrameReceivedUtc => _lastFrameReceivedUtc;

    /// <summary>Elapsed duration since the last fresh frame arrived from the captured window.</summary>
    public TimeSpan TimeSinceLastFreshFrame =>
        _lastFrameReceivedUtc == DateTimeOffset.MinValue
            ? TimeSpan.MaxValue
            : DateTimeOffset.UtcNow - _lastFrameReceivedUtc;

    /// <summary>Whether a capture session is currently active and receiving frames.</summary>
    public bool IsSessionActive => _session is not null && !_isWindowClosed && !_isDisposed;

    /// <summary>Last actionable error encountered by the capture engine.</summary>
    public string? LastError => _lastError;

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
            throw new InvalidOperationException("Hedef pencere simge durumunda (küçültülmüş). Yakalama için pencereyi görünür yapınız.");
        }

        await _captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCaptureSession(target.WindowHandle);

            // 1. If a fresh frame arrived recently and hasn't been read by this consumer, return immediately
            lock (_syncLock)
            {
                if (_isFreshFrameAvailable && _latestValidFrame is not null)
                {
                    _isFreshFrameAvailable = false;
                    return _latestValidFrame;
                }
            }

            // 2. Determine wait duration:
            // - Initial frame: wait up to 3000 ms to establish the stream.
            // - Subsequent streaming ticks: wait briefly (~60 ms) for DWM dirty updates.
            int waitTimeoutMs = _latestValidFrame is null ? 3000 : 60;

            bool signaled = false;
            try
            {
                signaled = await _frameSignal.WaitAsync(waitTimeoutMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Local wait timeout expired
            }

            lock (_syncLock)
            {
                if (_isWindowClosed)
                {
                    throw new InvalidOperationException("Hedef pencere yakalama sırasında kapatıldı.");
                }

                if (signaled && _latestValidFrame is not null)
                {
                    _isFreshFrameAvailable = false;
                    return _latestValidFrame;
                }

                // If timeout occurred, check if we have an existing valid frame (window is idle/static)
                if (_latestValidFrame is not null)
                {
                    // Return the existing frame; its CapturedAt timestamp reflects the true age of the frame
                    return _latestValidFrame;
                }
            }

            // If we have no frame at all after the initial wait timeout, verify window state and report error
            if (!CaptureInterop.IsWindow(target.WindowHandle))
            {
                throw new InvalidOperationException("Hedef pencere ilk kare gelmeden önce kapatıldı.");
            }

            if (CaptureInterop.IsIconic(target.WindowHandle))
            {
                throw new InvalidOperationException("Hedef pencere simge durumuna küçültüldü.");
            }

            throw new TimeoutException($"Windows Graphics Capture karesi zaman aşımına uğradı ({waitTimeoutMs} ms). Hedef pencere boşta veya kilitli olabilir.");
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private void EnsureCaptureSession(nint windowHandle)
    {
        lock (_syncLock)
        {
            if (_activeWindowHandle == windowHandle && _session is not null && _framePool is not null && !_isWindowClosed)
            {
                return;
            }

            TeardownCaptureSession();

            _device = CaptureInterop.CreateDirect3DDevice();
            _item = CaptureInterop.CreateItemForWindow(windowHandle);

            _item.Closed += OnItemClosed;

            var itemSize = _item.Size;
            if (itemSize.Width <= 0 || itemSize.Height <= 0)
            {
                throw new InvalidOperationException($"Windows Graphics Capture öğesi geçersiz boyut (0x0) bildirdi. Hedef pencere (HWND 0x{windowHandle:X8}) DWM kompozisyon yüzeyine sahip değil.");
            }

            _lastContentSize = itemSize;

            // Use double-buffering (bufferCount = 2) for smooth streaming without DWM starvation
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _lastContentSize);

            // Hook FrameArrived BEFORE starting the capture session
            _framePool.FrameArrived += OnFrameArrived;

            _session = _framePool.CreateCaptureSession(_item);
            try
            {
                _session.IsCursorCaptureEnabled = false;
            }
            catch
            {
                // Cursor capture toggle is optional; ignore if rejected by OS version
            }

            _session.StartCapture();
            _activeWindowHandle = windowHandle;
            _isWindowClosed = false;
            _lastError = null;
        }
    }

    private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            Direct3D11CaptureFrame? d3dFrame = null;
            lock (_syncLock)
            {
                if (_isDisposed || _session is null || _framePool is null)
                {
                    return;
                }

                try
                {
                    d3dFrame = sender.TryGetNextFrame();
                }
                catch (Exception ex)
                {
                    _lastError = $"TryGetNextFrame hatası: {ex.Message}";
                    return;
                }
            }

            if (d3dFrame is null)
            {
                return;
            }

            using (d3dFrame)
            {
                // 1. Handle window resize dynamically without dropping active session
                var contentSize = d3dFrame.ContentSize;
                if (contentSize.Width > 0 && contentSize.Height > 0)
                {
                    lock (_syncLock)
                    {
                        if (_device is not null && _framePool is not null &&
                            (contentSize.Width != _lastContentSize.Width || contentSize.Height != _lastContentSize.Height))
                        {
                            _lastContentSize = contentSize;
                            try
                            {
                                _framePool.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, contentSize);
                            }
                            catch (Exception ex)
                            {
                                _lastError = $"FramePool yeniden oluşturma hatası: {ex.Message}";
                            }
                        }
                    }
                }

                // 2. Bounded concurrency: if CPU extraction is already in progress, drop this frame to prevent buffer queues
                if (Interlocked.CompareExchange(ref _isExtracting, 1, 0) != 0)
                {
                    Interlocked.Increment(ref _droppedFramesCount);
                    return;
                }

                try
                {
                    var cpuFrame = await ExtractCpuFrameAsync(d3dFrame).ConfigureAwait(false);
                    lock (_syncLock)
                    {
                        _latestValidFrame = cpuFrame;
                        _lastFrameReceivedUtc = DateTimeOffset.UtcNow;
                        Interlocked.Increment(ref _totalReceivedFrames);
                        _isFreshFrameAvailable = true;
                        _lastError = null;
                    }

                    // Wake any pending consumer
                    try
                    {
                        if (_frameSignal.CurrentCount == 0)
                        {
                            _frameSignal.Release();
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // Signal disposed during teardown
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _isExtracting, 0);
                }
            }
        }
        catch (Exception ex)
        {
            _lastError = $"FrameArrived işleme hatası: {ex.Message}";
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        lock (_syncLock)
        {
            _isWindowClosed = true;
            _lastError = "Hedef pencere kullanıcı tarafından kapatıldı.";
            TeardownCaptureSession();
        }

        // Release signal so waiting consumers wake up and handle window closure
        try
        {
            if (_frameSignal.CurrentCount == 0)
            {
                _frameSignal.Release();
            }
        }
        catch (ObjectDisposedException)
        {
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
        lock (_syncLock)
        {
            if (_framePool is not null)
            {
                try
                {
                    _framePool.FrameArrived -= OnFrameArrived;
                }
                catch
                {
                }
            }

            if (_item is not null)
            {
                try
                {
                    _item.Closed -= OnItemClosed;
                }
                catch
                {
                }
                _item = null;
            }

            try
            {
                _session?.Dispose();
            }
            catch
            {
            }
            _session = null;

            try
            {
                _framePool?.Dispose();
            }
            catch
            {
            }
            _framePool = null;

            try
            {
                _device?.Dispose();
            }
            catch
            {
            }
            _device = null;

            _activeWindowHandle = nint.Zero;
            _isFreshFrameAvailable = false;
            _latestValidFrame = null;
            _lastFrameReceivedUtc = DateTimeOffset.MinValue;
            Interlocked.Exchange(ref _totalReceivedFrames, 0);
            Interlocked.Exchange(ref _droppedFramesCount, 0);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            TeardownCaptureSession();
            _frameSignal.Dispose();
            _captureGate.Dispose();
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
