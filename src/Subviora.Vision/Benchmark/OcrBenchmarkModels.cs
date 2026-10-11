using Subviora.Core.Capture;
using Subviora.Core.Geometry;

namespace Subviora.Vision.Benchmark;

/// <summary>
/// Labeled ground truth data for OCR benchmarking.
/// </summary>
/// <param name="SampleName">Identifier or file name of the benchmark sample.</param>
/// <param name="Frame">The captured or recorded game frame.</param>
/// <param name="ExpectedText">Ground-truth expected subtitle text.</param>
/// <param name="ExpectedRegions">Optional ground-truth bounding boxes.</param>
public sealed record OcrBenchmarkSample(
    string SampleName,
    CapturedFrame Frame,
    string ExpectedText,
    IReadOnlyList<NormalizedRect>? ExpectedRegions = null);

/// <summary>
/// Result of evaluating a single sample during a benchmark run.
/// </summary>
public sealed record OcrBenchmarkItemResult(
    string SampleName,
    string RecognizedText,
    string ExpectedText,
    double? CharacterErrorRate,
    double? WordErrorRate,
    double? BoxIoU,
    double LatencyMs);

/// <summary>
/// Comprehensive summary report of an OCR engine benchmark run.
/// </summary>
public sealed record OcrBenchmarkReport(
    string ProviderId,
    string ProviderDisplayName,
    int TotalSamples,
    int SuccessfulSamples,
    double ColdStartDurationMs,
    double MeanLatencyMs,
    double P50LatencyMs,
    double P95LatencyMs,
    double? AverageCer,
    double? AverageWer,
    double? AverageIoU,
    double MemoryDeltaMb,
    TimeSpan TotalDuration,
    IReadOnlyList<OcrBenchmarkItemResult> ItemResults);
