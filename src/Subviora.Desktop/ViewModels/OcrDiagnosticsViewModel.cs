using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Subviora.Capture.Pipeline;
using Subviora.Core.Capture;
using Subviora.Core.Geometry;
using Subviora.Core.Profiles;
using Subviora.Core.Vision;
using Subviora.Desktop.Mvvm;
using Subviora.Infrastructure.Paths;
using Subviora.Vision.Benchmark;

namespace Subviora.Desktop.ViewModels;

public enum DiagnosticRegionMode
{
    FullFrame = 0,
    ProfileSubtitles = 1,
    ManualCustom = 2
}

public sealed record RegionModeOption(DiagnosticRegionMode Mode, string DisplayName);

public sealed class OcrBoxDisplayItem
{
    public required string Text { get; init; }
    public double? Confidence { get; init; }
    public required double Left { get; init; }
    public required double Top { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public string ConfidencePercent => Confidence.HasValue ? $"{Math.Round(Confidence.Value * 100, 1)}%" : "Belirtilmiyor";
}

public sealed class OcrRegionDisplayItem
{
    public required string Label { get; init; }
    public required double Left { get; init; }
    public required double Top { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public string StrokeBrush { get; init; } = "#A3E635";
}

public sealed class OcrDiagnosticsViewModel : PageViewModel
{
    private readonly IWindowDiscoveryService _windowDiscovery;
    private readonly CaptureProcessingPipeline _pipeline;
    private readonly IGameProfileProvider _profileProvider;
    private readonly AppPaths _appPaths;
    private readonly ILogger<OcrDiagnosticsViewModel> _logger;

    private WindowInfo? _selectedWindow;
    private IOcrProvider? _selectedOcrProvider;
    private IScreenCaptureProvider? _selectedCaptureProvider;
    private RegionModeOption? _selectedRegionMode;
    private WriteableBitmap? _previewBitmap;
    private ImageSource? _framePreviewImage;

    private int _nativeFrameWidth = 1920;
    private int _nativeFrameHeight = 1080;

    private double _manualRegionX = 0.10;
    private double _manualRegionY = 0.78;
    private double _manualRegionWidth = 0.80;
    private double _manualRegionHeight = 0.15;

    private string _manualRegionXText = "0.10";
    private string _manualRegionYText = "0.78";
    private string _manualRegionWidthText = "0.80";
    private string _manualRegionHeightText = "0.15";

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
    public ObservableCollection<RegionModeOption> AvailableRegionModes { get; } = [];
    public ObservableCollection<OcrTextLine> RecognizedLines { get; } = [];
    public ObservableCollection<OcrRegionDisplayItem> ActiveRegionBoxes { get; } = [];
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
                _ = UpdateActiveRegionsAsync();
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
            if (SetProperty(ref _selectedOcrProvider, value))
            {
                if (value is not null)
                {
                    _pipeline.OcrProvider = value;
                }
                OnPropertyChanged(nameof(OcrProviderAvailabilityText));
                OnPropertyChanged(nameof(CanRunOcr));
                OnPropertyChanged(nameof(CanRunBenchmark));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public RegionModeOption? SelectedRegionMode
    {
        get => _selectedRegionMode;
        set
        {
            if (SetProperty(ref _selectedRegionMode, value))
            {
                OnPropertyChanged(nameof(IsManualRegionVisible));
                _ = UpdateActiveRegionsAsync();
            }
        }
    }

    public bool IsManualRegionVisible => SelectedRegionMode?.Mode == DiagnosticRegionMode.ManualCustom;

    public int NativeFrameWidth
    {
        get => _nativeFrameWidth;
        private set => SetProperty(ref _nativeFrameWidth, value);
    }

    public int NativeFrameHeight
    {
        get => _nativeFrameHeight;
        private set => SetProperty(ref _nativeFrameHeight, value);
    }

    public string ManualRegionXText
    {
        get => _manualRegionXText;
        set
        {
            if (SetProperty(ref _manualRegionXText, value) &&
                double.TryParse(value.Replace(',', '.'), CultureInfo.InvariantCulture, out var parsed))
            {
                _manualRegionX = Math.Clamp(parsed, 0.0, 0.99);
                _ = UpdateActiveRegionsAsync();
            }
        }
    }

    public string ManualRegionYText
    {
        get => _manualRegionYText;
        set
        {
            if (SetProperty(ref _manualRegionYText, value) &&
                double.TryParse(value.Replace(',', '.'), CultureInfo.InvariantCulture, out var parsed))
            {
                _manualRegionY = Math.Clamp(parsed, 0.0, 0.99);
                _ = UpdateActiveRegionsAsync();
            }
        }
    }

    public string ManualRegionWidthText
    {
        get => _manualRegionWidthText;
        set
        {
            if (SetProperty(ref _manualRegionWidthText, value) &&
                double.TryParse(value.Replace(',', '.'), CultureInfo.InvariantCulture, out var parsed))
            {
                _manualRegionWidth = Math.Clamp(parsed, 0.01, 1.0 - _manualRegionX);
                _ = UpdateActiveRegionsAsync();
            }
        }
    }

    public string ManualRegionHeightText
    {
        get => _manualRegionHeightText;
        set
        {
            if (SetProperty(ref _manualRegionHeightText, value) &&
                double.TryParse(value.Replace(',', '.'), CultureInfo.InvariantCulture, out var parsed))
            {
                _manualRegionHeight = Math.Clamp(parsed, 0.01, 1.0 - _manualRegionY);
                _ = UpdateActiveRegionsAsync();
            }
        }
    }

    public string OcrProviderAvailabilityText =>
        SelectedOcrProvider is null
            ? string.Empty
            : SelectedOcrProvider.IsAvailable
                ? (SelectedOcrProvider.Id == "mock-ocr"
                    ? "Yalnızca test/simülasyon amaçlıdır (gerçek ekran metni tanımaz)."
                    : "Kullanılabilir")
                : $"Kullanılamıyor: {SelectedOcrProvider.AvailabilityMessage}";

    public bool CanRunOcr => _pipeline.LatestFrame is not null && SelectedOcrProvider?.IsAvailable == true;
    public bool CanRunBenchmark => SelectedOcrProvider?.IsAvailable == true;

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
        private set
        {
            if (SetProperty(ref _framePreviewImage, value))
            {
                OnPropertyChanged(nameof(HasFramePreview));
                OnPropertyChanged(nameof(HasNoFramePreview));
                OnPropertyChanged(nameof(CanRunOcr));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasFramePreview => FramePreviewImage is not null;
    public bool HasNoFramePreview => FramePreviewImage is null;

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
    public string? BenchmarkSummary
    {
        get => _benchmarkSummary;
        private set
        {
            if (SetProperty(ref _benchmarkSummary, value))
            {
                OnPropertyChanged(nameof(HasBenchmarkSummary));
            }
        }
    }
    public bool HasBenchmarkSummary => !string.IsNullOrWhiteSpace(BenchmarkSummary);

    public OcrDiagnosticsViewModel(
        IWindowDiscoveryService windowDiscovery,
        CaptureProcessingPipeline pipeline,
        IEnumerable<IOcrProvider> ocrProviders,
        IEnumerable<IScreenCaptureProvider> captureProviders,
        IGameProfileProvider profileProvider,
        AppPaths appPaths,
        ILogger<OcrDiagnosticsViewModel> logger)
        : base("Yakalama & OCR Denetimi", "Pencere yakalama, OCR motoru değerlendirmesi ve hata ayıklama telemetrisi (AG-02).")
    {
        _windowDiscovery = windowDiscovery;
        _pipeline = pipeline;
        _profileProvider = profileProvider;
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

        AvailableRegionModes.Add(new RegionModeOption(DiagnosticRegionMode.FullFrame, "Tüm Kare (Full Frame)"));
        AvailableRegionModes.Add(new RegionModeOption(DiagnosticRegionMode.ProfileSubtitles, "Oyun Profili Alt Yazıları"));
        AvailableRegionModes.Add(new RegionModeOption(DiagnosticRegionMode.ManualCustom, "Özel Bölge (Manuel)"));
        _selectedRegionMode = AvailableRegionModes[1]; // Profile Subtitles default

        RefreshWindowsCommand = new AsyncRelayCommand(RefreshWindowsAsync);
        StartCaptureCommand = new RelayCommand(StartCapture, () => CanStartCapture && !IsCapturing);
        StopCaptureCommand = new RelayCommand(StopCapture, () => IsCapturing);
        CaptureOnceCommand = new AsyncRelayCommand(CaptureOnceAsync, () => CanStartCapture);
        RunOcrOnceCommand = new AsyncRelayCommand(RunOcrOnceAsync, () => CanRunOcr);
        SaveScreenshotCommand = new AsyncRelayCommand(SaveScreenshotAsync, () => _pipeline.LatestFrame is not null);
        RunBenchmarkCommand = new AsyncRelayCommand(RunBenchmarkAsync, () => CanRunBenchmark);

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

    private async Task UpdateActiveRegionsAsync()
    {
        if (SelectedRegionMode is null)
        {
            _pipeline.OcrRegions = [];
            ActiveRegionBoxes.Clear();
            return;
        }

        switch (SelectedRegionMode.Mode)
        {
            case DiagnosticRegionMode.FullFrame:
                _pipeline.OcrRegions = [];
                ActiveRegionBoxes.Clear();
                break;

            case DiagnosticRegionMode.ProfileSubtitles:
                GameProfile? profile = null;
                if (SelectedWindow is not null)
                {
                    if (!string.IsNullOrWhiteSpace(SelectedWindow.ProcessName))
                    {
                        profile = await _profileProvider.FindByExecutableAsync(SelectedWindow.ProcessName, CancellationToken.None);
                    }

                    profile ??= await _profileProvider.FindByExecutableAsync(SelectedWindow.Title, CancellationToken.None);
                }

                profile ??= await _profileProvider.GetByIdAsync("universal", CancellationToken.None);

                if (profile is not null && profile.SubtitleRegions.Count > 0)
                {
                    _pipeline.OcrRegions = profile.SubtitleRegions.Select(r => r.Bounds).ToList();
                    ActiveRegionBoxes.Clear();
                    foreach (var region in profile.SubtitleRegions)
                    {
                        ActiveRegionBoxes.Add(new OcrRegionDisplayItem
                        {
                            Label = $"{profile.DisplayName}: {region.Id}",
                            Left = region.Bounds.X * NativeFrameWidth,
                            Top = region.Bounds.Y * NativeFrameHeight,
                            Width = region.Bounds.Width * NativeFrameWidth,
                            Height = region.Bounds.Height * NativeFrameHeight,
                            StrokeBrush = "#A3E635"
                        });
                    }
                }
                else
                {
                    _pipeline.OcrRegions = [];
                    ActiveRegionBoxes.Clear();
                }
                break;

            case DiagnosticRegionMode.ManualCustom:
                var manualRect = new NormalizedRect(_manualRegionX, _manualRegionY, _manualRegionWidth, _manualRegionHeight);
                _pipeline.OcrRegions = [manualRect];
                ActiveRegionBoxes.Clear();
                ActiveRegionBoxes.Add(new OcrRegionDisplayItem
                {
                    Label = "Özel Bölge",
                    Left = manualRect.X * NativeFrameWidth,
                    Top = manualRect.Y * NativeFrameHeight,
                    Width = manualRect.Width * NativeFrameWidth,
                    Height = manualRect.Height * NativeFrameHeight,
                    StrokeBrush = "#A3E635"
                });
                break;
        }
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

        if (!SelectedOcrProvider.IsAvailable)
        {
            StatusMessage = $"{SelectedOcrProvider.DisplayName} kullanılamıyor: {SelectedOcrProvider.AvailabilityMessage}";
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

        if (!SelectedOcrProvider.IsAvailable)
        {
            StatusMessage = $"{SelectedOcrProvider.DisplayName} kullanılamıyor: {SelectedOcrProvider.AvailabilityMessage}";
            return;
        }

        var frame = _pipeline.LatestFrame;
        if (frame is null)
        {
            StatusMessage = "Benchmark için lütfen önce 'Kare Yakala' veya 'Canlı Yakala' ile bir pencere karesi yakalayın.";
            return;
        }

        StatusMessage = $"{SelectedOcrProvider.DisplayName} için gerçek kare karşılaştırmalı performans testi yürütülüyor...";

        try
        {
            // Benchmark runs on actual captured pixels
            var samples = new List<OcrBenchmarkSample>
            {
                new("live_frame_01", frame, ExpectedText: null, ExpectedRegions: _pipeline.OcrRegions)
            };

            var report = await OcrBenchmarkRunner.RunBenchmarkAsync(
                SelectedOcrProvider,
                samples,
                warmIterations: 3,
                captureLatencyMs: CaptureLatencyMs > 0 ? CaptureLatencyMs : null);

            string accuracyText = report.HasVerifiedGroundTruth && report.AverageCer.HasValue
                ? $"CER: %{report.AverageCer.Value * 100:F1} | WER: %{report.AverageWer!.Value * 100:F1}"
                : "Doğruluk: Zemin metin (ground-truth) belirtilmedi (yalnızca gerçek performans ölçüldü)";

            string e2eText = report.EndToEndLatencyMs.HasValue
                ? $" | Uçtan Uca Gecikme: {report.EndToEndLatencyMs.Value:F1} ms"
                : string.Empty;

            BenchmarkSummary = $"Motor: {report.ProviderDisplayName} | Başarı: {report.SuccessfulSamples}/{report.TotalSamples} | " +
                               $"Soğuk Başlatma: {report.ColdStartDurationMs} ms | OCR Tanıma (Ort.): {report.MeanWarmLatencyMs} ms | " +
                               $"P50: {report.P50LatencyMs} ms | P95: {report.P95LatencyMs} ms{e2eText} | " +
                               $"Bellek Farkı: {report.MemoryDeltaMb} MB | {accuracyText}";

            StatusMessage = "Karşılaştırmalı performans testi tamamlandı.";
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

        bool sizeChanged = NativeFrameWidth != frame.Width || NativeFrameHeight != frame.Height;
        NativeFrameWidth = frame.Width;
        NativeFrameHeight = frame.Height;

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

        if (sizeChanged)
        {
            _ = UpdateActiveRegionsAsync();
        }
    }

    private void UpdateOcrResult(OcrResult result, int frameWidth, int frameHeight)
    {
        RecognizedLines.Clear();
        DetectedBoundingBoxes.Clear();

        foreach (var line in result.Lines)
        {
            RecognizedLines.Add(line);

            DetectedBoundingBoxes.Add(new OcrBoxDisplayItem
            {
                Text = line.Text,
                Confidence = line.Confidence,
                Left = line.Bounds.X * frameWidth,
                Top = line.Bounds.Y * frameHeight,
                Width = Math.Max(2.0, line.Bounds.Width * frameWidth),
                Height = Math.Max(2.0, line.Bounds.Height * frameHeight)
            });
        }
    }
}
