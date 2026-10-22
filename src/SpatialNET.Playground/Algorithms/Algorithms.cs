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

public abstract class PredicateAlgorithm(string id, string name, Func<IGeometry2D, IGeometry2D, bool> predicate)
    : PlaygroundAlgorithm(id, name, "Prädikate", [In.A(In.Any, "A"), In.B(In.Any)], [], (i, _, _, _) => predicate(i["a"], i["b"]));

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
