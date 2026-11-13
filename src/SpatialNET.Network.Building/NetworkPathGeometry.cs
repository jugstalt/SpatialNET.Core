namespace SpatialNET.Network.Building;

/// <summary>Where an edge lies on its source line: the line and the range of the edge on it (ADR 0050).</summary>
/// <param name="Line">The source line, as the host stores it.</param>
/// <param name="FromMeasure">Where the edge starts, as geometric length from the start of the line.</param>
/// <param name="ToMeasure">Where the edge ends.</param>
public readonly record struct EdgeShape(IPolyline2D Line, double FromMeasure, double ToMeasure);

/// <summary>Draws the ways of a network on the map from the source lines of their edges (ADR 0050).</summary>
public static class NetworkPathGeometry
{
    /// <summary>
    /// The way as one polyline: the piece of the source line of every step, cut with <c>Substring</c>, turned round where
    /// the step walks the edge against its direction, and joined.
    /// </summary>
    /// <param name="path">The way.</param>
    /// <param name="shapeOf">The source line and range of an edge, from the host (for example <c>InMemoryGraph.TryGetSource</c> or its database).</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>The way; an empty polyline for a way without steps.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="shapeOf"/> is <c>null</c>, or a shape has no line.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D ToPolyline<TNodeId, TEdgeId>(
        this NetworkPath<TNodeId, TEdgeId> path,
        Func<TEdgeId, EdgeShape> shapeOf,
        IGeometryFactory? factory = null)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(shapeOf);
        var f = GeometryFactory.Resolve(factory);
        var points = new List<Coordinate>();
        foreach (var step in path.Steps)
        {
            var shape = shapeOf(step.Edge);
            ArgumentNullException.ThrowIfNull(shape.Line, nameof(shapeOf));
            var piece = step.Reversed
                ? shape.Line.Substring(shape.ToMeasure, shape.FromMeasure, f)
                : shape.Line.Substring(shape.FromMeasure, shape.ToMeasure, f);
            for (int p = 0; p < piece.PathCount; p++)
            {
                var part = piece[p];
                for (int i = 0; i < part.PointCount; i++)
                {
                    var point = part[i];
                    var c = new Coordinate(point.X, point.Y,
                        point is IPoint3D z ? z.Z : double.NaN,
                        point is IPointM m ? m.M : double.NaN);
                    if (points.Count == 0 || points[^1].X != c.X || points[^1].Y != c.Y)
                    {
                        points.Add(c);
                    }
                }
            }
        }

        return points.Count < 2 ? f.CreatePolyline([], null) : f.CreatePolyline([points.ToArray()], null);
    }
}
