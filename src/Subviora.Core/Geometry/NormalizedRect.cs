namespace Subviora.Core.Geometry;

/// <summary>
/// A rectangle expressed in normalized coordinates relative to the captured client area,
/// where (0,0) is the top-left corner and (1,1) is the bottom-right corner.
/// Normalized coordinates keep game profiles independent of the player's resolution.
/// </summary>
public readonly record struct NormalizedRect
{
    public NormalizedRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>
    /// True when all values are finite, the size is positive and the rectangle lies inside the unit square.
    /// </summary>
    public bool IsValid =>
        double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height)
        && X >= 0 && Y >= 0 && Width > 0 && Height > 0
        && Right <= 1.0 + Tolerance && Bottom <= 1.0 + Tolerance;

    /// <summary>Floating-point tolerance applied to the right and bottom edges.</summary>
    public const double Tolerance = 1e-9;

    /// <summary>Creates a rectangle and throws if it is not valid.</summary>
    public static NormalizedRect Create(double x, double y, double width, double height)
    {
        var rect = new NormalizedRect(x, y, width, height);
        if (!rect.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Normalized rectangle ({x}, {y}, {width}, {height}) must have positive size and lie within [0, 1].");
        }

        return rect;
    }

    /// <summary>Returns true if this rectangle overlaps <paramref name="other"/> with a non-zero area.</summary>
    public bool Intersects(NormalizedRect other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}
