using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using SpatialNET.Internal.Noding;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Overlay;

/// <summary>
/// Overlay gegen NTS OverlayNG im Strict-Mode mit gleicher Gitterweite (ADR 0005/0008). Ungültige Eingaben
/// (Tag self-crossing) haben kein NTS-Soll; dort wird nur ein gültiges Ergebnis gefordert.
/// </summary>
public class OverlayFixtureTests
{
    public static TheoryData<string, string> Fixtures(string algorithm) => FixtureCatalog.Theory(algorithm);

    public static TheoryData<string, string> IntersectionFixtures => Fixtures("intersection");
    public static TheoryData<string, string> UnionFixtures => Fixtures("union");
    public static TheoryData<string, string> DifferenceFixtures => Fixtures("difference");
    public static TheoryData<string, string> SymDifferenceFixtures => Fixtures("symDifference");

    [Theory, MemberData(nameof(IntersectionFixtures))]
    public void Intersection(string id, string implementation) => Check(id, implementation, SpatialFunction.Intersection);

    [Theory, MemberData(nameof(UnionFixtures))]
    public void Union(string id, string implementation) => Check(id, implementation, SpatialFunction.Union);

    [Theory, MemberData(nameof(DifferenceFixtures))]
    public void Difference(string id, string implementation) => Check(id, implementation, SpatialFunction.Difference);

    [Theory, MemberData(nameof(SymDifferenceFixtures))]
    public void SymDifference(string id, string implementation) => Check(id, implementation, SpatialFunction.SymDifference);

    internal static void Check(string id, string implementation, SpatialFunction op)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var (a, b) = (fixture.ReadA(factory), fixture.ReadB(factory));
        var (na, nb) = (Nts.Read(fixture.A), Nts.Read(fixture.B!));
        double gridSize = fixture.Params.ContainsKey("gridSize")
            ? fixture.Param("gridSize", 0)
            : GridModel.AutoSize(MaxAbs(na, nb));

        // ADR 0012: Union/SymDifference gemischter Dimensionen (beide nicht leer) sind nicht abbildbar.
        if (op is SpatialFunction.Union or SpatialFunction.SymDifference && !na.IsEmpty && !nb.IsEmpty
            && Dim(na) != Dim(nb))
        {
            Assert.Throws<NotSupportedException>(() => op == SpatialFunction.Union
                ? a.Union(b, factory: factory, gridSize: gridSize)
                : a.SymDifference(b, factory: factory, gridSize: gridSize));
            return;
        }

        IGeometry2D actual = op switch
        {
            SpatialFunction.Intersection => a.Intersection(b, factory: factory, gridSize: gridSize),
            SpatialFunction.Union => a.Union(b, factory: factory, gridSize: gridSize),
            SpatialFunction.Difference => a.Difference(b, factory: factory, gridSize: gridSize),
            _ => a.SymDifference(b, factory: factory, gridSize: gridSize),
        };

        if (fixture.Tags.Contains("self-crossing"))
        {
            Assert.True(actual.IsValid(), "Ergebnis eines Overlays muss gültig sein (ADR 0003).");
            return;
        }

        // Flächen in SpatialNET-Semantik (Even-Odd, degenerierte Ringe ignoriert, ADR 0003) an NTS übergeben.
        var (sa, sb) = (Nts.ToNts(a), Nts.ToNts(b));
        Geometry expected;
        try
        {
            expected = new OverlayNG(sa, sb, new PrecisionModel(1 / gridSize), op) { StrictMode = true }.GetResult();
        }
        catch (TopologyException)
        {
            // NTS scheitert an der Eingabe – ohne Orakel wird nur ein gültiges Ergebnis gefordert.
            Assert.True(actual.IsValid(), "Ergebnis eines Overlays muss gültig sein (ADR 0003).");
            return;
        }

        // ADR 0012: Polylinie ∩ Polylinie bzw. ∩ Fläche liefert Linien; reine Berührungs-/Kreuzungspunkte fallen weg.
        if (op == SpatialFunction.Intersection && !expected.IsEmpty
            && (int)expected.Dimension < Math.Min(Dim(sa), Dim(sb)))
        {
            expected = expected.Factory.CreateGeometryCollection();
        }

        OracleAssert.SameGeometry(expected, actual, gridSize, fixture);
    }

    private static int Dim(Geometry g) => g is IPolygonal ? 2 : g is ILineal ? 1 : 0;

    private static double MaxAbs(params Geometry[] geometries) =>
        geometries.Where(g => !g.IsEmpty).Select(g => g.EnvelopeInternal)
            .Select(e => Math.Max(Math.Max(Math.Abs(e.MinX), Math.Abs(e.MaxX)), Math.Max(Math.Abs(e.MinY), Math.Abs(e.MaxY))))
            .DefaultIfEmpty(0).Max();
}
