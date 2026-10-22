namespace SpatialNET.Playground.Algorithms;

// Deskriptoren aller öffentlichen Algorithmen (ADR 0011). Neue Algorithmen bekommen hier eine Klasse;
// der Test PlaygroundCompletenessTests erzwingt, dass keiner fehlt.

internal static class In
{
    public static readonly string[] Any = ["Point", "MultiPoint", "LineString", "Polygon"];
    public static readonly string[] Lines = ["LineString"];
    public static readonly string[] Areas = ["Polygon"];
    public static readonly string[] LinesAndAreas = ["LineString", "Polygon"];

    public static InputDescriptor A(string[] types, string label = "Eingabe") => new("a", label, types);
    public static InputDescriptor B(string[] types, string label = "B") => new("b", label, types);
}

internal static class P
{
    public static ParameterDescriptor Distance(double d = 10, string label = "Distanz") => new("distance", label, "number", d, "kbs");
    public static ParameterDescriptor Join => new("join", "Join", "enum", "Round", null, ["Round", "Mitre", "Bevel"]);
    public static ParameterDescriptor FillRule => new("fillRule", "Füllregel", "enum", "EvenOdd", null, ["EvenOdd", "NonZero", "Positive", "Negative"]);
    public static ParameterDescriptor GridSize => new("gridSize", "Gitterweite (leer = auto)", "number", null, "kbs");
}

// ---------- Maße ----------

public sealed class AreaAlgorithm() : PlaygroundAlgorithm("area", "Area", "Maße", [In.A(In.Any)], [], (i, _, _, _) => i["a"].Area());
public sealed class LengthAlgorithm() : PlaygroundAlgorithm("length", "Length", "Maße", [In.A(In.Any)], [], (i, _, _, _) => i["a"].Length());
public sealed class EnvelopeAlgorithm() : PlaygroundAlgorithm("envelope", "Envelope", "Maße", [In.A(In.Any)], [], (i, _, _, _) => i["a"].Envelope());
public sealed class CentroidAlgorithm() : PlaygroundAlgorithm("centroid", "Centroid", "Maße", [In.A(In.Any)], [], (i, _, _, _) => i["a"].Centroid());
public sealed class DistanceAlgorithm() : PlaygroundAlgorithm("distance", "Distance", "Maße", [In.A(In.Any, "A"), In.B(In.Any)], [], (i, _, _, _) => i["a"].Distance(i["b"]));

// ---------- Prüfung und Prädikate ----------

public sealed class IsValidAlgorithm() : PlaygroundAlgorithm("isValid", "IsValid", "Prüfung", [In.A(In.Any)], [], (i, _, _, _) => i["a"].IsValid());
public sealed class ValidateAlgorithm() : PlaygroundAlgorithm("validate", "Validate", "Prüfung", [In.A(In.Any)], [], (i, _, _, _) => i["a"].Validate());
public sealed class RelateAlgorithm() : PlaygroundAlgorithm("relate", "Relate (DE-9IM)", "Prädikate", [In.A(In.Any, "A"), In.B(In.Any)], [], (i, _, _, _) => i["a"].Relate(i["b"]));

/// <summary>
/// Ergebnis eines Prädikats samt Abstand der Eingaben. Ein Prädikat rechnet exakt: Ein gezeichneter Punkt, der optisch auf einer Linie
/// liegt, liegt rechnerisch fast nie genau darauf. Der Abstand zeigt, ob ein „false“ nur an der Rundung der Koordinaten liegt.
/// </summary>
/// <param name="Value">Das Ergebnis des Prädikats.</param>
/// <param name="Distance">Der Abstand der beiden Eingaben (<c>NaN</c>, wenn eine leer ist).</param>
/// <param name="WithinTolerance">Ob der Abstand höchstens die angegebene Toleranz beträgt; <c>null</c> ohne Toleranz.</param>
/// <param name="NearlyTouching">Ob der Abstand größer als 0, aber nur um Rundungsgröße ist.</param>
public sealed record PredicateResult(bool Value, double Distance, bool? WithinTolerance, bool NearlyTouching);

public abstract class PredicateAlgorithm(string id, string name, Func<IGeometry2D, IGeometry2D, bool> predicate)
    : PlaygroundAlgorithm(id, name, "Prädikate", [In.A(In.Any, "A"), In.B(In.Any)],
        [new("tolerance", "Toleranz (nur Hinweis, das Prädikat bleibt exakt)", "number", null, "kbs")],
        (i, p, _, _) =>
        {
            var (a, b) = (i["a"], i["b"]);
            double distance = a.Distance(b);
            double scale = new[] { a.Envelope(), b.Envelope() }.Where(e => !e.IsEmpty)
                .SelectMany(e => new[] { e.MinX, e.MinY, e.MaxX, e.MaxY }).Select(Math.Abs).Append(1).Max();
            return new PredicateResult(predicate(a, b), distance,
                p.OptionalNumber("tolerance") is { } tolerance && !double.IsNaN(distance) ? distance <= tolerance : null,
                distance > 0 && distance <= 1e-9 * scale);
        });

public sealed class IntersectsAlgorithm() : PredicateAlgorithm("intersects", "Intersects", (a, b) => a.Intersects(b));
public sealed class DisjointAlgorithm() : PredicateAlgorithm("disjoint", "Disjoint", (a, b) => a.Disjoint(b));
public sealed class ContainsAlgorithm() : PredicateAlgorithm("contains", "Contains", (a, b) => a.Contains(b));
public sealed class WithinAlgorithm() : PredicateAlgorithm("within", "Within", (a, b) => a.Within(b));
public sealed class TouchesAlgorithm() : PredicateAlgorithm("touches", "Touches", (a, b) => a.Touches(b));
public sealed class CoversAlgorithm() : PredicateAlgorithm("covers", "Covers", (a, b) => a.Covers(b));
public sealed class CoveredByAlgorithm() : PredicateAlgorithm("coveredBy", "CoveredBy", (a, b) => a.CoveredBy(b));
public sealed class CrossesAlgorithm() : PredicateAlgorithm("crosses", "Crosses", (a, b) => a.Crosses(b));
public sealed class OverlapsAlgorithm() : PredicateAlgorithm("overlaps", "Overlaps", (a, b) => a.Overlaps(b));
public sealed class EqualsTopoAlgorithm() : PredicateAlgorithm("equalsTopo", "EqualsTopo", (a, b) => a.EqualsTopo(b));

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
    [In.A(In.LinesAndAreas, "Polygon/Linie"), In.B(In.Lines, "Schnittlinie")], [],
    (i, _, f, g) => i["a"] switch
    {
        IPolygon2D polygon => polygon.Cut((IPolyline2D)i["b"], f, g).Cast<IGeometry2D>().ToList(),
        IPolyline2D line => line.Cut((IPolyline2D)i["b"], f, g).Cast<IGeometry2D>().ToList(),
        _ => throw new NotSupportedException("Cut braucht ein Polygon oder eine Linie."),
    });

// ---------- Konstruktion ----------

public sealed class BufferAlgorithm() : PlaygroundAlgorithm("buffer", "Buffer", "Konstruktion",
    [In.A(In.Any)],
    [P.Distance(), P.Join,
     new("cap", "Cap", "enum", "Round", null, ["Round", "Flat", "Square"]),
     new("quadrantSegments", "Quadrant-Segmente", "int", 8),
     new("mitreLimit", "Mitre-Limit", "number", 5.0),
     new("singleSided", "Einseitig", "bool", false)],
    (i, p, f, g) => i["a"].Buffer(p.Number("distance"), new BufferOptions
    {
        JoinStyle = p.Enum<JoinStyle>("join"),
        CapStyle = p.Enum<CapStyle>("cap"),
        QuadrantSegments = (int)p.Number("quadrantSegments"),
        MitreLimit = p.Number("mitreLimit"),
        SingleSided = p.Bool("singleSided"),
    }, f, g));

public sealed class OffsetCurveAlgorithm() : PlaygroundAlgorithm("offsetCurve", "OffsetCurve", "Konstruktion",
    [In.A(In.LinesAndAreas, "Linie/Polygon")],
    [P.Distance(5, "Distanz (+ links / außen)"), P.Join, new("joined", "Verbunden", "bool", false)],
    (i, p, f, g) =>
    {
        var options = new OffsetOptions { JoinStyle = p.Enum<JoinStyle>("join"), Joined = p.Bool("joined") };
        return i["a"] switch
        {
            IPolygon2D polygon => polygon.OffsetCurve(p.Number("distance"), options, f, g),
            IPolyline2D line => line.OffsetCurve(p.Number("distance"), options, f, g),
            _ => throw new NotSupportedException("OffsetCurve braucht eine Linie oder ein Polygon."),
        };
    });

public sealed class ConvexHullAlgorithm() : PlaygroundAlgorithm("convexHull", "ConvexHull", "Vereinfachen",
    [In.A(In.Any)], [], (i, _, f, _) => i["a"].ConvexHull(f));

public sealed class SimplifyAlgorithm() : PlaygroundAlgorithm("simplify", "Simplify", "Vereinfachen",
    [In.A(In.LinesAndAreas)],
    [new("tolerance", "Toleranz", "number", 5.0, "kbs"), new("preserveTopology", "Topologie erhalten", "bool", false)],
    (i, p, f, g) => i["a"] switch
    {
        IPolygon2D polygon => polygon.Simplify(p.Number("tolerance"), p.Bool("preserveTopology"), f, g),
        IPolyline2D line => line.Simplify(p.Number("tolerance"), p.Bool("preserveTopology"), f),
        _ => throw new NotSupportedException("Simplify braucht eine Linie oder ein Polygon."),
    });

// ---------- Lineare Referenzierung (ADR 0017) ----------

public sealed class InterpolateAlgorithm() : PlaygroundAlgorithm("interpolate", "Interpolate", "Lineare Referenzierung",
    [In.A(In.Lines, "Linie")], [P.Distance(10, "Länge entlang der Linie")],
    (i, p, _, _) => ((IPolyline2D)i["a"]).Interpolate(p.Number("distance")));

public sealed class ProjectAlgorithm() : PlaygroundAlgorithm("project", "Project", "Lineare Referenzierung",
    [In.A(In.Lines, "Linie"), In.B(["Point"], "Punkt")], [],
    (i, _, _, _) => ((IPolyline2D)i["a"]).Project((IPoint2D)i["b"]));

public sealed class SubstringAlgorithm() : PlaygroundAlgorithm("substring", "Substring", "Lineare Referenzierung",
    [In.A(In.Lines, "Linie")],
    [new("from", "Von (Länge)", "number", 0.0, "kbs"), new("to", "Bis (Länge)", "number", 10.0, "kbs")],
    (i, p, f, _) => ((IPolyline2D)i["a"]).Substring(p.Number("from"), p.Number("to"), f));

// ---------- Mess- und Punkt-Helfer (ADR 0019) ----------

public sealed class NearestPointsAlgorithm() : PlaygroundAlgorithm("nearestPoints", "NearestPoints", "Maße",
    [In.A(In.Any, "A"), In.B(In.Any)], [],
    (i, _, f, _) => i["a"].NearestPoints(i["b"]) is { } pair
        ? f.CreatePolyline([new[] { pair.OnA, pair.OnB }], null)
        : null);

public sealed class IsWithinDistanceAlgorithm() : PlaygroundAlgorithm("isWithinDistance", "IsWithinDistance", "Maße",
    [In.A(In.Any, "A"), In.B(In.Any)], [P.Distance(10)],
    (i, p, _, _) => i["a"].IsWithinDistance(i["b"], p.Number("distance")));

public sealed class PointOnSurfaceAlgorithm() : PlaygroundAlgorithm("pointOnSurface", "PointOnSurface", "Maße",
    [In.A(In.Any)], [], (i, _, _, _) => i["a"].PointOnSurface());

public sealed class BoundaryAlgorithm() : PlaygroundAlgorithm("boundary", "Boundary", "Konstruktion",
    [In.A(In.Any)], [], (i, _, f, _) => i["a"].Boundary(f));

public sealed class DensifyAlgorithm() : PlaygroundAlgorithm("densify", "Densify", "Konstruktion",
    [In.A(In.Any)], [new("maxSegmentLength", "Maximale Segmentlänge", "number", 5.0, "kbs")],
    (i, p, f, _) => i["a"].Densify(p.Number("maxSegmentLength"), f));

public sealed class ReverseAlgorithm() : PlaygroundAlgorithm("reverse", "Reverse", "Konstruktion",
    [In.A(In.Lines, "Linie")], [], (i, _, f, _) => ((IPolyline2D)i["a"]).Reverse(f));

public sealed class MakeValidAlgorithm() : PlaygroundAlgorithm("makeValid", "MakeValid", "Prüfung",
    [In.A(In.Any)], [P.FillRule, P.GridSize],
    (i, p, f, g) => i["a"].MakeValid(p.Enum<FillRule>("fillRule"), f, p.OptionalNumber("gridSize") ?? g));

public sealed class ClipAlgorithm() : PlaygroundAlgorithm("clip", "Clip", "Overlay",
    [In.A(In.Any)],
    [new("minX", "Min X", "number", 0.0, "kbs"), new("minY", "Min Y", "number", 0.0, "kbs"),
     new("maxX", "Max X", "number", 100.0, "kbs"), new("maxY", "Max Y", "number", 100.0, "kbs"), P.GridSize],
    (i, p, f, g) => i["a"].Clip(new Envelope2D(p.Number("minX"), p.Number("minY"), p.Number("maxX"), p.Number("maxY")), f, p.OptionalNumber("gridSize") ?? g));

// ---------- LineMerge und Polygonize (ADR 0023) ----------

public sealed class LineMergeAlgorithm() : PlaygroundAlgorithm("lineMerge", "LineMerge", "Konstruktion",
    [In.A(In.Lines, "Linie")], [new("directed", "Gerichtet", "bool", false)],
    (i, p, f, _) => ((IPolyline2D)i["a"]).LineMerge(p.Bool("directed"), f));

public sealed class PolygonizeAlgorithm() : PlaygroundAlgorithm("polygonize", "Polygonize", "Overlay",
    [In.A(In.Lines, "Linien")], [P.GridSize],
    (i, p, f, g) => ((IPolyline2D)i["a"]).Polygonize(f, p.OptionalNumber("gridSize") ?? g).Cast<IGeometry2D>().ToList());

// ---------- Transformationen, Prüfungen und Hausdorff (ADR 0025) ----------

public sealed class TranslateAlgorithm() : PlaygroundAlgorithm("translate", "Translate", "Konstruktion",
    [In.A(In.Any)],
    [new("dx", "Verschiebung X", "number", 10.0, "kbs"), new("dy", "Verschiebung Y", "number", 5.0, "kbs")],
    (i, p, f, _) => i["a"].Translate(p.Number("dx"), p.Number("dy"), f));

public sealed class ScaleAlgorithm() : PlaygroundAlgorithm("scale", "Scale", "Konstruktion",
    [In.A(In.Any)],
    [new("sx", "Faktor X", "number", 2.0), new("sy", "Faktor Y", "number", 0.5),
     new("originX", "Fixpunkt X", "number", 0.0, "kbs"), new("originY", "Fixpunkt Y", "number", 0.0, "kbs")],
    (i, p, f, _) => i["a"].Scale(p.Number("sx"), p.Number("sy"), new Coordinate(p.Number("originX"), p.Number("originY")), f));

public sealed class RotateAlgorithm() : PlaygroundAlgorithm("rotate", "Rotate", "Konstruktion",
    [In.A(In.Any)],
    [new("degrees", "Winkel (Grad, gegen den Uhrzeigersinn)", "number", 30.0),
     new("originX", "Mittelpunkt X", "number", 0.0, "kbs"), new("originY", "Mittelpunkt Y", "number", 0.0, "kbs")],
    (i, p, f, _) => i["a"].Rotate(p.Number("degrees") * Math.PI / 180, new Coordinate(p.Number("originX"), p.Number("originY")), f));

public sealed class IsSimpleAlgorithm() : PlaygroundAlgorithm("isSimple", "IsSimple", "Prüfung", [In.A(In.Any)], [], (i, _, _, _) => i["a"].IsSimple());

public sealed class IsClosedAlgorithm() : PlaygroundAlgorithm("isClosed", "IsClosed", "Prüfung", [In.A(In.Lines, "Linie")], [], (i, _, _, _) => ((IPolyline2D)i["a"]).IsClosed());

public sealed class IsRingAlgorithm() : PlaygroundAlgorithm("isRing", "IsRing", "Prüfung", [In.A(In.Lines, "Linie")], [], (i, _, _, _) => ((IPolyline2D)i["a"]).IsRing());

public sealed class EqualsExactAlgorithm() : PlaygroundAlgorithm("equalsExact", "EqualsExact", "Prädikate",
    [In.A(In.Any, "A"), In.B(In.Any)], [new("tolerance", "Toleranz", "number", 0.0, "kbs")],
    (i, p, _, _) => i["a"].EqualsExact(i["b"], p.Number("tolerance")));

public sealed class HausdorffDistanceAlgorithm() : PlaygroundAlgorithm("hausdorffDistance", "HausdorffDistance", "Maße",
    [In.A(In.Any, "A"), In.B(In.Any)], [new("densifyFraction", "Verdichtungsanteil (0–1]", "number", 1.0)],
    (i, p, _, _) => i["a"].HausdorffDistance(i["b"], p.Number("densifyFraction")));

// ---------- Hüllformen und Beschriftungspunkt (ADR 0026) ----------

public sealed class MinimumRotatedRectangleAlgorithm() : PlaygroundAlgorithm("minimumRotatedRectangle", "MinimumRotatedRectangle", "Konstruktion",
    [In.A(In.Any)], [], (i, _, f, _) => i["a"].MinimumRotatedRectangle(f));

public sealed class MinimumBoundingCircleAlgorithm() : PlaygroundAlgorithm("minimumBoundingCircle", "MinimumBoundingCircle", "Konstruktion",
    [In.A(In.Any)], [new("quadrantSegments", "Segmente je Viertelkreis", "int", 16)],
    (i, p, f, _) => (object?)i["a"].MinimumBoundingCircle()?.ToPolygon(f, (int)p.Number("quadrantSegments")) ?? f.CreatePolygon([], null));

public sealed class PolylabelAlgorithm() : PlaygroundAlgorithm("polylabel", "Polylabel", "Maße",
    [In.A(In.Areas, "Polygon")], [new("tolerance", "Toleranz (leer = auto)", "number", null, "kbs")],
    (i, p, _, _) => ((IPolygon2D)i["a"]).Polylabel(p.OptionalNumber("tolerance")));

// ---------- Bereinigen (ADR 0027) ----------

public sealed class RemoveHolesAlgorithm() : PlaygroundAlgorithm("removeHoles", "RemoveHoles", "Konstruktion",
    [In.A(In.Areas, "Polygon")], [new("minArea", "Mindestfläche eines Lochs (leer = alle entfernen)", "number", null)],
    (i, p, f, _) => ((IPolygon2D)i["a"]).RemoveHoles(p.OptionalNumber("minArea") ?? double.PositiveInfinity, f));

public sealed class RemoveSmallPartsAlgorithm() : PlaygroundAlgorithm("removeSmallParts", "RemoveSmallParts", "Konstruktion",
    [In.A(In.Any)], [new("minSize", "Mindestgröße (Fläche bzw. Länge)", "number", 10.0)],
    (i, p, f, _) => i["a"].RemoveSmallParts(p.Number("minSize"), f));

public sealed class SnapToGridAlgorithm() : PlaygroundAlgorithm("snapToGrid", "SnapToGrid", "Konstruktion",
    [In.A(In.Any)], [new("gridSize", "Gitterweite", "number", 1.0, "kbs")],
    (i, p, f, _) => i["a"].SnapToGrid(p.Number("gridSize"), f));

public sealed class ReducePrecisionAlgorithm() : PlaygroundAlgorithm("reducePrecision", "ReducePrecision", "Konstruktion",
    [In.A(In.Any)], [new("gridSize", "Gitterweite", "number", 1.0, "kbs")],
    (i, p, f, _) => i["a"].ReducePrecision(p.Number("gridSize"), f));

// ---------- Lineare Referenzierung nach M (ADR 0015/0016): M kommt über WKT in den Playground ----------

public sealed class InterpolateByMAlgorithm() : PlaygroundAlgorithm("interpolateByM", "InterpolateByM", "Lineare Referenzierung",
    [In.A(In.Lines, "Linie (WKT mit M)")], [new("m", "M-Wert", "number", 150.0)],
    (i, p, _, _) => ((IPolyline2D)i["a"]).InterpolateByM(p.Number("m")));

public sealed class ProjectToMAlgorithm() : PlaygroundAlgorithm("projectToM", "ProjectToM", "Lineare Referenzierung",
    [In.A(In.Lines, "Linie (WKT mit M)"), In.B(["Point"], "Punkt")], [],
    (i, _, _, _) => ((IPolyline2D)i["a"]).ProjectToM((IPoint2D)i["b"]));

public sealed class SubstringByMAlgorithm() : PlaygroundAlgorithm("substringByM", "SubstringByM", "Lineare Referenzierung",
    [In.A(In.Lines, "Linie (WKT mit M)")],
    [new("fromM", "Von (M)", "number", 50.0), new("toM", "Bis (M)", "number", 200.0)],
    (i, p, f, _) => ((IPolyline2D)i["a"]).SubstringByM(p.Number("fromM"), p.Number("toM"), f));

// ---------- Delaunay-Triangulierung und konkave Hülle (ADR 0029) ----------

public sealed class DelaunayTrianglesAlgorithm() : PlaygroundAlgorithm("delaunayTriangles", "DelaunayTriangles", "Konstruktion",
    [In.A(In.Any)], [],
    (i, _, f, _) => i["a"].DelaunayTriangles(f).Cast<IGeometry2D>().ToList());

public sealed class ConcaveHullAlgorithm() : PlaygroundAlgorithm("concaveHull", "ConcaveHull", "Konstruktion",
    [In.A(In.Any)],
    [new("maxEdgeLength", "Längste Randkante", "number", 20.0, "kbs"), new("holesAllowed", "Löcher erlaubt", "bool", false)],
    (i, p, f, _) => i["a"].ConcaveHull(p.Number("maxEdgeLength"), p.Bool("holesAllowed"), f));

public sealed class ConcaveHullByRatioAlgorithm() : PlaygroundAlgorithm("concaveHullByRatio", "ConcaveHullByRatio", "Konstruktion",
    [In.A(In.Any)],
    [new("edgeLengthRatio", "Kantenlängen-Verhältnis (0–1)", "number", 0.3), new("holesAllowed", "Löcher erlaubt", "bool", false)],
    (i, p, f, _) => i["a"].ConcaveHullByRatio(p.Number("edgeLengthRatio"), p.Bool("holesAllowed"), f));
