using Subviora.Core.Geometry;

namespace Subviora.Vision.Benchmark;

/// <summary>
/// Computes quantitative evaluation metrics for OCR benchmarking:
/// Character Error Rate (CER), Word Error Rate (WER), Bounding Box IoU, and latency percentiles.
/// </summary>
public static class OcrBenchmarkMetrics
{
    /// <summary>
    /// Calculates Levenshtein edit distance between two strings.
    /// </summary>
    public static int LevenshteinDistance(string? source, string? target)
    {
        if (string.IsNullOrEmpty(source))
        {
            return target?.Length ?? 0;
        }

        if (string.IsNullOrEmpty(target))
        {
            return source.Length;
        }

        int n = source.Length;
        int m = target.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= m; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    /// <summary>
    /// Calculates Character Error Rate (CER): LevenshteinDistance(expected, actual) / expected.Length.
    /// Clamped to [0, 1] for reporting consistency.
    /// </summary>
    public static double CalculateCer(string expected, string actual)
    {
        string normExp = NormalizeText(expected);
        string normAct = NormalizeText(actual);

        if (normExp.Length == 0)
        {
            return normAct.Length == 0 ? 0.0 : 1.0;
        }

        int distance = LevenshteinDistance(normExp, normAct);
        return Math.Min(1.0, (double)distance / normExp.Length);
    }

    /// <summary>
    /// Calculates Word Error Rate (WER): LevenshteinDistance on whitespace-delimited word tokens.
    /// </summary>
    public static double CalculateWer(string expected, string actual)
    {
        var expectedWords = TokenizeWords(expected);
        var actualWords = TokenizeWords(actual);

        if (expectedWords.Length == 0)
        {
            return actualWords.Length == 0 ? 0.0 : 1.0;
        }

        int n = expectedWords.Length;
        int m = actualWords.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= m; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = string.Equals(expectedWords[i - 1], actualWords[j - 1], StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return Math.Min(1.0, (double)d[n, m] / n);
    }

    /// <summary>
    /// Calculates Intersection over Union (IoU) of two normalized rectangles.
    /// </summary>
    public static double CalculateIoU(NormalizedRect a, NormalizedRect b)
    {
        double x1 = Math.Max(a.X, b.X);
        double y1 = Math.Max(a.Y, b.Y);
        double x2 = Math.Min(a.Right, b.Right);
        double y2 = Math.Min(a.Bottom, b.Bottom);

        double intersectionWidth = Math.Max(0.0, x2 - x1);
        double intersectionHeight = Math.Max(0.0, y2 - y1);
        double intersectionArea = intersectionWidth * intersectionHeight;

        double areaA = a.Width * a.Height;
        double areaB = b.Width * b.Height;
        double unionArea = areaA + areaB - intersectionArea;

        if (unionArea <= 0.0)
        {
            return 0.0;
        }

        return Math.Clamp(intersectionArea / unionArea, 0.0, 1.0);
    }

    /// <summary>
    /// Calculates the value at a specified percentile (e.g. 0.5 for p50, 0.95 for p95).
    /// </summary>
    public static double CalculatePercentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 1)
        {
            return sorted[0];
        }

        double rank = Math.Clamp(percentile, 0.0, 1.0) * (sorted.Count - 1);
        int lowIndex = (int)Math.Floor(rank);
        int highIndex = (int)Math.Ceiling(rank);

        if (lowIndex == highIndex)
        {
            return sorted[lowIndex];
        }

        double fraction = rank - lowIndex;
        return sorted[lowIndex] + fraction * (sorted[highIndex] - sorted[lowIndex]);
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string[] TokenizeWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }
}
