using Subviora.Core.Capture;

namespace Subviora.Capture.Pipeline;

/// <summary>
/// Real-time capture and OCR telemetry for diagnostic monitoring.
/// Accurately reflects session state, frame freshness, and system resources.
/// </summary>
public sealed record CaptureDiagnostics(
    CaptureTarget? Target,
    string Status,
    int FrameWidth,
    int FrameHeight,
    double Fps,
    double CaptureLatencyMs,
    double OcrLatencyMs,
    double CpuUsagePercent,
    double MemoryWorkingSetMb,
    long TotalFramesCaptured,
    long TotalFramesProcessed,
    long DroppedFramesCount,
    string? LastCaptureError = null,
    string? LastOcrError = null,
    string? ProviderName = null,
    string? TargetWindowDescription = null,
    double TimeSinceLastFreshFrameMs = 0.0,
    bool IsWindowIdle = false)
{
    public static CaptureDiagnostics Empty { get; } = new(
        Target: null,
        Status: "Boşta",
        FrameWidth: 0,
        FrameHeight: 0,
        Fps: 0,
        CaptureLatencyMs: 0,
        OcrLatencyMs: 0,
        CpuUsagePercent: 0,
        MemoryWorkingSetMb: 0,
        TotalFramesCaptured: 0,
        TotalFramesProcessed: 0,
        DroppedFramesCount: 0,
        LastCaptureError: null,
        LastOcrError: null,
        ProviderName: null,
        TargetWindowDescription: null,
        TimeSinceLastFreshFrameMs: 0.0,
        IsWindowIdle: false);
}
