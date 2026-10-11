using System.Diagnostics;
using Subviora.Core.Vision;

namespace Subviora.Vision.Benchmark;

/// <summary>
/// Executes standardized, repeatable OCR benchmark evaluations against providers.
/// Measures cold-start, warm-runtime latency (p50/p95), accuracy (CER/WER/IoU), and memory footprint.
/// </summary>
public sealed class OcrBenchmarkRunner
{
    public static async Task<OcrBenchmarkReport> RunBenchmarkAsync(
        IOcrProvider provider,
        IReadOnlyList<OcrBenchmarkSample> samples,
        int warmIterations = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count == 0)
        {
            throw new ArgumentException("At least one benchmark sample must be provided.", nameof(samples));
        }

        long initialMemory = GC.GetTotalMemory(forceFullCollection: false);
        var totalSw = Stopwatch.StartNew();

        double coldStartMs = 0;
        var warmLatencies = new List<double>();
        var itemResults = new List<OcrBenchmarkItemResult>();
        int successfulSamples = 0;

        // 1. Cold-start run (first sample, first iteration)
        var firstSample = samples[0];
        var coldSw = Stopwatch.StartNew();
        try
        {
            var request = new OcrRequest(
                firstSample.Frame,
                firstSample.ExpectedRegions ?? [],
                "en");

            _ = await provider.RecognizeAsync(request, cancellationToken).ConfigureAwait(false);
            coldSw.Stop();
            coldStartMs = coldSw.Elapsed.TotalMilliseconds;
        }
        catch
        {
            coldSw.Stop();
            coldStartMs = coldSw.Elapsed.TotalMilliseconds;
        }

        // 2. Benchmark iterations across all samples
        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sampleLatencies = new List<double>();
            string lastRecognizedText = string.Empty;
            OcrResult? lastResult = null;

            for (int i = 0; i < warmIterations; i++)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var request = new OcrRequest(
                        sample.Frame,
                        sample.ExpectedRegions ?? [],
                        "en");

                    var result = await provider.RecognizeAsync(request, cancellationToken).ConfigureAwait(false);
                    sw.Stop();
                    sampleLatencies.Add(sw.Elapsed.TotalMilliseconds);
                    warmLatencies.Add(sw.Elapsed.TotalMilliseconds);
                    lastResult = result;
                    lastRecognizedText = result.FullText;
                }
                catch
                {
                    sw.Stop();
                    sampleLatencies.Add(sw.Elapsed.TotalMilliseconds);
                }
            }

            if (lastResult is not null)
            {
                successfulSamples++;
            }

            double? cer = !string.IsNullOrEmpty(sample.ExpectedText)
                ? OcrBenchmarkMetrics.CalculateCer(sample.ExpectedText, lastRecognizedText)
                : null;

            double? wer = !string.IsNullOrEmpty(sample.ExpectedText)
                ? OcrBenchmarkMetrics.CalculateWer(sample.ExpectedText, lastRecognizedText)
                : null;

            double? iou = null;
            if (sample.ExpectedRegions is { Count: > 0 } && lastResult is { Lines.Count: > 0 })
            {
                iou = OcrBenchmarkMetrics.CalculateIoU(sample.ExpectedRegions[0], lastResult.Lines[0].Bounds);
            }

            double meanSampleLatency = sampleLatencies.Count > 0 ? sampleLatencies.Average() : 0.0;

            itemResults.Add(new OcrBenchmarkItemResult(
                SampleName: sample.SampleName,
                RecognizedText: lastRecognizedText,
                ExpectedText: sample.ExpectedText,
                CharacterErrorRate: cer.HasValue ? Math.Round(cer.Value, 4) : null,
                WordErrorRate: wer.HasValue ? Math.Round(wer.Value, 4) : null,
                BoxIoU: iou.HasValue ? Math.Round(iou.Value, 4) : null,
                LatencyMs: Math.Round(meanSampleLatency, 1)));
        }

        totalSw.Stop();
        long finalMemory = GC.GetTotalMemory(forceFullCollection: false);
        double memoryDeltaMb = Math.Max(0.0, (finalMemory - initialMemory) / (1024.0 * 1024.0));

        double meanLatency = warmLatencies.Count > 0 ? warmLatencies.Average() : 0.0;
        double p50Latency = OcrBenchmarkMetrics.CalculatePercentile(warmLatencies, 0.50);
        double p95Latency = OcrBenchmarkMetrics.CalculatePercentile(warmLatencies, 0.95);

        var validCers = itemResults.Where(r => r.CharacterErrorRate.HasValue).Select(r => r.CharacterErrorRate!.Value).ToList();
        var validWers = itemResults.Where(r => r.WordErrorRate.HasValue).Select(r => r.WordErrorRate!.Value).ToList();
        var validIous = itemResults.Where(r => r.BoxIoU.HasValue).Select(r => r.BoxIoU!.Value).ToList();

        return new OcrBenchmarkReport(
            ProviderId: provider.Id,
            ProviderDisplayName: provider.DisplayName,
            TotalSamples: samples.Count,
            SuccessfulSamples: successfulSamples,
            ColdStartDurationMs: Math.Round(coldStartMs, 1),
            MeanLatencyMs: Math.Round(meanLatency, 1),
            P50LatencyMs: Math.Round(p50Latency, 1),
            P95LatencyMs: Math.Round(p95Latency, 1),
            AverageCer: validCers.Count > 0 ? Math.Round(validCers.Average(), 4) : null,
            AverageWer: validWers.Count > 0 ? Math.Round(validWers.Average(), 4) : null,
            AverageIoU: validIous.Count > 0 ? Math.Round(validIous.Average(), 4) : null,
            MemoryDeltaMb: Math.Round(memoryDeltaMb, 2),
            TotalDuration: totalSw.Elapsed,
            ItemResults: itemResults);
    }
}
