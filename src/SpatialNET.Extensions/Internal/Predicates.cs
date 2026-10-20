namespace SpatialNET.Internal;

/// <summary>Geometrische Grundprädikate, exakt über orient2d (ADR 0005).</summary>
internal static class Predicates
{
    /// <summary>Vorzeichen der Orientierung von c zur Geraden a→b: +1 links, -1 rechts, 0 kollinear.</summary>
    public static int Orient(Coordinate a, Coordinate b, Coordinate c) => Noding.Orientation.Orient2D(a, b, c);
}
