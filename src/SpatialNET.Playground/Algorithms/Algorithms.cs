namespace SpatialNET.Playground.Algorithms;

// Descriptors of all public algorithms (ADR 0011). A new algorithm gets a class here;
// the test PlaygroundCompletenessTests makes sure that none is missing.

internal static class In
{
    public static readonly string[] Any = ["Point", "MultiPoint", "LineString", "Polygon"];
    public static readonly string[] Lines = ["LineString"];
    public static readonly string[] Areas = ["Polygon"];
    public static readonly string[] LinesAndAreas = ["LineString", "Polygon"];

    public static InputDescriptor A(string[] types, string label = "Input") => new("a", label, types);
    public static InputDescriptor B(string[] types, string label = "B", bool optional = false) => new("b", label, types, optional);
}

internal static class P
{
    public static ParameterDescriptor Distance(double d = 10, string label = "Distance") => new("distance", label, "number", d, "kbs");
    public static ParameterDescriptor Join => new("join", "Join", "enum", "Round", null, ["Round", "Mitre", "Bevel"]);
    public static ParameterDescriptor FillRule => new("fillRule", "Fill rule", "enum", "EvenOdd", null, ["EvenOdd", "NonZero", "Positive", "Negative"]);
    public static ParameterDescriptor GridSize => new("gridSize", "Grid size (empty = auto)", "number", null, "kbs");
}

// ---------- Measures ----------

public sealed class AreaAlgorithm() : PlaygroundAlgorithm("area", "Area", "Measures", [In.A(In.Any)], [], (i, _, _, _) => i["a"].Area());
public sealed class LengthAlgorithm() : PlaygroundAlgorithm("length", "Length", "Measures", [In.A(In.Any)], [], (i, _, _, _) => i["a"].Length());
public sealed class EnvelopeAlgorithm() : PlaygroundAlgorithm("envelope", "Envelope", "Measures", [In.A(In.Any)], [], (i, _, _, _) => i["a"].Envelope());
public sealed class CentroidAlgorithm() : PlaygroundAlgorithm("centroid", "Centroid", "Measures", [In.A(In.Any)], [], (i, _, _, _) => i["a"].Centroid());
public sealed class DistanceAlgorithm() : PlaygroundAlgorithm("distance", "Distance", "Measures", [In.A(In.Any, "A"), In.B(In.Any)], [], (i, _, _, _) => i["a"].Distance(i["b"]));

// ---------- Validation and predicates ----------

public sealed class IsValidAlgorithm() : PlaygroundAlgorithm("isValid", "IsValid", "Validation", [In.A(In.Any)], [], (i, _, _, _) => i["a"].IsValid());
public sealed class ValidateAlgorithm() : PlaygroundAlgorithm("validate", "Validate", "Validation", [In.A(In.Any)], [], (i, _, _, _) => i["a"].Validate());
public sealed class RelateAlgorithm() : PlaygroundAlgorithm("relate", "Relate (DE-9IM)", "Predicates", [In.A(In.Any, "A"), In.B(In.Any)], [], (i, _, _, _) => i["a"].Relate(i["b"]));

/// <summary>
/// The result of a predicate with the distance of the inputs and the alternative with a tolerance. A predicate computes exactly: a point
/// that is drawn on a line almost never lies exactly on it. The distance shows whether a "false" is only a rounding difference of the
/// coordinates; the alternative names how to ask the library with a tolerance.
/// </summary>
/// <param name="Value">The result of the predicate.</param>
/// <param name="Distance">The distance of the two inputs (<c>NaN</c> if one is empty).</param>
/// <param name="NearlyTouching">Whether the distance is greater than 0 but only of the size of a rounding error.</param>
/// <param name="Alternative">The call with a tolerance <c>t</c> (text; the library has no global tolerance).</param>
public sealed record PredicateResult(bool Value, double Distance, bool NearlyTouching, string Alternative);

public abstract class PredicateAlgorithm(string id, string name, Func<IGeometry2D, IGeometry2D, bool> predicate, string alternative)
    : PlaygroundAlgorithm(id, name, "Predicates", [In.A(In.Any, "A"), In.B(In.Any)], [],
        (i, _, _, _) =>
        {
            var (a, b) = (i["a"], i["b"]);
            double distance = a.Distance(b);
            double scale = new[] { a.Envelope(), b.Envelope() }.Where(e => !e.IsEmpty)
                .SelectMany(e => new[] { e.MinX, e.MinY, e.MaxX, e.MaxY }).Select(Math.Abs).Append(1).Max();
            return new PredicateResult(predicate(a, b), distance, distance > 0 && distance <= 1e-9 * scale, alternative);
        });

public sealed class IntersectsAlgorithm() : PredicateAlgorithm("intersects", "Intersects", (a, b) => a.Intersects(b), "a.IsWithinDistance(b, t)");
public sealed class DisjointAlgorithm() : PredicateAlgorithm("disjoint", "Disjoint", (a, b) => a.Disjoint(b), "!a.IsWithinDistance(b, t)");
public sealed class ContainsAlgorithm() : PredicateAlgorithm("contains", "Contains", (a, b) => a.Contains(b), "a.Buffer(t).Contains(b)");
public sealed class WithinAlgorithm() : PredicateAlgorithm("within", "Within", (a, b) => a.Within(b), "b.Buffer(t).Contains(a)");
public sealed class TouchesAlgorithm() : PredicateAlgorithm("touches", "Touches", (a, b) => a.Touches(b), "a.IsWithinDistance(b, t) && !a.Buffer(-t).Intersects(b)  (areas)");
public sealed class CoversAlgorithm() : PredicateAlgorithm("covers", "Covers", (a, b) => a.Covers(b), "a.Buffer(t).Covers(b)");
public sealed class CoveredByAlgorithm() : PredicateAlgorithm("coveredBy", "CoveredBy", (a, b) => a.CoveredBy(b), "b.Buffer(t).Covers(a)");
public sealed class CrossesAlgorithm() : PredicateAlgorithm("crosses", "Crosses", (a, b) => a.Crosses(b), "no simple counterpart: IsWithinDistance for \"they meet\", NearestPoints for where");
public sealed class OverlapsAlgorithm() : PredicateAlgorithm("overlaps", "Overlaps", (a, b) => a.Overlaps(b), "a.Buffer(-t).Intersects(b.Buffer(-t)) && !a.Buffer(t).Covers(b) && !b.Buffer(t).Covers(a)  (areas)");
public sealed class EqualsTopoAlgorithm() : PredicateAlgorithm("equalsTopo", "EqualsTopo", (a, b) => a.EqualsTopo(b), "a.HausdorffDistance(b) <= t");

// ---------- Overlay ----------

public sealed class IntersectionAlgorithm() : PlaygroundAlgorithm("intersection", "Intersection", "Overlay",
    [In.A(In.Any, "A"), In.B(In.Any)], [P.FillRule, P.GridSize],
    (i, p, f, g) => i["a"].Intersection(i["b"], p.Enum<FillRule>("fillRule"), f, p.OptionalNumber("gridSize") ?? g));

public sealed class UnionAlgorithm() : PlaygroundAlgorithm("union", "Union", "Overlay",
    [In.A(In.Any, "A"), In.B(In.Any)], [P.FillRule, P.GridSize],
    (i, p, f, g) => i["a"].Union(i["b"], p.Enum<FillRule>("fillRule"), f, p.OptionalNumber("gridSize") ?? g));

public sealed class DifferenceAlgorithm() : PlaygroundAlgorithm("difference", "Difference", "Overlay",
    [In.A(In.Any, "A"), In.B(In.Any)], [P.FillRule, P.GridSize],
    (i, p, f, g) => i["a"].Difference(i["b"], p.Enum<FillRule>("fillRule"), f, p.OptionalNumber("gridSize") ?? g));

public sealed class SymDifferenceAlgorithm() : PlaygroundAlgorithm("symDifference", "SymDifference", "Overlay",
    [In.A(In.Any, "A"), In.B(In.Any)], [P.FillRule, P.GridSize],
    (i, p, f, g) => i["a"].SymDifference(i["b"], p.Enum<FillRule>("fillRule"), f, p.OptionalNumber("gridSize") ?? g));

public sealed class CutAlgorithm() : PlaygroundAlgorithm("cut", "Cut", "Overlay",
    [In.A(In.LinesAndAreas, "Polygon/line"), In.B(In.Lines, "Cutting line")], [],
    (i, _, f, g) => i["a"] switch
    {
        IPolygon2D polygon => polygon.Cut((IPolyline2D)i["b"], f, g).Cast<IGeometry2D>().ToList(),
        IPolyline2D line => line.Cut((IPolyline2D)i["b"], f, g).Cast<IGeometry2D>().ToList(),
        _ => throw new NotSupportedException("Cut needs a polygon or a line."),
    });

// ---------- Construction ----------

public sealed class BufferAlgorithm() : PlaygroundAlgorithm("buffer", "Buffer", "Construction",
    [In.A(In.Any)],
    [P.Distance(), P.Join,
     new("cap", "Cap", "enum", "Round", null, ["Round", "Flat", "Square"]),
     new("quadrantSegments", "Quadrant segments", "int", 8),
     new("mitreLimit", "Mitre limit", "number", 5.0),
     new("singleSided", "Single-sided", "bool", false)],
    (i, p, f, g) => i["a"].Buffer(p.Number("distance"), new BufferOptions
    {
        JoinStyle = p.Enum<JoinStyle>("join"),
        CapStyle = p.Enum<CapStyle>("cap"),
        QuadrantSegments = (int)p.Number("quadrantSegments"),
        MitreLimit = p.Number("mitreLimit"),
        SingleSided = p.Bool("singleSided"),
    }, f, g));

public sealed class OffsetCurveAlgorithm() : PlaygroundAlgorithm("offsetCurve", "OffsetCurve", "Construction",
    [In.A(In.LinesAndAreas, "Line/polygon")],
    [P.Distance(5, "Distance (+ left / outside)"), P.Join, new("joined", "Joined", "bool", false)],
    (i, p, f, g) =>
    {
        var options = new OffsetOptions { JoinStyle = p.Enum<JoinStyle>("join"), Joined = p.Bool("joined") };
        return i["a"] switch
        {
            IPolygon2D polygon => polygon.OffsetCurve(p.Number("distance"), options, f, g),
            IPolyline2D line => line.OffsetCurve(p.Number("distance"), options, f, g),
            _ => throw new NotSupportedException("OffsetCurve needs a line or a polygon."),
        };
    });

public sealed class ConvexHullAlgorithm() : PlaygroundAlgorithm("convexHull", "ConvexHull", "Simplify",
    [In.A(In.Any)], [], (i, _, f, _) => i["a"].ConvexHull(f));

public sealed class SimplifyAlgorithm() : PlaygroundAlgorithm("simplify", "Simplify", "Simplify",
    [In.A(In.LinesAndAreas)],
    [new("tolerance", "Tolerance", "number", 5.0, "kbs"), new("preserveTopology", "Preserve topology", "bool", false)],
    (i, p, f, g) => i["a"] switch
    {
        IPolygon2D polygon => polygon.Simplify(p.Number("tolerance"), p.Bool("preserveTopology"), f, g),
        IPolyline2D line => line.Simplify(p.Number("tolerance"), p.Bool("preserveTopology"), f),
        _ => throw new NotSupportedException("Simplify needs a line or a polygon."),
    });

// ---------- Linear referencing (ADR 0017) ----------

public sealed class InterpolateAlgorithm() : PlaygroundAlgorithm("interpolate", "Interpolate", "Linear referencing",
    [In.A(In.Lines, "Line")], [P.Distance(10, "Length along the line")],
    (i, p, _, _) => ((IPolyline2D)i["a"]).Interpolate(p.Number("distance")));

public sealed class ProjectAlgorithm() : PlaygroundAlgorithm("project", "Project", "Linear referencing",
    [In.A(In.Lines, "Line"), In.B(["Point"], "Point")], [],
    (i, _, _, _) => ((IPolyline2D)i["a"]).Project((IPoint2D)i["b"]));

public sealed class SubstringAlgorithm() : PlaygroundAlgorithm("substring", "Substring", "Linear referencing",
    [In.A(In.Lines, "Line")],
    [new("from", "From (length)", "number", 0.0, "kbs"), new("to", "To (length)", "number", 10.0, "kbs")],
    (i, p, f, _) => ((IPolyline2D)i["a"]).Substring(p.Number("from"), p.Number("to"), f));

// ---------- Measure and point helpers (ADR 0019) ----------

public sealed class NearestPointsAlgorithm() : PlaygroundAlgorithm("nearestPoints", "NearestPoints", "Measures",
    [In.A(In.Any, "A"), In.B(In.Any)], [],
    (i, _, f, _) => i["a"].NearestPoints(i["b"]) is { } pair
        ? f.CreatePolyline([new[] { pair.OnA, pair.OnB }], null)
        : null);

public sealed class IsWithinDistanceAlgorithm() : PlaygroundAlgorithm("isWithinDistance", "IsWithinDistance", "Measures",
    [In.A(In.Any, "A"), In.B(In.Any)], [P.Distance(10)],
    (i, p, _, _) => i["a"].IsWithinDistance(i["b"], p.Number("distance")));

public sealed class PointOnSurfaceAlgorithm() : PlaygroundAlgorithm("pointOnSurface", "PointOnSurface", "Measures",
    [In.A(In.Any)], [], (i, _, _, _) => i["a"].PointOnSurface());

public sealed class BoundaryAlgorithm() : PlaygroundAlgorithm("boundary", "Boundary", "Construction",
    [In.A(In.Any)], [], (i, _, f, _) => i["a"].Boundary(f));

public sealed class DensifyAlgorithm() : PlaygroundAlgorithm("densify", "Densify", "Construction",
    [In.A(In.Any)], [new("maxSegmentLength", "Maximum segment length", "number", 5.0, "kbs")],
    (i, p, f, _) => i["a"].Densify(p.Number("maxSegmentLength"), f));

public sealed class ReverseAlgorithm() : PlaygroundAlgorithm("reverse", "Reverse", "Construction",
    [In.A(In.Lines, "Line")], [], (i, _, f, _) => ((IPolyline2D)i["a"]).Reverse(f));

public sealed class MakeValidAlgorithm() : PlaygroundAlgorithm("makeValid", "MakeValid", "Validation",
    [In.A(In.Any)], [P.FillRule, P.GridSize],
    (i, p, f, g) => i["a"].MakeValid(p.Enum<FillRule>("fillRule"), f, p.OptionalNumber("gridSize") ?? g));

public sealed class ClipAlgorithm() : PlaygroundAlgorithm("clip", "Clip", "Overlay",
    [In.A(In.Any)],
    [new("minX", "Min X", "number", 0.0, "kbs"), new("minY", "Min Y", "number", 0.0, "kbs"),
     new("maxX", "Max X", "number", 100.0, "kbs"), new("maxY", "Max Y", "number", 100.0, "kbs"), P.GridSize],
    (i, p, f, g) => i["a"].Clip(new Envelope2D(p.Number("minX"), p.Number("minY"), p.Number("maxX"), p.Number("maxY")), f, p.OptionalNumber("gridSize") ?? g));

// ---------- LineMerge and Polygonize (ADR 0023) ----------

public sealed class LineMergeAlgorithm() : PlaygroundAlgorithm("lineMerge", "LineMerge", "Construction",
    [In.A(In.Lines, "Line")], [new("directed", "Directed", "bool", false)],
    (i, p, f, _) => ((IPolyline2D)i["a"]).LineMerge(p.Bool("directed"), f));

public sealed class PolygonizeAlgorithm() : PlaygroundAlgorithm("polygonize", "Polygonize", "Overlay",
    [In.A(In.Lines, "Lines")], [P.GridSize],
    (i, p, f, g) => ((IPolyline2D)i["a"]).Polygonize(f, p.OptionalNumber("gridSize") ?? g).Cast<IGeometry2D>().ToList());

// ---------- Transformations, checks and Hausdorff (ADR 0025) ----------

public sealed class TranslateAlgorithm() : PlaygroundAlgorithm("translate", "Translate", "Construction",
    [In.A(In.Any)],
    [new("dx", "Shift X", "number", 10.0, "kbs"), new("dy", "Shift Y", "number", 5.0, "kbs")],
    (i, p, f, _) => i["a"].Translate(p.Number("dx"), p.Number("dy"), f));

public sealed class ScaleAlgorithm() : PlaygroundAlgorithm("scale", "Scale", "Construction",
    [In.A(In.Any)],
    [new("sx", "Factor X", "number", 2.0), new("sy", "Factor Y", "number", 0.5),
     new("originX", "Fixed point X", "number", 0.0, "kbs"), new("originY", "Fixed point Y", "number", 0.0, "kbs")],
    (i, p, f, _) => i["a"].Scale(p.Number("sx"), p.Number("sy"), new Coordinate(p.Number("originX"), p.Number("originY")), f));

public sealed class RotateAlgorithm() : PlaygroundAlgorithm("rotate", "Rotate", "Construction",
    [In.A(In.Any)],
    [new("degrees", "Angle (degrees, counter-clockwise)", "number", 30.0),
     new("originX", "Centre X", "number", 0.0, "kbs"), new("originY", "Centre Y", "number", 0.0, "kbs")],
    (i, p, f, _) => i["a"].Rotate(p.Number("degrees") * Math.PI / 180, new Coordinate(p.Number("originX"), p.Number("originY")), f));

public sealed class IsSimpleAlgorithm() : PlaygroundAlgorithm("isSimple", "IsSimple", "Validation", [In.A(In.Any)], [], (i, _, _, _) => i["a"].IsSimple());

public sealed class IsClosedAlgorithm() : PlaygroundAlgorithm("isClosed", "IsClosed", "Validation", [In.A(In.Lines, "Line")], [], (i, _, _, _) => ((IPolyline2D)i["a"]).IsClosed());

public sealed class IsRingAlgorithm() : PlaygroundAlgorithm("isRing", "IsRing", "Validation", [In.A(In.Lines, "Line")], [], (i, _, _, _) => ((IPolyline2D)i["a"]).IsRing());

public sealed class EqualsExactAlgorithm() : PlaygroundAlgorithm("equalsExact", "EqualsExact", "Predicates",
    [In.A(In.Any, "A"), In.B(In.Any)], [new("tolerance", "Tolerance", "number", 0.0, "kbs")],
    (i, p, _, _) => i["a"].EqualsExact(i["b"], p.Number("tolerance")));

public sealed class HausdorffDistanceAlgorithm() : PlaygroundAlgorithm("hausdorffDistance", "HausdorffDistance", "Measures",
    [In.A(In.Any, "A"), In.B(In.Any)], [new("densifyFraction", "Densify fraction (0–1]", "number", 1.0)],
    (i, p, _, _) => i["a"].HausdorffDistance(i["b"], p.Number("densifyFraction")));

// ---------- Bounding shapes and label point (ADR 0026) ----------

public sealed class MinimumRotatedRectangleAlgorithm() : PlaygroundAlgorithm("minimumRotatedRectangle", "MinimumRotatedRectangle", "Construction",
    [In.A(In.Any)], [], (i, _, f, _) => i["a"].MinimumRotatedRectangle(f));

public sealed class MinimumBoundingCircleAlgorithm() : PlaygroundAlgorithm("minimumBoundingCircle", "MinimumBoundingCircle", "Construction",
    [In.A(In.Any)], [new("quadrantSegments", "Segments per quarter circle", "int", 16)],
    (i, p, f, _) => (object?)i["a"].MinimumBoundingCircle()?.ToPolygon(f, (int)p.Number("quadrantSegments")) ?? f.CreatePolygon([], null));

public sealed class PolylabelAlgorithm() : PlaygroundAlgorithm("polylabel", "Polylabel", "Measures",
    [In.A(In.Areas, "Polygon")], [new("tolerance", "Tolerance (empty = auto)", "number", null, "kbs")],
    (i, p, _, _) => ((IPolygon2D)i["a"]).Polylabel(p.OptionalNumber("tolerance")));

// ---------- Clean-up (ADR 0027) ----------

public sealed class RemoveHolesAlgorithm() : PlaygroundAlgorithm("removeHoles", "RemoveHoles", "Construction",
    [In.A(In.Areas, "Polygon")], [new("minArea", "Smallest area of a hole to keep (empty = remove all)", "number", null)],
    (i, p, f, _) => ((IPolygon2D)i["a"]).RemoveHoles(p.OptionalNumber("minArea") ?? double.PositiveInfinity, f));

public sealed class RemoveSmallPartsAlgorithm() : PlaygroundAlgorithm("removeSmallParts", "RemoveSmallParts", "Construction",
    [In.A(In.Any)], [new("minSize", "Smallest size to keep (area or length)", "number", 10.0)],
    (i, p, f, _) => i["a"].RemoveSmallParts(p.Number("minSize"), f));

public sealed class SnapToGridAlgorithm() : PlaygroundAlgorithm("snapToGrid", "SnapToGrid", "Construction",
    [In.A(In.Any)], [new("gridSize", "Grid size", "number", 1.0, "kbs")],
    (i, p, f, _) => i["a"].SnapToGrid(p.Number("gridSize"), f));

public sealed class ReducePrecisionAlgorithm() : PlaygroundAlgorithm("reducePrecision", "ReducePrecision", "Construction",
    [In.A(In.Any)], [new("gridSize", "Grid size", "number", 1.0, "kbs")],
    (i, p, f, _) => i["a"].ReducePrecision(p.Number("gridSize"), f));

// ---------- Linear referencing by M (ADR 0015/0016): M reaches the playground through WKT ----------

public sealed class InterpolateByMAlgorithm() : PlaygroundAlgorithm("interpolateByM", "InterpolateByM", "Linear referencing",
    [In.A(In.Lines, "Line (WKT with M)")], [new("m", "M value", "number", 150.0)],
    (i, p, _, _) => ((IPolyline2D)i["a"]).InterpolateByM(p.Number("m")));

public sealed class ProjectToMAlgorithm() : PlaygroundAlgorithm("projectToM", "ProjectToM", "Linear referencing",
    [In.A(In.Lines, "Line (WKT with M)"), In.B(["Point"], "Point")], [],
    (i, _, _, _) => ((IPolyline2D)i["a"]).ProjectToM((IPoint2D)i["b"]));

public sealed class SubstringByMAlgorithm() : PlaygroundAlgorithm("substringByM", "SubstringByM", "Linear referencing",
    [In.A(In.Lines, "Line (WKT with M)")],
    [new("fromM", "From (M)", "number", 50.0), new("toM", "To (M)", "number", 200.0)],
    (i, p, f, _) => ((IPolyline2D)i["a"]).SubstringByM(p.Number("fromM"), p.Number("toM"), f));

public sealed class LocateBetweenAlgorithm() : PlaygroundAlgorithm("locateBetween", "LocateBetween", "Linear referencing",
    [In.A(In.Lines, "Line (WKT with M)")],
    [new("fromM", "From (M)", "number", 50.0), new("toM", "To (M)", "number", 200.0)],
    (i, p, f, _) => ((IPolyline2D)i["a"]).LocateBetween(p.Number("fromM"), p.Number("toM"), f));

public sealed class LocateAlongAlgorithm() : PlaygroundAlgorithm("locateAlong", "LocateAlong", "Linear referencing",
    [In.A(In.Lines, "Line (WKT with M)")], [new("m", "M value", "number", 150.0)],
    (i, p, f, _) => ((IPolyline2D)i["a"]).LocateAlong(p.Number("m"), f));

// ---------- Delaunay triangulation and concave hull (ADR 0029) ----------

public sealed class DelaunayTrianglesAlgorithm() : PlaygroundAlgorithm("delaunayTriangles", "DelaunayTriangles", "Construction",
    [In.A(In.Any)], [],
    (i, _, f, _) => i["a"].DelaunayTriangles(f).Cast<IGeometry2D>().ToList());

public sealed class ConcaveHullAlgorithm() : PlaygroundAlgorithm("concaveHull", "ConcaveHull", "Construction",
    [In.A(In.Any)],
    [new("maxEdgeLength", "Longest border edge", "number", 20.0, "kbs"), new("holesAllowed", "Holes allowed", "bool", false)],
    (i, p, f, _) => i["a"].ConcaveHull(p.Number("maxEdgeLength"), p.Bool("holesAllowed"), f));

public sealed class ConcaveHullByRatioAlgorithm() : PlaygroundAlgorithm("concaveHullByRatio", "ConcaveHullByRatio", "Construction",
    [In.A(In.Any)],
    [new("edgeLengthRatio", "Edge length ratio (0–1)", "number", 0.3), new("holesAllowed", "Holes allowed", "bool", false)],
    (i, p, f, _) => i["a"].ConcaveHullByRatio(p.Number("edgeLengthRatio"), p.Bool("holesAllowed"), f));

// ---------- Voronoi diagram, triangulations and terrain (ADR 0036, ADR 0037, ADR 0038) ----------

public sealed class VoronoiDiagramAlgorithm() : PlaygroundAlgorithm("voronoiDiagram", "VoronoiDiagram", "Triangulation",
    [In.A(In.Any, "Sites"), In.B(In.Areas, "Clip area (optional)", optional: true)], [P.GridSize],
    (i, p, f, g) => i["a"].VoronoiDiagram(i.TryGetValue("b", out var clip) ? (IPolygon2D)clip : null, f, p.OptionalNumber("gridSize") ?? g)
        .Select(c => (IGeometry2D)c.Cell).ToList());

public sealed class VoronoiEdgesAlgorithm() : PlaygroundAlgorithm("voronoiEdges", "VoronoiEdges", "Triangulation",
    [In.A(In.Any, "Sites"), In.B(In.Areas, "Clip area (optional)", optional: true)], [P.GridSize],
    (i, p, f, g) => i["a"].VoronoiEdges(i.TryGetValue("b", out var clip) ? (IPolygon2D)clip : null, f, p.OptionalNumber("gridSize") ?? g));

public sealed class DelaunayEdgesAlgorithm() : PlaygroundAlgorithm("delaunayEdges", "DelaunayEdges", "Triangulation",
    [In.A(In.Any)], [],
    (i, _, f, _) => i["a"].DelaunayEdges(f));

public sealed class TriangulateAlgorithm() : PlaygroundAlgorithm("triangulate", "Triangulate (polygon)", "Triangulation",
    [In.A(In.Areas, "Polygon")], [P.GridSize],
    (i, p, f, g) => ((IPolygon2D)i["a"]).Triangulate(f, p.OptionalNumber("gridSize") ?? g).Cast<IGeometry2D>().ToList());

public sealed class ConstrainedDelaunayAlgorithm() : PlaygroundAlgorithm("constrainedDelaunay", "ConstrainedDelaunay", "Triangulation",
    [In.A(In.Any, "Points"), In.B(In.LinesAndAreas, "Break lines")], [P.GridSize],
    (i, p, f, g) => Triangulation.FromPointsAndLines(i["a"], i["b"], f, p.OptionalNumber("gridSize") ?? g).ToPolygons(f).Cast<IGeometry2D>().ToList());

public sealed class InterpolateZAlgorithm() : PlaygroundAlgorithm("interpolateZ", "InterpolateZ (height)", "Triangulation",
    [In.A(In.Any, "Points with Z (WKT)"), In.B(["Point"], "Point")], [],
    (i, _, _, _) =>
    {
        var point = (IPoint2D)i["b"];
        return Triangulation.FromPoints(i["a"]).InterpolateZ(new Coordinate(point.X, point.Y));
    });

public sealed class ContourAlgorithm() : PlaygroundAlgorithm("contour", "Contour (lines of equal height)", "Triangulation",
    [In.A(In.Any, "Points with Z (WKT)")],
    [new("z", "Height", "number", 50.0), new("interval", "Interval (0 = only the height above)", "number", 0.0)],
    (i, p, f, _) =>
    {
        var triangulation = Triangulation.FromPoints(i["a"]);
        double interval = p.Number("interval");
        if (!(interval > 0))
        {
            return triangulation.Contour(p.Number("z"), f);
        }

        var heights = triangulation.Vertices.Where(v => v.HasZ).Select(v => v.Z).ToList();
        if (heights.Count == 0)
        {
            return new List<IGeometry2D>();
        }

        var levels = new List<IGeometry2D>();
        for (double level = Math.Ceiling(heights.Min() / interval) * interval; level <= heights.Max() && levels.Count < 200; level += interval)
        {
            levels.Add(triangulation.Contour(level, f));
        }

        return levels;
    });

// ---------- Terrain analysis (ADR 0039) and events on a route (ADR 0040) ----------

public sealed class DrapeAlgorithm() : PlaygroundAlgorithm("drape", "Drape (line over terrain)", "Triangulation",
    [In.A(In.Any, "Points with Z (WKT)"), In.B(In.Lines, "Line")], [],
    (i, _, f, _) => Triangulation.FromPoints(i["a"]).Drape((IPolyline2D)i["b"], f));

public sealed class SteepTrianglesAlgorithm() : PlaygroundAlgorithm("steepTriangles", "Steep triangles (slope)", "Triangulation",
    [In.A(In.Any, "Points with Z (WKT)")],
    [new("minSlope", "At least this steep (degrees)", "number", 55.0)],
    (i, p, f, _) =>
    {
        var triangulation = Triangulation.FromPoints(i["a"]);
        double min = p.Number("minSlope");
        var triangles = triangulation.ToPolygons(f);
        return Enumerable.Range(0, triangulation.TriangleCount).Where(t => triangulation.Slope(t) >= min).Select(t => (IGeometry2D)triangles[t]).ToList();
    });

public sealed class TerrainVolumeAlgorithm() : PlaygroundAlgorithm("terrainVolume", "Volume above a height", "Triangulation",
    [In.A(In.Any, "Points with Z (WKT)")],
    [new("z", "Height of the plane", "number", 50.0)],
    (i, p, _, _) =>
    {
        var (above, below) = Triangulation.FromPoints(i["a"]).Volume(p.Number("z"));
        return above - below;    // the net volume: positive if more of the terrain lies above the plane than is missing below it
    });

// ---------- Natural neighbour, quality meshes, surfaces (ADR 0041) ----------

public sealed class NaturalNeighbourAlgorithm() : PlaygroundAlgorithm("naturalNeighbour", "InterpolateNaturalNeighbour (smooth height)", "Triangulation",
    [In.A(In.Any, "Points with Z (WKT)"), In.B(["Point"], "Point")], [],
    (i, _, _, _) =>
    {
        var point = (IPoint2D)i["b"];
        return Triangulation.FromPoints(i["a"]).InterpolateNaturalNeighbour(new Coordinate(point.X, point.Y));
    });

public sealed class RefineAlgorithm() : PlaygroundAlgorithm("refine", "Refine (quality mesh)", "Triangulation",
    [In.A(In.Any, "Points (with Z) or a polygon")],
    [new("minAngle", "Smallest angle (degrees, 0 to 34)", "number", 25.0), new("maxArea", "Largest area (empty = no limit)", "number")],
    (i, p, f, _) =>
    {
        var triangulation = i["a"] is IPolygon2D polygon ? Triangulation.FromPolygon(polygon, f) : Triangulation.FromPoints(i["a"]);
        return triangulation.Refine(p.Number("minAngle"), p.OptionalNumber("maxArea")).ToPolygons(f).Cast<IGeometry2D>().ToList();
    });

public sealed class SlopeZonesAlgorithm() : PlaygroundAlgorithm("slopeZones", "SlopeZones (areas by slope)", "Triangulation",
    [In.A(In.Any, "Points with Z (WKT)")],
    [new("limits", "Limits in degrees, separated by commas", "text", "10, 30, 50")],
    (i, p, f, _) =>
    {
        var limits = p.Text("limits").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => double.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return Triangulation.FromPoints(i["a"]).SlopeZones(limits, f).Cast<IGeometry2D>().ToList();
    });

public sealed class LineOfSightAlgorithm() : PlaygroundAlgorithm("lineOfSight", "LineOfSight (can it be seen)", "Triangulation",
    [In.A(In.Any, "Points with Z (WKT)"), In.B(In.Lines, "Line from the observer to the target")],
    [new("observerHeight", "Height of the eye", "number", 1.7), new("targetHeight", "Height of the target", "number", 0.0)],
    (i, p, _, _) =>
    {
        var path = ((IPolyline2D)i["b"])[0];
        var from = path[0];
        var to = path[path.PointCount - 1];
        return Triangulation.FromPoints(i["a"]).LineOfSight(new Coordinate(from.X, from.Y), new Coordinate(to.X, to.Y), p.Number("observerHeight"), p.Number("targetHeight"));
    });

public sealed class SurfaceVolumeAlgorithm() : PlaygroundAlgorithm("surfaceVolume", "Volume between two surfaces", "Triangulation",
    [In.A(In.Any, "Surface 1: points with Z (WKT)"), In.B(In.Any, "Surface 2: points with Z (WKT)")], [],
    (i, _, _, _) =>
    {
        var (above, below) = Triangulation.FromPoints(i["a"]).Volume(Triangulation.FromPoints(i["b"]));
        return above - below;    // the net volume: positive if surface 1 lies above surface 2 on the whole
    });

/// <summary>The tables of events that a text parameter holds: <c>from..to value</c> for a line event, <c>m value</c> for a point event; <c>#</c> starts a comment.</summary>
internal static class EventText
{
    public static (List<LineEvent<string>> Lines, List<PointEvent<string>> Points) Parse(string text)
    {
        var lines = new List<LineEvent<string>>();
        var points = new List<PointEvent<string>>();
        int number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            var value = parts.Length > 1 ? parts[1].Trim() : "";
            var range = parts[0].Split("..", 2);
            bool Read(string s, out double d) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) && double.IsFinite(d);

            if (range.Length == 2 && Read(range[0], out double from) && Read(range[1], out double to))
            {
                lines.Add(new LineEvent<string>(from, to, value));
            }
            else if (range.Length == 1 && Read(range[0], out double m))
            {
                points.Add(new PointEvent<string>(m, value));
            }
            else
            {
                throw new ArgumentException($"Line {number} of the events is not \"from..to value\" or \"m value\": {line}");
            }
        }

        return (lines, points);
    }
}

public sealed class LocateEventsAlgorithm() : PlaygroundAlgorithm("locateEvents", "LocateEvents (draw on the route)", "Linear referencing",
    [In.A(In.Lines, "Route (WKT with M)")],
    [new("events", "Events: from..to value, or m value", "text", "12000..12150 50\n12150..12360 70\n12360..12520 30\n12080 bridge\n12300 bridge")],
    (i, p, f, _) =>
    {
        var route = (IPolyline2D)i["a"];
        var (lines, points) = EventText.Parse(p.Text("events"));
        var shapes = route.LocateEvents(lines, f).Select(x => (IGeometry2D)x.Shape);
        var spots = route.LocateEvents(points, f).Select(x => (IGeometry2D)x.Points);
        return shapes.Concat(spots).ToList();
    });

public sealed class IntersectEventsAlgorithm() : PlaygroundAlgorithm("intersectEvents", "IntersectEvents (where both hold)", "Linear referencing",
    [In.A(In.Lines, "Route (WKT with M)")],
    [
        new("tableA", "Table A: from..to value", "text", "12000..12150 50\n12150..12520 70"),
        new("tableB", "Table B: from..to value", "text", "12000..12100 asphalt\n12100..12400 gravel\n12400..12520 asphalt"),
    ],
    (i, p, f, _) =>
    {
        var route = (IPolyline2D)i["a"];
        var both = LineEvents.Intersect(EventText.Parse(p.Text("tableA")).Lines, EventText.Parse(p.Text("tableB")).Lines, (a, b) => $"{a}, {b}");
        return route.LocateEvents(both, f).Select(x => (IGeometry2D)x.Shape).ToList();
    });
