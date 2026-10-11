using System.Diagnostics;
using Subviora.Core.Capture;
using Subviora.Core.Geometry;
using Subviora.Core.Vision;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Subviora.Capture.Pipeline;

/// <summary>
/// Coordinates asynchronous game frame acquisition and bounded OCR dispatch.
/// Ensures latest frames are prioritized, stale frames are discarded, and threads remain unblocked.
/// </summary>
public sealed class CaptureProcessingPipeline : IAsyncDisposable
{
    private readonly SemaphoreSlim _ocrSemaphore = new(1, 1);
    private CancellationTokenSource? _pipelineCts;
    private Task? _pipelineTask;
    private CapturedFrame? _latestFrame;
    private OcrResult? _latestOcrResult;

    private long _totalFramesCaptured;
    private long _totalFramesProcessed;
    private long _droppedFramesCount;

    private double _lastCaptureLatencyMs;
    private double _lastOcrLatencyMs;
    private string? _lastCaptureError;
    private string? _lastOcrError;

    private readonly Stopwatch _fpsStopwatch = Stopwatch.StartNew();
    private int _framesSinceFpsSample;
    private double _currentFps;

    private DateTimeOffset _lastOcrTimestamp = DateTimeOffset.MinValue;
    private DateTimeOffset _lastFreshFrameTimestamp = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastObservedCapturedAt = DateTimeOffset.MinValue;
    private Task? _activeOcrTask;
    private TimeSpan _lastCpuTotalTime;
    private DateTimeOffset _lastCpuSampleTime = DateTimeOffset.UtcNow;
    private double _currentCpuPercent;
    private bool _isDisposed;

    public IScreenCaptureProvider CaptureProvider { get; set; }
    public IOcrProvider OcrProvider { get; set; }
    public CaptureTarget? Target { get; private set; }
    public bool IsRunning => _pipelineTask is { IsCompleted: false };
    public int OcrSamplingIntervalMs { get; set; } = 500;
    public IReadOnlyList<NormalizedRect> OcrRegions { get; set; } = [];
    public string LanguageTag { get; set; } = "en";
    public CapturedFrame? LatestFrame => _latestFrame;
    public OcrResult? LatestOcrResult => _latestOcrResult;

    public event Action<CapturedFrame, OcrResult?, CaptureDiagnostics>? PipelineTicked;

    public CaptureProcessingPipeline(IScreenCaptureProvider captureProvider, IOcrProvider ocrProvider)
    {
        CaptureProvider = captureProvider;
        OcrProvider = ocrProvider;
    }

    /// <summary>Starts continuous background capture for the given target.</summary>
    public void Start(CaptureTarget target)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (IsRunning)
        {
            Stop();
        }

        Target = target;
        _pipelineCts = new CancellationTokenSource();
        _totalFramesCaptured = 0;
        _totalFramesProcessed = 0;
        _droppedFramesCount = 0;
        _lastCaptureError = null;
        _lastOcrError = null;

        _pipelineTask = Task.Run(() => RunCaptureLoopAsync(_pipelineCts.Token));
    }

    /// <summary>Stops background capture.</summary>
    public void Stop()
    {
        if (_pipelineCts is not null)
        {
            _pipelineCts.Cancel();
            try
            {
                _pipelineTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Ignore task cancellation wait exceptions
            }

            try
            {
                _activeOcrTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Ignore OCR cancellation wait exceptions
            }

            _pipelineCts.Dispose();
            _pipelineCts = null;
            _pipelineTask = null;
            _activeOcrTask = null;
            _lastCaptureError = null;

            var diag = CollectDiagnostics();
            if (_latestFrame is not null)
            {
                PipelineTicked?.Invoke(_latestFrame, null, diag);
            }
        }
    }

    /// <summary>Resets cached frames, errors, statistics, and OCR results.</summary>
    public void Reset()
    {
        _latestFrame = null;
        _latestOcrResult = null;
        _lastCaptureError = null;
        _lastOcrError = null;
        _currentFps = 0;
        _lastCaptureLatencyMs = 0;
        _lastOcrLatencyMs = 0;
        Interlocked.Exchange(ref _totalFramesCaptured, 0);
        Interlocked.Exchange(ref _totalFramesProcessed, 0);
        Interlocked.Exchange(ref _droppedFramesCount, 0);
        _lastFreshFrameTimestamp = DateTimeOffset.MinValue;
        _lastObservedCapturedAt = DateTimeOffset.MinValue;
        _lastOcrTimestamp = DateTimeOffset.MinValue;
    }

    /// <summary>Captures a single frame synchronously on demand.</summary>
    public async Task<CapturedFrame> CaptureOnceAsync(CaptureTarget target, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var sw = Stopwatch.StartNew();
        try
        {
            var frame = await CaptureProvider.CaptureFrameAsync(target, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            _lastCaptureLatencyMs = sw.Elapsed.TotalMilliseconds;
            _latestFrame = frame;
            _lastCaptureError = null;
            return frame;
        }
        catch (Exception ex)
        {
            _lastCaptureError = ex.Message;
            throw;
        }
    }

    /// <summary>Saves a captured frame to disk as a PNG file for diagnostic purposes.</summary>
    public async Task SaveScreenshotAsync(string destinationPath, CapturedFrame? frameToSave = null)
    {
        var frame = frameToSave ?? _latestFrame;
        if (frame is null || frame.Pixels.IsEmpty)
        {
            throw new InvalidOperationException("No captured frame is available to save.");
        }

        string? dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var memoryStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memoryStream);

        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            (uint)frame.Width,
            (uint)frame.Height,
            96,
            96,
            frame.Pixels.ToArray());

        await encoder.FlushAsync();

        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        using var streamReader = memoryStream.AsStreamForRead();
        await streamReader.CopyToAsync(fileStream).ConfigureAwait(false);
    }

    private async Task RunCaptureLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (Target is null)
            {
                break;
            }

            try
            {
                var sw = Stopwatch.StartNew();
                var frame = await CaptureProvider.CaptureFrameAsync(Target, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                _lastCaptureLatencyMs = sw.Elapsed.TotalMilliseconds;
                _lastCaptureError = null;
                Interlocked.Increment(ref _totalFramesCaptured);

                if (frame.CapturedAt > _lastObservedCapturedAt)
                {
                    _lastFreshFrameTimestamp = DateTimeOffset.UtcNow;
                    _lastObservedCapturedAt = frame.CapturedAt;
                }

                UpdateFps();

                _latestFrame = frame;

                // Non-blocking OCR dispatch
                TryDispatchOcr(frame, cancellationToken);

                // Publish tick event with null ocrResult so newly arrived frames are not labeled with stale OCR text
                var diagnostics = CollectDiagnostics();
                PipelineTicked?.Invoke(frame, null, diagnostics);

                // Target ~30 FPS capture rate for background diagnostics loop
                await Task.Delay(33, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastCaptureError = ex.Message;
                var diagnostics = CollectDiagnostics();
                if (_latestFrame is not null)
                {
                    PipelineTicked?.Invoke(_latestFrame, null, diagnostics);
                }

                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void TryDispatchOcr(CapturedFrame frame, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if ((now - _lastOcrTimestamp).TotalMilliseconds < OcrSamplingIntervalMs)
        {
            return;
        }

        // Bounded concurrency: try to acquire OCR semaphore
        if (!_ocrSemaphore.Wait(0, cancellationToken))
        {
            // OCR is currently processing previous frame; drop this frame for OCR
            Interlocked.Increment(ref _droppedFramesCount);
            return;
        }

        _lastOcrTimestamp = now;

        _activeOcrTask = Task.Run(async () =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var request = new OcrRequest(frame, OcrRegions, LanguageTag);
                var result = await OcrProvider.RecognizeAsync(request, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                _lastOcrLatencyMs = sw.Elapsed.TotalMilliseconds;
                _lastOcrError = null;
                _latestOcrResult = result;
                Interlocked.Increment(ref _totalFramesProcessed);

                // Publish tick event with the fresh OCR result matched with its analyzed frame
                var diag = CollectDiagnostics();
                PipelineTicked?.Invoke(frame, result, diag);
            }
            catch (OperationCanceledException)
            {
                // Cancellation expected when capture stops
            }
            catch (Exception ex)
            {
                _lastOcrError = ex.Message;
            }
            finally
            {
                try
                {
                    _ocrSemaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Semaphore disposed on teardown
                }
            }
        }, CancellationToken.None);
    }

    private void UpdateFps()
    {
        _framesSinceFpsSample++;
        if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
        {
            _currentFps = (_framesSinceFpsSample * 1000.0) / _fpsStopwatch.ElapsedMilliseconds;
            _framesSinceFpsSample = 0;
            _fpsStopwatch.Restart();
        }
    }

    private double MeasureCpuUsage(Process proc)
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - _lastCpuSampleTime).TotalMilliseconds;
        if (elapsed < 300)
        {
            return _currentCpuPercent;
        }

        try
        {
            var totalTime = proc.TotalProcessorTime;
            var cpuUsedMs = (totalTime - _lastCpuTotalTime).TotalMilliseconds;
            _lastCpuTotalTime = totalTime;
            _lastCpuSampleTime = now;

            double cpuPercent = (cpuUsedMs / (elapsed * Environment.ProcessorCount)) * 100.0;
            _currentCpuPercent = Math.Clamp(Math.Round(cpuPercent, 1), 0.0, 100.0);
        }
        catch
        {
            // Ignore access errors
        }

        return _currentCpuPercent;
    }

    public CaptureDiagnostics CollectDiagnostics()
    {
        double cpuPercent = 0.0;
        double memMb = 0.0;

        try
        {
            using var proc = Process.GetCurrentProcess();
            memMb = proc.WorkingSet64 / (1024.0 * 1024.0);
            cpuPercent = MeasureCpuUsage(proc);
        }
        catch
        {
            // Diagnostics should never crash the pipeline
        }

        double timeSinceLastFreshMs = (DateTimeOffset.UtcNow - _lastFreshFrameTimestamp).TotalMilliseconds;
        bool isIdle = IsRunning && timeSinceLastFreshMs > 1000;

        string status;
        if (!IsRunning)
        {
            status = "Durduruldu";
        }
        else if (!string.IsNullOrEmpty(_lastCaptureError))
        {
            status = "Yakalama Hatası";
        }
        else
        {
            status = isIdle ? "Pencere Boşta (Değişiklik Yok)" : "Canlı Akış (Aktif)";
        }

        string? targetDesc = Target is not null
            ? $"HWND 0x{Target.WindowHandle:X8} (PID {Target.ProcessId})"
            : null;

        return new CaptureDiagnostics(
            Target: Target,
            Status: status,
            FrameWidth: _latestFrame?.Width ?? 0,
            FrameHeight: _latestFrame?.Height ?? 0,
            Fps: Math.Round(_currentFps, 1),
            CaptureLatencyMs: Math.Round(_lastCaptureLatencyMs, 1),
            OcrLatencyMs: Math.Round(_lastOcrLatencyMs, 1),
            CpuUsagePercent: cpuPercent,
            MemoryWorkingSetMb: Math.Round(memMb, 1),
            TotalFramesCaptured: Interlocked.Read(ref _totalFramesCaptured),
            TotalFramesProcessed: Interlocked.Read(ref _totalFramesProcessed),
            DroppedFramesCount: Interlocked.Read(ref _droppedFramesCount),
            LastCaptureError: _lastCaptureError,
            LastOcrError: _lastOcrError,
            ProviderName: CaptureProvider.Id,
            TargetWindowDescription: targetDesc,
            TimeSinceLastFreshFrameMs: Math.Round(timeSinceLastFreshMs, 0),
            IsWindowIdle: isIdle);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Stop();

            if (CaptureProvider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (CaptureProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _ocrSemaphore.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
