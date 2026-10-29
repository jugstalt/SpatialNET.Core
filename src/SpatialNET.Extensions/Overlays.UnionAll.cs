using SpatialNET.Internal;
using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Overlay;

namespace SpatialNET;

public static partial class Overlays
{
    /// <summary>The number of geometries that are united together on one level of <c>UnionAll</c> (ADR 0018).</summary>
    private const int UnionGroupSize = 4;

    // ---------- UnionAll ----------

    /// <summary>
    /// Union of many polygons (ADR 0018, ADR 0047): all inputs in one overlay, on one grid. Each input is read under
    /// <paramref name="fillRule"/> as in <c>Union</c>.
    /// </summary>
    /// <param name="polygons">The polygons; empty ones are skipped.</param>
    /// <param name="fillRule">The fill rule for every input; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic from all inputs).</param>
    /// <returns>The union; an empty polygon for no input.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygons"/> or one of its elements is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolygon2D UnionAll(this IEnumerable<IPolygon2D> polygons, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolygon2D)UnionAllCore(Materialize(polygons, nameof(polygons)), 2, fillRule, factory, gridSize);

    /// <summary>Union of many polylines, noded like <see cref="Union(IPolyline2D, IPolyline2D, IGeometryFactory?, double?)"/>: a path is cut where different input lines meet or end (ADR 0018).</summary>
    /// <param name="polylines">The polylines; empty ones are skipped.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic from all inputs).</param>
    /// <returns>The union; an empty polyline for no input.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polylines"/> or one of its elements is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D UnionAll(this IEnumerable<IPolyline2D> polylines, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)UnionAllCore(Materialize(polylines, nameof(polylines)), 1, FillRule.EvenOdd, factory, gridSize);

    /// <summary>Union of many multipoints (ADR 0018).</summary>
    /// <param name="multiPoints">The multipoints; empty ones are skipped.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic from all inputs).</param>
    /// <returns>The union; an empty multipoint for no input.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="multiPoints"/> or one of its elements is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IMultiPoint2D UnionAll(this IEnumerable<IMultiPoint2D> multiPoints, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IMultiPoint2D)UnionAllCore(Materialize(multiPoints, nameof(multiPoints)), 0, FillRule.EvenOdd, factory, gridSize);

    /// <summary>
    /// Union of many geometries of equal dimension (ADR 0018); points and multipoints count as one dimension,
    /// empty geometries are skipped.
    /// </summary>
    /// <param name="geometries">The geometries.</param>
    /// <param name="fillRule">The fill rule for polygonal inputs; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic from all inputs).</param>
    /// <returns>The union; an empty polygon if there is no non-empty input.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometries"/> or one of its elements is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="NotSupportedException">An element is of an unknown kind, or the non-empty inputs have different dimensions.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D UnionAll(this IEnumerable<IGeometry2D> geometries, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null)
    {
        var items = Materialize(geometries, nameof(geometries));
        foreach (var g in items)
        {
            GeometryAccess.EnsureKnown(g, nameof(geometries));
        }

        var dimensions = items.Where(g => !GeometryAccess.IsEmpty(g)).Select(OverlayRunner.Dimension).Distinct().ToList();
        if (dimensions.Count > 1)
        {
            throw new NotSupportedException(
                $"UnionAll of mixed dimensions ({string.Join("/", dimensions.Order())}) cannot be represented: results are dimension-homogeneous (ADR 0005/0018).");
        }

        return UnionAllCore(items, dimensions.Count == 0 ? 2 : dimensions[0], fillRule, factory, gridSize);
    }

    private static List<TGeometry> Materialize<TGeometry>(IEnumerable<TGeometry> source, string paramName) where TGeometry : class, IGeometry2D
    {
        ArgumentNullException.ThrowIfNull(source, paramName);
        var list = new List<TGeometry>();
        foreach (var item in source)
        {
            if (item is null)
            {
                throw new ArgumentNullException(paramName, $"The element at index {list.Count} is null.");
            }

            list.Add(item);
        }

        return list;
    }

    /// <summary>
    /// The union of many inputs (ADR 0018): the grid is resolved once over all inputs; polygons are then united in one pass
    /// (ADR 0047), lines and points in a cascade: ordered by STR packing of their envelopes and united in groups of
    /// <see cref="UnionGroupSize"/>, level by level.
    /// </summary>
    private static IGeometry2D UnionAllCore(IReadOnlyList<IGeometry2D> inputs, int dimension, FillRule fillRule, IGeometryFactory? factory, double? gridSize)
    {
        var f = GeometryFactory.Resolve(factory);
        var source = inputs.Count > 0 ? inputs[0] : null;
        var items = inputs.Where(g => !GeometryAccess.IsEmpty(g)).ToList();
        if (items.Count == 0)
        {
            return Empty(dimension, f, source);
        }

        // One grid for all steps, so that the grouping cannot change what the steps round to.
        double size = GridModel.Resolve(gridSize, [.. items]).Size;

        // A fill rule other than EvenOdd applies to the inputs once; the intermediate results are valid polygons.
        bool normalized = dimension == 2 && fillRule != FillRule.EvenOdd;
        var current = normalized ? items.Select(g => Normalize(g, 2, fillRule, f, size)).Where(g => !GeometryAccess.IsEmpty(g)).ToList() : items;

        if (current.Count == 1 && !normalized)
        {
            current[0] = Normalize(current[0], dimension, FillRule.EvenOdd, f, size);
        }

        if (dimension == 2 && current.Count > 1)
        {
            return UnionAreasInOnePass(current, normalized, f, size);
        }

        while (current.Count > 1)
        {
            var order = StrPacking.Order(current.Select(g => g.Envelope()).ToList(), UnionGroupSize);
            var next = new List<IGeometry2D>();
            for (int start = 0; start < order.Length; start += UnionGroupSize)
            {
                var group = order.Skip(start).Take(UnionGroupSize).Select(i => current[i]).ToList();
                var united = UnionGroup(group, 0, group.Count, dimension, f, size);
                if (!GeometryAccess.IsEmpty(united))
                {
                    next.Add(united);
                }
            }

            current = next;
        }

        return current.Count == 0 ? Empty(dimension, f, source) : current[0];
    }

    /// <summary>
    /// The union of many polygons in one overlay (ADR 0047): every input becomes rings oriented by their nesting (shells
    /// counter-clockwise, holes clockwise), whose winding number is 1 in its area and 0 outside; the sum over all inputs is positive
    /// exactly in the union (<see cref="FillRule.Positive"/>, as for the buffer). An input whose rings meet each other or themselves is
    /// normalised by the overlay first; inputs that are normalised already (<paramref name="valid"/>) are taken as they are.
    /// </summary>
    private static IGeometry2D UnionAreasInOnePass(List<IGeometry2D> items, bool valid, IGeometryFactory f, double size)
    {
        var rings = new List<(Coordinate[] Path, int Operand, bool Area)>();
        foreach (var item in items)
        {
            var polygon = (IPolygon2D)item;
            if (!valid && !HasSimpleRings(polygon))
            {
                polygon = (IPolygon2D)Normalize(polygon, 2, FillRule.EvenOdd, f, size);
            }

            foreach (var part in PolygonStructure.Build(polygon))
            {
                rings.Add((part.Shell, 0, true));
                foreach (var hole in part.Holes) rings.Add((hole, 0, true));
            }
        }

        var engine = new OverlayEngine([null, null], new GridModel(size), [FillRule.Positive, FillRule.Positive], rings);
        var parts = engine.Areas(h => engine.InsideLeft(h, 0));
        return f.CreatePolygon(parts.Select(p => GeometryAccess.Restrict(p, f)).ToArray(), items[0]);
    }

    /// <summary>
    /// Whether the rings of the polygon have area and do not meet: no two segments intersect, except neighbours in a ring at their
    /// common vertex (exact). Then the nesting of the rings is their even-odd reading.
    /// </summary>
    private static bool HasSimpleRings(IPolygon2D polygon)
    {
        var segments = new List<(Coordinate A, Coordinate B, int Ring, int Index, int Count)>();
        for (int r = 0; r < polygon.RingCount; r++)
        {
            var closed = Rings.Closed(polygon[r]);
            if (closed.Length == 0) continue;
            var ring = new List<Coordinate>(closed.Length);
            foreach (var c in closed)
            {
                if (!double.IsFinite(c.X) || !double.IsFinite(c.Y)) return false;
                if (ring.Count == 0 || !Rings.SameXY(ring[^1], c)) ring.Add(c);
            }

            if (ring.Count < 4 || Rings.IsDegenerate(ring.ToArray())) return false;
            for (int i = 1; i < ring.Count; i++) segments.Add((ring[i - 1], ring[i], r, i - 1, ring.Count - 1));
        }

        var order = segments.OrderBy(s => Math.Min(s.A.X, s.B.X)).ToArray();
        var active = new List<int>();
        for (int k = 0; k < order.Length; k++)
        {
            var s = order[k];
            double minX = Math.Min(s.A.X, s.B.X), minY = Math.Min(s.A.Y, s.B.Y), maxY = Math.Max(s.A.Y, s.B.Y);
            active.RemoveAll(i => Math.Max(order[i].A.X, order[i].B.X) < minX);
            foreach (int i in active)
            {
                var t = order[i];
                if (Math.Max(t.A.Y, t.B.Y) < minY || maxY < Math.Min(t.A.Y, t.B.Y)) continue;
                if (s.Ring == t.Ring && ((s.Index + 1) % s.Count == t.Index || (t.Index + 1) % t.Count == s.Index))
                {
                    // neighbours share one vertex; they must not run back along each other
                    var (first, second) = (s.Index + 1) % s.Count == t.Index ? (s, t) : (t, s);
                    if (Segments.IsOnSegment(second.B, first.A, first.B) || Segments.IsOnSegment(first.A, second.A, second.B)) return false;
                    continue;
                }

                if (Segments.Intersects(s.A, s.B, t.A, t.B)) return false;
            }

            active.Add(k);
        }

        return true;
    }

    /// <summary>Unites <c>items[start..end)</c> by recursive halving.</summary>
    private static IGeometry2D UnionGroup(List<IGeometry2D> items, int start, int end, int dimension, IGeometryFactory f, double size)
    {
        if (end - start == 1)
        {
            return items[start];
        }

        int middle = start + (end - start) / 2;
        return UnionPair(UnionGroup(items, start, middle, dimension, f, size), UnionGroup(items, middle, end, dimension, f, size), dimension, f, size);
    }

    private static IGeometry2D UnionPair(IGeometry2D a, IGeometry2D b, int dimension, IGeometryFactory f, double size) => dimension switch
    {
        0 => OverlayRunner.Points(OverlayOp.Union, a, b, FillRule.EvenOdd, f, size),
        1 => OverlayRunner.Lines(OverlayOp.Union, a, b, FillRule.EvenOdd, f, size),
        _ => OverlayRunner.Areas(OverlayOp.Union, a, b, FillRule.EvenOdd, f, size),
    };

    /// <summary>A single geometry through the overlay core: valid and normalized.</summary>
    private static IGeometry2D Normalize(IGeometry2D g, int dimension, FillRule fillRule, IGeometryFactory f, double size) => dimension switch
    {
        0 => OverlayRunner.Points(OverlayOp.Union, g, g, fillRule, f, size),
        1 => OverlayRunner.Lines(OverlayOp.Union, g, null, fillRule, f, size),
        _ => OverlayRunner.Areas(OverlayOp.Union, g, null, fillRule, f, size),
    };

    private static IGeometry2D Empty(int dimension, IGeometryFactory f, IGeometry2D? source) => dimension switch
    {
        0 => f.CreateMultiPoint([], source),
        1 => f.CreatePolyline([], source),
        _ => f.CreatePolygon([], source),
    };
}
