namespace SpatialNET.Internal;

/// <summary>Basic geometric predicates, exact via orient2d (ADR 0005).</summary>
internal static class Predicates
{
    /// <summary>Sign of the orientation of c relative to the line a→b: +1 left, -1 right, 0 collinear.</summary>
    public static int Orient(Coordinate a, Coordinate b, Coordinate c) => Noding.Orientation.Orient2D(a, b, c);
}
