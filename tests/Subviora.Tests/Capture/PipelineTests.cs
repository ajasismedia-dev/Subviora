using Subviora.Capture.Pipeline;
using Subviora.Core.Capture;
using Subviora.Core.Geometry;
using Subviora.Core.Vision;

namespace Subviora.Tests.Capture;

public sealed class PipelineTests
{
    private sealed class FastMockCaptureProvider : IScreenCaptureProvider
    {
        public string Id => "fast-mock-capture";

        public ValueTask<CapturedFrame> CaptureFrameAsync(CaptureTarget target, CancellationToken cancellationToken = default)
        {
            var frame = CapturedFrame.CreateEmpty(320, 240);
            return ValueTask.FromResult(frame);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SlowMockOcrProvider : IOcrProvider
    {
        private readonly int _delayMs;
        private int _currentConcurrentCount;
        private int _maxConcurrentCount;

        public string Id => "slow-mock-ocr";
        public string DisplayName => "Slow Mock OCR";
        public bool IsAvailable => true;
        public string? AvailabilityMessage => null;
        public int MaxConcurrentObserved => _maxConcurrentCount;

        public SlowMockOcrProvider(int delayMs)
        {
            _delayMs = delayMs;
        }

        public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            int current = Interlocked.Increment(ref _currentConcurrentCount);
            lock (this)
            {
                if (current > _maxConcurrentCount)
                {
                    _maxConcurrentCount = current;
                }
            }

            try
            {
                await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
                return new OcrResult([
                    new OcrTextLine("Mock subtitle text", new NormalizedRect(0.1, 0.8, 0.8, 0.1), 0.95)
                ]);
            }
            finally
            {
                Interlocked.Decrement(ref _currentConcurrentCount);
            }
        }
    }

    [Fact]
    public async Task Pipeline_DropsStaleFrames_WhenOcrIsSlowerThanCapture()
    {
        var capture = new FastMockCaptureProvider();
        var slowOcr = new SlowMockOcrProvider(delayMs: 120);

        var pipeline = new CaptureProcessingPipeline(capture, slowOcr)
        {
            OcrSamplingIntervalMs = 10 // Attempt OCR frequently
        };

        var target = new CaptureTarget(new nint(123), 1001);

        pipeline.Start(target);

        // Allow loop to capture multiple frames while OCR is busy with the first
        await Task.Delay(350, TestContext.Current.CancellationToken);

        pipeline.Stop();

        var diag = pipeline.CollectDiagnostics();

        Assert.True(diag.TotalFramesCaptured > 1, $"Expected captured frames > 1, got {diag.TotalFramesCaptured}");
        Assert.True(diag.DroppedFramesCount > 0, $"Expected dropped frames > 0, got {diag.DroppedFramesCount}");
        Assert.True(diag.TotalFramesCaptured > diag.TotalFramesProcessed, "Captured frames should exceed processed frames under load");
        Assert.Equal(1, slowOcr.MaxConcurrentObserved);

        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task Pipeline_CaptureOnce_ReturnsFrameAndUpdatesLatest()
    {
        var capture = new FastMockCaptureProvider();
        var ocr = new SlowMockOcrProvider(delayMs: 10);
        var pipeline = new CaptureProcessingPipeline(capture, ocr);
        var target = new CaptureTarget(new nint(456), 1002);

        var frame = await pipeline.CaptureOnceAsync(target, TestContext.Current.CancellationToken);

        Assert.NotNull(frame);
        Assert.Equal(320, frame.Width);
        Assert.Equal(240, frame.Height);
        Assert.Same(frame, pipeline.LatestFrame);

        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task Pipeline_StopAndDispose_ExecutesCleanly()
    {
        var capture = new FastMockCaptureProvider();
        var ocr = new SlowMockOcrProvider(delayMs: 10);
        var pipeline = new CaptureProcessingPipeline(capture, ocr);
        var target = new CaptureTarget(new nint(789), 1003);

        pipeline.Start(target);
        Assert.True(pipeline.IsRunning);

        pipeline.Stop();
        Assert.False(pipeline.IsRunning);

        await pipeline.DisposeAsync();

        // Calling start on disposed pipeline should throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => pipeline.Start(target));
    }

    [Fact]
    public async Task Pipeline_PassesConfiguredOcrRegions_ToOcrRequest()
    {
        var capture = new FastMockCaptureProvider();
        OcrRequest? capturedRequest = null;

        var mockOcr = new CapturingMockOcrProvider(req => capturedRequest = req);
        var pipeline = new CaptureProcessingPipeline(capture, mockOcr);

        var customRegion = new NormalizedRect(0.2, 0.75, 0.6, 0.2);
        pipeline.OcrRegions = [customRegion];

        var target = new CaptureTarget(new nint(999), 1004);
        var frame = await pipeline.CaptureOnceAsync(target, TestContext.Current.CancellationToken);

        // Run OCR tick
        pipeline.Start(target);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        pipeline.Stop();

        Assert.NotNull(capturedRequest);
        Assert.Single(capturedRequest.Regions);
        Assert.Equal(customRegion, capturedRequest.Regions[0]);

        await pipeline.DisposeAsync();
    }

    [Fact]
    public void Pipeline_Diagnostics_PopulatesProviderAndTargetDescription()
    {
        var capture = new FastMockCaptureProvider();
        var ocr = new SlowMockOcrProvider(delayMs: 10);
        var pipeline = new CaptureProcessingPipeline(capture, ocr);
        var target = new CaptureTarget(new nint(0x1234), 5678);

        pipeline.Start(target);
        var diag = pipeline.CollectDiagnostics();

        Assert.Equal("fast-mock-capture", diag.ProviderName);
        Assert.NotNull(diag.TargetWindowDescription);
        Assert.Contains("0x00001234", diag.TargetWindowDescription);
        Assert.Contains("5678", diag.TargetWindowDescription);
        Assert.False(diag.IsWindowIdle);

        pipeline.Stop();
    }

    [Fact]
    public async Task Pipeline_DecouplesStaleOcrFromCaptureTicks()
    {
        var capture = new FastMockCaptureProvider();
        var slowOcr = new SlowMockOcrProvider(delayMs: 150);
        var pipeline = new CaptureProcessingPipeline(capture, slowOcr)
        {
            OcrSamplingIntervalMs = 500
        };

        var target = new CaptureTarget(new nint(999), 1005);
        int ocrTicks = 0;
        int captureOnlyTicks = 0;

        pipeline.PipelineTicked += (frame, ocrResult, diag) =>
        {
            if (ocrResult is not null)
            {
                Interlocked.Increment(ref ocrTicks);
            }
            else
            {
                Interlocked.Increment(ref captureOnlyTicks);
            }
        };

        pipeline.Start(target);
        await Task.Delay(250, TestContext.Current.CancellationToken);
        pipeline.Stop();

        Assert.True(captureOnlyTicks > 0, $"Expected capture-only ticks > 0, got {captureOnlyTicks}");
        Assert.True(captureOnlyTicks > ocrTicks, $"Expected capture ticks ({captureOnlyTicks}) > ocr ticks ({ocrTicks})");

        await pipeline.DisposeAsync();
    }

    private sealed class CapturingMockOcrProvider : IOcrProvider
    {
        private readonly Action<OcrRequest> _onRequest;
        public CapturingMockOcrProvider(Action<OcrRequest> onRequest) => _onRequest = onRequest;

        public string Id => "capturing-ocr";
        public string DisplayName => "Capturing Mock OCR";
        public bool IsAvailable => true;
        public string? AvailabilityMessage => null;

        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            _onRequest(request);
            return Task.FromResult(new OcrResult([new OcrTextLine("captured", new NormalizedRect(0, 0, 1, 1), 1.0)]));
        }
    }
}

