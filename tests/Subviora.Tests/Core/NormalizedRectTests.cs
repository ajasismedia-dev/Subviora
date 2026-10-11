using Subviora.Core.Geometry;

namespace Subviora.Tests.Core;

public sealed class NormalizedRectTests
{
    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(0.2, 0.84, 0.6, 0.12)]
    [InlineData(0.5, 0.5, 0.5, 0.5)]
    public void IsValid_ForRectanglesInsideUnitSquare_ReturnsTrue(double x, double y, double w, double h) =>
        Assert.True(new NormalizedRect(x, y, w, h).IsValid);

    [Theory]
    [InlineData(-0.1, 0, 0.5, 0.5)]
    [InlineData(0, 0, 0, 0.5)]
    [InlineData(0, 0, 0.5, -0.1)]
    [InlineData(0.6, 0, 0.5, 0.5)]
    [InlineData(0, 0.9, 0.5, 0.2)]
    [InlineData(double.NaN, 0, 0.5, 0.5)]
    [InlineData(0, 0, double.PositiveInfinity, 0.5)]
    public void IsValid_ForOutOfRangeOrDegenerateRectangles_ReturnsFalse(double x, double y, double w, double h) =>
        Assert.False(new NormalizedRect(x, y, w, h).IsValid);

    [Fact]
    public void Create_WithInvalidValues_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NormalizedRect.Create(0.9, 0.9, 0.5, 0.5));

    [Fact]
    public void Intersects_DetectsOverlapButNotTouchingEdges()
    {
        var a = NormalizedRect.Create(0, 0, 0.5, 0.5);
        Assert.True(a.Intersects(NormalizedRect.Create(0.25, 0.25, 0.5, 0.5)));
        Assert.False(a.Intersects(NormalizedRect.Create(0.5, 0, 0.5, 0.5)));
    }
}
