using Subviora.Core.Capture;
using Subviora.Core.Geometry;
using Subviora.Vision.Benchmark;
using Subviora.Vision.Mock;

namespace Subviora.Tests.Vision;

public sealed class BenchmarkCalculationTests
{
    [Fact]
    public void LevenshteinDistance_CalculatesAccurateDistances()
    {
        Assert.Equal(0, OcrBenchmarkMetrics.LevenshteinDistance("hello", "hello"));
        Assert.Equal(0, OcrBenchmarkMetrics.LevenshteinDistance("", ""));
        Assert.Equal(5, OcrBenchmarkMetrics.LevenshteinDistance("", "world"));
        Assert.Equal(4, OcrBenchmarkMetrics.LevenshteinDistance("test", ""));
        Assert.Equal(1, OcrBenchmarkMetrics.LevenshteinDistance("cat", "hat"));
        Assert.Equal(1, OcrBenchmarkMetrics.LevenshteinDistance("cat", "cats"));
        Assert.Equal(1, OcrBenchmarkMetrics.LevenshteinDistance("cats", "cat"));
        Assert.Equal(3, OcrBenchmarkMetrics.LevenshteinDistance("kitten", "sitting"));
    }

    [Fact]
    public void CalculateCer_CalculatesAccurateErrorRates()
    {
        // Exact match
        Assert.Equal(0.0, OcrBenchmarkMetrics.CalculateCer("Grand Theft Auto", "Grand Theft Auto"));

        // Empty matches
        Assert.Equal(0.0, OcrBenchmarkMetrics.CalculateCer("", ""));
        Assert.Equal(1.0, OcrBenchmarkMetrics.CalculateCer("", "unexpected text"));

        // Complete mismatch (clamped at 1.0)
        Assert.Equal(1.0, OcrBenchmarkMetrics.CalculateCer("abc", "xyz"));

        // Single substitution: 1 error in 7 characters
        double cer = OcrBenchmarkMetrics.CalculateCer("Michael", "Michzel");
        Assert.True(cer > 0.1 && cer < 0.2, $"Expected CER ~0.14, got {cer}");
    }

    [Fact]
    public void CalculateWer_CalculatesAccurateWordErrorRates()
    {
        // Exact match
        Assert.Equal(0.0, OcrBenchmarkMetrics.CalculateWer("What are you doing", "What are you doing"));

        // Empty strings
        Assert.Equal(0.0, OcrBenchmarkMetrics.CalculateWer("", ""));
        Assert.Equal(1.0, OcrBenchmarkMetrics.CalculateWer("", "some word"));

        // One substitution in 4 words: 1 / 4 = 0.25
        Assert.Equal(0.25, OcrBenchmarkMetrics.CalculateWer("What are you doing", "What were you doing"));

        // One deletion in 4 words: 1 / 4 = 0.25
        Assert.Equal(0.25, OcrBenchmarkMetrics.CalculateWer("What are you doing", "What are you"));

        // Complete mismatch
        Assert.Equal(1.0, OcrBenchmarkMetrics.CalculateWer("Hello world", "Goodbye universe"));
    }

    [Fact]
    public void CalculateIoU_CalculatesAccurateIntersectionOverUnion()
    {
        // Identical rectangles
        var rectA = new NormalizedRect(0.1, 0.8, 0.8, 0.1);
        Assert.Equal(1.0, OcrBenchmarkMetrics.CalculateIoU(rectA, rectA), precision: 5);

        // Disjoint rectangles
        var rectDisjoint = new NormalizedRect(0.0, 0.0, 0.2, 0.2);
        var rectOther = new NormalizedRect(0.5, 0.5, 0.2, 0.2);
        Assert.Equal(0.0, OcrBenchmarkMetrics.CalculateIoU(rectDisjoint, rectOther));

        // Overlapping rectangles:
        // rect1: [0.0, 0.0] to [0.4, 0.2] -> area = 0.08
        // rect2: [0.2, 0.0] to [0.6, 0.2] -> area = 0.08
        // intersection: [0.2, 0.0] to [0.4, 0.2] -> area = 0.04
        // union = 0.08 + 0.08 - 0.04 = 0.12
        // IoU = 0.04 / 0.12 = 1/3 (~0.3333)
        var rect1 = new NormalizedRect(0.0, 0.0, 0.4, 0.2);
        var rect2 = new NormalizedRect(0.2, 0.0, 0.4, 0.2);
        double iou = OcrBenchmarkMetrics.CalculateIoU(rect1, rect2);
        Assert.Equal(1.0 / 3.0, iou, 4);
    }

    [Fact]
    public void CalculatePercentile_CalculatesAccuratePercentiles()
    {
        Assert.Equal(0.0, OcrBenchmarkMetrics.CalculatePercentile([], 0.5));

        var single = new List<double> { 42.0 };
        Assert.Equal(42.0, OcrBenchmarkMetrics.CalculatePercentile(single, 0.5));

        var values = new List<double> { 10.0, 20.0, 30.0, 40.0, 50.0 };
        Assert.Equal(10.0, OcrBenchmarkMetrics.CalculatePercentile(values, 0.0));
        Assert.Equal(30.0, OcrBenchmarkMetrics.CalculatePercentile(values, 0.5));
        Assert.Equal(50.0, OcrBenchmarkMetrics.CalculatePercentile(values, 1.0));
        Assert.True(OcrBenchmarkMetrics.CalculatePercentile(values, 0.95) >= 45.0);
    }

    [Fact]
    public async Task OcrBenchmarkRunner_ExecutesBenchmark_ReturnsValidReport()
    {
        var mockProvider = new MockOcrProvider
        {
            LatencyMs = 5
        };

        var frame = CapturedFrame.CreateEmpty(640, 480);
        var samples = new List<OcrBenchmarkSample>
        {
            new("sample_1", frame, "What are you doing here, Michael?", [new NormalizedRect(0.1, 0.8, 0.8, 0.1)]),
            new("sample_2", frame, "What are you doing here, Michael?", [new NormalizedRect(0.1, 0.8, 0.8, 0.1)])
        };

        var report = await OcrBenchmarkRunner.RunBenchmarkAsync(
            mockProvider,
            samples,
            warmIterations: 1,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.Equal("Mock / Test OCR", report.ProviderDisplayName);
        Assert.Equal(2, report.TotalSamples);
        Assert.Equal(2, report.SuccessfulSamples);
        Assert.True(report.ColdStartDurationMs >= 0);
        Assert.True(report.P50LatencyMs >= 0);
        Assert.True(report.P95LatencyMs >= 0);
        Assert.Equal(0.0, report.AverageCer); // Mock returns exact string
        Assert.Equal(0.0, report.AverageWer);
        Assert.True(report.HasVerifiedGroundTruth);
    }

    [Fact]
    public async Task OcrBenchmarkRunner_WhenNoGroundTruthProvided_DoesNotFabricateAccuracyMetrics()
    {
        var mockProvider = new MockOcrProvider { LatencyMs = 2 };
        var frame = CapturedFrame.CreateEmpty(640, 480);
        var samples = new List<OcrBenchmarkSample>
        {
            new("live_frame", frame, ExpectedText: null, ExpectedRegions: [new NormalizedRect(0.1, 0.8, 0.8, 0.1)])
        };

        var report = await OcrBenchmarkRunner.RunBenchmarkAsync(
            mockProvider,
            samples,
            warmIterations: 2,
            captureLatencyMs: 14.5,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.False(report.HasVerifiedGroundTruth);
        Assert.Null(report.AverageCer);
        Assert.Null(report.AverageWer);
        Assert.NotNull(report.CaptureLatencyMs);
        Assert.Equal(14.5, report.CaptureLatencyMs.Value);
        Assert.NotNull(report.EndToEndLatencyMs);
        Assert.True(report.EndToEndLatencyMs.Value >= 14.5);
    }
}
