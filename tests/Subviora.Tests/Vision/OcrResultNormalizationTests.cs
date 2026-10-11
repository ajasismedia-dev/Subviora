using Subviora.Core.Geometry;
using Subviora.Core.Vision;

namespace Subviora.Tests.Vision;

public sealed class OcrResultNormalizationTests
{
    [Fact]
    public void OcrResult_Empty_ContainsZeroLinesAndEmptyFullText()
    {
        var empty = OcrResult.Empty;

        Assert.NotNull(empty);
        Assert.Empty(empty.Lines);
        Assert.Equal(string.Empty, empty.FullText);
    }

    [Fact]
    public void OcrResult_FullText_ConcatenatesLinesWithNewlines()
    {
        var lines = new List<OcrTextLine>
        {
            new("First line of subtitle", new NormalizedRect(0.1, 0.8, 0.8, 0.05), 0.92),
            new("Second line of subtitle", new NormalizedRect(0.1, 0.86, 0.8, 0.05), 0.88)
        };

        var result = new OcrResult(lines);

        Assert.Equal("First line of subtitle" + Environment.NewLine + "Second line of subtitle", result.FullText);
        Assert.Equal(2, result.Lines.Count);
    }

    [Fact]
    public void OcrResult_PreservesUnicodeCharactersSafely()
    {
        // Subtitles may contain Turkish, English, numbers, symbols, quotation marks
        string unicodeSubtitle = "Arthur: \"Hadi gidelim dostum! Çabuk ol, köprüyü geçeceğiz...\"";

        var line = new OcrTextLine(unicodeSubtitle, new NormalizedRect(0.1, 0.8, 0.8, 0.08), 0.98);
        var result = new OcrResult([line]);

        Assert.Equal(unicodeSubtitle, result.FullText);
        Assert.Equal(unicodeSubtitle, result.Lines[0].Text);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void OcrConfidence_ValidRange_StoredCorrectly(double confidence)
    {
        var line = new OcrTextLine("Test", new NormalizedRect(0, 0, 1, 1), confidence);
        Assert.Equal(confidence, line.Confidence);
    }

    [Fact]
    public void BoundingBoxes_AreNormalizedInUnitRange()
    {
        var bounds = new NormalizedRect(0.15, 0.82, 0.70, 0.12);
        var line = new OcrTextLine("Subtitle", bounds, 0.9);

        Assert.True(line.Bounds.X >= 0.0 && line.Bounds.X <= 1.0);
        Assert.True(line.Bounds.Y >= 0.0 && line.Bounds.Y <= 1.0);
        Assert.True(line.Bounds.Width >= 0.0 && line.Bounds.Width <= 1.0);
        Assert.True(line.Bounds.Height >= 0.0 && line.Bounds.Height <= 1.0);
        Assert.True(line.Bounds.Right <= 1.0);
        Assert.True(line.Bounds.Bottom <= 1.0);
    }
}
