namespace SpatialNET.Internal;

/// <summary>Geometrische Grundprädikate. Wird mit dem Präzisionskern (#25) exakt.</summary>
internal static class Predicates
{
    /// <summary>Vorzeichen der Orientierung von c zur Geraden a→b: +1 links, -1 rechts, 0 kollinear.</summary>
    public static int Orient(Coordinate a, Coordinate b, Coordinate c) => Math.Sign(Segments.Orient(a, b, c));
}
