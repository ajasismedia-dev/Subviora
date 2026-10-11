using Subviora.Core.Capture;

namespace Subviora.Capture.Pipeline;

/// <summary>
/// Real-time capture and OCR telemetry for diagnostic monitoring.
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
    string? LastOcrError = null)
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
        DroppedFramesCount: 0);
}
