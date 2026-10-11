using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Subviora.Capture.Pipeline;
using Subviora.Core.Capture;
using Subviora.Core.Geometry;
using Subviora.Core.Vision;
using Subviora.Desktop.Mvvm;
using Subviora.Infrastructure.Paths;
using Subviora.Vision.Benchmark;

using System.Windows.Input;

namespace Subviora.Desktop.ViewModels;

public sealed class OcrBoxDisplayItem
{
    public required string Text { get; init; }
    public required double Confidence { get; init; }
    public required double Left { get; init; }
    public required double Top { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public string ConfidencePercent => $"{Math.Round(Confidence * 100, 1)}%";
}

public sealed class OcrDiagnosticsViewModel : PageViewModel
{
    private readonly IWindowDiscoveryService _windowDiscovery;
    private readonly CaptureProcessingPipeline _pipeline;
    private readonly AppPaths _appPaths;
    private readonly ILogger<OcrDiagnosticsViewModel> _logger;

    private WindowInfo? _selectedWindow;
    private IOcrProvider? _selectedOcrProvider;
    private IScreenCaptureProvider? _selectedCaptureProvider;
    private WriteableBitmap? _previewBitmap;
    private ImageSource? _framePreviewImage;

    private string _statusText = "Boşta";
    private string _resolutionText = "—";
    private double _fps;
    private double _captureLatencyMs;
    private double _ocrLatencyMs;
    private double _memoryMb;
    private long _totalFramesCaptured;
    private long _totalFramesProcessed;
    private long _droppedFramesCount;
    private string? _lastCaptureError;
    private string? _lastOcrError;
    private string? _statusMessage;
    private string? _benchmarkSummary;

    public ObservableCollection<WindowInfo> AvailableWindows { get; } = [];
    public ObservableCollection<IOcrProvider> AvailableOcrProviders { get; } = [];
    public ObservableCollection<IScreenCaptureProvider> AvailableCaptureProviders { get; } = [];
    public ObservableCollection<OcrTextLine> RecognizedLines { get; } = [];
    public ObservableCollection<OcrBoxDisplayItem> DetectedBoundingBoxes { get; } = [];

    public ICommand RefreshWindowsCommand { get; }
    public ICommand StartCaptureCommand { get; }
    public ICommand StopCaptureCommand { get; }
    public ICommand CaptureOnceCommand { get; }
    public ICommand RunOcrOnceCommand { get; }
    public ICommand SaveScreenshotCommand { get; }
    public ICommand RunBenchmarkCommand { get; }

    public bool IsCapturing => _pipeline.IsRunning;

    public WindowInfo? SelectedWindow
    {
        get => _selectedWindow;
        set
        {
            if (SetProperty(ref _selectedWindow, value))
            {
                OnPropertyChanged(nameof(CanStartCapture));
                if (value is not null && IsCapturing)
                {
                    _pipeline.Start(value.ToCaptureTarget());
                }
            }
        }
    }

    public bool CanStartCapture => SelectedWindow is not null;

    public IOcrProvider? SelectedOcrProvider
    {
        get => _selectedOcrProvider;
        set
        {
            if (SetProperty(ref _selectedOcrProvider, value) && value is not null)
            {
                _pipeline.OcrProvider = value;
                OnPropertyChanged(nameof(OcrProviderAvailabilityText));
            }
        }
    }

    public string OcrProviderAvailabilityText =>
        SelectedOcrProvider?.IsAvailable == true
            ? "Kullanılabilir"
            : SelectedOcrProvider?.AvailabilityMessage ?? "Kullanılamıyor";

    public IScreenCaptureProvider? SelectedCaptureProvider
    {
        get => _selectedCaptureProvider;
        set
        {
            if (SetProperty(ref _selectedCaptureProvider, value) && value is not null)
            {
                _pipeline.CaptureProvider = value;
            }
        }
    }

    public ImageSource? FramePreviewImage
    {
        get => _framePreviewImage;
        private set => SetProperty(ref _framePreviewImage, value);
    }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string ResolutionText { get => _resolutionText; private set => SetProperty(ref _resolutionText, value); }
    public double Fps { get => _fps; private set => SetProperty(ref _fps, value); }
    public double CaptureLatencyMs { get => _captureLatencyMs; private set => SetProperty(ref _captureLatencyMs, value); }
    public double OcrLatencyMs { get => _ocrLatencyMs; private set => SetProperty(ref _ocrLatencyMs, value); }
    public double MemoryMb { get => _memoryMb; private set => SetProperty(ref _memoryMb, value); }
    public long TotalFramesCaptured { get => _totalFramesCaptured; private set => SetProperty(ref _totalFramesCaptured, value); }
    public long TotalFramesProcessed { get => _totalFramesProcessed; private set => SetProperty(ref _totalFramesProcessed, value); }
    public long DroppedFramesCount { get => _droppedFramesCount; private set => SetProperty(ref _droppedFramesCount, value); }
    public string? LastCaptureError { get => _lastCaptureError; private set => SetProperty(ref _lastCaptureError, value); }
    public string? LastOcrError { get => _lastOcrError; private set => SetProperty(ref _lastOcrError, value); }
    public string? StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string? BenchmarkSummary { get => _benchmarkSummary; private set => SetProperty(ref _benchmarkSummary, value); }

    public OcrDiagnosticsViewModel(
        IWindowDiscoveryService windowDiscovery,
        CaptureProcessingPipeline pipeline,
        IEnumerable<IOcrProvider> ocrProviders,
        IEnumerable<IScreenCaptureProvider> captureProviders,
        AppPaths appPaths,
        ILogger<OcrDiagnosticsViewModel> logger)
        : base("Yakalama & OCR Denetimi", "Pencere yakalama, OCR motoru değerlendirmesi ve hata ayıklama telemetrisi (AG-02).")
    {
        _windowDiscovery = windowDiscovery;
        _pipeline = pipeline;
        _appPaths = appPaths;
        _logger = logger;

        foreach (var p in ocrProviders)
        {
            AvailableOcrProviders.Add(p);
        }
        _selectedOcrProvider = AvailableOcrProviders.FirstOrDefault();

        foreach (var c in captureProviders)
        {
            AvailableCaptureProviders.Add(c);
        }
        _selectedCaptureProvider = AvailableCaptureProviders.FirstOrDefault();

        RefreshWindowsCommand = new AsyncRelayCommand(RefreshWindowsAsync);
        StartCaptureCommand = new RelayCommand(StartCapture, () => CanStartCapture && !IsCapturing);
        StopCaptureCommand = new RelayCommand(StopCapture, () => IsCapturing);
        CaptureOnceCommand = new AsyncRelayCommand(CaptureOnceAsync, () => CanStartCapture);
        RunOcrOnceCommand = new AsyncRelayCommand(RunOcrOnceAsync, () => _pipeline.LatestFrame is not null);
        SaveScreenshotCommand = new AsyncRelayCommand(SaveScreenshotAsync, () => _pipeline.LatestFrame is not null);
        RunBenchmarkCommand = new AsyncRelayCommand(RunBenchmarkAsync);

        _pipeline.PipelineTicked += OnPipelineTicked;
    }

    public override Task OnNavigatedToAsync()
    {
        return RefreshWindowsAsync();
    }

    private Task RefreshWindowsAsync()
    {
        AvailableWindows.Clear();
        var windows = _windowDiscovery.EnumerateEligibleWindows();
        foreach (var win in windows)
        {
            AvailableWindows.Add(win);
        }

        if (SelectedWindow is null || !AvailableWindows.Contains(SelectedWindow))
        {
            SelectedWindow = AvailableWindows.FirstOrDefault();
        }

        StatusMessage = AvailableWindows.Count > 0
            ? $"{AvailableWindows.Count} uygun pencere tespit edildi."
            : "Uygun açık uygulama penceresi bulunamadı.";

        return Task.CompletedTask;
    }

    private void StartCapture()
    {
        if (SelectedWindow is null)
        {
            return;
        }

        StatusMessage = $"{SelectedWindow.Title} için yakalama başlatılıyor...";
        _pipeline.Start(SelectedWindow.ToCaptureTarget());
        OnPropertyChanged(nameof(IsCapturing));
    }

    private void StopCapture()
    {
        _pipeline.Stop();
        StatusMessage = "Yakalama durduruldu.";
        OnPropertyChanged(nameof(IsCapturing));
    }

    private async Task CaptureOnceAsync()
    {
        if (SelectedWindow is null)
        {
            return;
        }

        try
        {
            StatusMessage = "Tek kare yakalanıyor...";
            var frame = await _pipeline.CaptureOnceAsync(SelectedWindow.ToCaptureTarget());
            UpdatePreview(frame);

            // Automatically run OCR on captured frame
            await RunOcrOnceAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Yakalama hatası: {ex.Message}";
            _logger.LogError(ex, "CaptureOnce failed");
        }
    }

    private async Task RunOcrOnceAsync()
    {
        var frame = _pipeline.LatestFrame;
        if (frame is null || SelectedOcrProvider is null)
        {
            return;
        }

        try
        {
            StatusMessage = $"{SelectedOcrProvider.DisplayName} ile OCR çalıştırılıyor...";
            var sw = Stopwatch.StartNew();
            var request = new OcrRequest(frame, _pipeline.OcrRegions, "en");
            var result = await SelectedOcrProvider.RecognizeAsync(request, CancellationToken.None);
            sw.Stop();

            OcrLatencyMs = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
            UpdateOcrResult(result, frame.Width, frame.Height);
            StatusMessage = $"OCR tamamlandı: {result.Lines.Count} satır tespit edildi ({OcrLatencyMs} ms).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"OCR hatası: {ex.Message}";
            _logger.LogError(ex, "OCR once failed");
        }
    }

    private async Task SaveScreenshotAsync()
    {
        var frame = _pipeline.LatestFrame;
        if (frame is null)
        {
            return;
        }

        try
        {
            string screenshotsDir = Path.Combine(_appPaths.RootDirectory, "Screenshots");
            Directory.CreateDirectory(screenshotsDir);

            string filename = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string fullPath = Path.Combine(screenshotsDir, filename);

            await _pipeline.SaveScreenshotAsync(fullPath, frame);
            StatusMessage = $"Ekran görüntüsü kaydedildi: {fullPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Görüntü kaydetme hatası: {ex.Message}";
            _logger.LogError(ex, "SaveScreenshot failed");
        }
    }

    private async Task RunBenchmarkAsync()
    {
        if (SelectedOcrProvider is null)
        {
            return;
        }

        StatusMessage = $"{SelectedOcrProvider.DisplayName} için karşılaştırmalı test yürütülüyor...";

        try
        {
            // Create synthetic subtitle benchmark samples
            var frame = _pipeline.LatestFrame ?? CapturedFrame.CreateEmpty(1920, 1080);
            var samples = new List<OcrBenchmarkSample>
            {
                new("sample_gta_01", frame, "What are you doing here, Michael?", [new NormalizedRect(0.1, 0.8, 0.8, 0.1)]),
                new("sample_gta_02", frame, "We need to get to the docks before sunset.", [new NormalizedRect(0.1, 0.8, 0.8, 0.1)]),
                new("sample_gta_03", frame, "Mission passed! Respect +", [new NormalizedRect(0.2, 0.4, 0.6, 0.2)])
            };

            var report = await OcrBenchmarkRunner.RunBenchmarkAsync(SelectedOcrProvider, samples, warmIterations: 2);
            BenchmarkSummary = $"Motor: {report.ProviderDisplayName} | Başarı: {report.SuccessfulSamples}/{report.TotalSamples} | " +
                               $"Soğuk Başlatma: {report.ColdStartDurationMs} ms | P50 Gecikme: {report.P50LatencyMs} ms | " +
                               $"P95: {report.P95LatencyMs} ms | Bellek: {report.MemoryDeltaMb} MB";

            StatusMessage = "Karşılaştırmalı test başarıyla tamamlandı.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Benchmark hatası: {ex.Message}";
            _logger.LogError(ex, "Benchmark failed");
        }
    }

    private void OnPipelineTicked(CapturedFrame frame, OcrResult? ocrResult, CaptureDiagnostics diag)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            StatusText = diag.Status;
            ResolutionText = $"{diag.FrameWidth} x {diag.FrameHeight} (BGRA32)";
            Fps = diag.Fps;
            CaptureLatencyMs = diag.CaptureLatencyMs;
            OcrLatencyMs = diag.OcrLatencyMs;
            MemoryMb = diag.MemoryWorkingSetMb;
            TotalFramesCaptured = diag.TotalFramesCaptured;
            TotalFramesProcessed = diag.TotalFramesProcessed;
            DroppedFramesCount = diag.DroppedFramesCount;
            LastCaptureError = diag.LastCaptureError;
            LastOcrError = diag.LastOcrError;

            UpdatePreview(frame);

            if (ocrResult is not null)
            {
                UpdateOcrResult(ocrResult, frame.Width, frame.Height);
            }
        });
    }

    private void UpdatePreview(CapturedFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.IsEmpty)
        {
            return;
        }

        if (_previewBitmap is null || _previewBitmap.PixelWidth != frame.Width || _previewBitmap.PixelHeight != frame.Height)
        {
            _previewBitmap = new WriteableBitmap(
                frame.Width,
                frame.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null);
            FramePreviewImage = _previewBitmap;
        }

        _previewBitmap.WritePixels(
            new Int32Rect(0, 0, frame.Width, frame.Height),
            frame.Pixels.ToArray(),
            frame.Stride,
            0);
    }

    private void UpdateOcrResult(OcrResult result, int frameWidth, int frameHeight)
    {
        RecognizedLines.Clear();
        DetectedBoundingBoxes.Clear();

        foreach (var line in result.Lines)
        {
            RecognizedLines.Add(line);

            // Scale normalized bounding box to preview dimensions (or display container)
            DetectedBoundingBoxes.Add(new OcrBoxDisplayItem
            {
                Text = line.Text,
                Confidence = line.Confidence,
                Left = line.Bounds.X * 100.0,
                Top = line.Bounds.Y * 100.0,
                Width = line.Bounds.Width * 100.0,
                Height = line.Bounds.Height * 100.0
            });
        }
    }
}
