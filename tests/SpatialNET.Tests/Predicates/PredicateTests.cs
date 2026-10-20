using NetTopologySuite.Geometries;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Predicates;

/// <summary>Prädikate und DE-9IM exakt gegen NTS (ADR 0008/0013), je Fixture für Imp und Fremd-Implementierung.</summary>
public class PredicateFixtureTests
{
    private static readonly Dictionary<string, (Func<IGeometry2D, IGeometry2D, bool> Ours, Func<Geometry, Geometry, bool> Nts)> Predicates = new()
    {
        ["intersects"] = ((a, b) => a.Intersects(b), (a, b) => a.Intersects(b)),
        ["disjoint"] = ((a, b) => a.Disjoint(b), (a, b) => a.Disjoint(b)),
        ["contains"] = ((a, b) => a.Contains(b), (a, b) => a.Contains(b)),
        ["within"] = ((a, b) => a.Within(b), (a, b) => a.Within(b)),
        ["touches"] = ((a, b) => a.Touches(b), (a, b) => a.Touches(b)),
        ["covers"] = ((a, b) => a.Covers(b), (a, b) => a.Covers(b)),
        ["coveredBy"] = ((a, b) => a.CoveredBy(b), (a, b) => a.CoveredBy(b)),
        ["crosses"] = ((a, b) => a.Crosses(b), (a, b) => a.Crosses(b)),
        ["overlaps"] = ((a, b) => a.Overlaps(b), (a, b) => a.Overlaps(b)),
        ["equalsTopo"] = ((a, b) => a.EqualsTopo(b), (a, b) => a.EqualsTopologically(b)),
    };

    public static TheoryData<string, string> RelateFixtures => FixtureCatalog.Theory("relate");

    public static TheoryData<string, string> PredicateFixtures()
    {
        var data = new TheoryData<string, string>();
        foreach (var name in Predicates.Keys)
            foreach (var row in FixtureCatalog.Theory(name))
                data.Add((string)row[0], (string)row[1]);
        return data;
    }

    [Theory]
    [MemberData(nameof(RelateFixtures))]
    public void Relate(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var (a, b) = (fixture.ReadA(factory), fixture.ReadB(factory));

        if (fixture.Tags.Contains("self-crossing"))
        {
            Assert.Equal(a.Relate(b), a.Relate(b)); // ungültige Eingabe: nur deterministisch, ohne Exception
            return;
        }

        var expected = fixture.Expected ?? Nts.Read(fixture.A).Relate(Nts.Read(fixture.B!)).ToString();
        Assert.Equal(expected, a.Relate(b).ToString());
        Assert.Equal(Transpose(expected), b.Relate(a).ToString());
    }

    [Theory]
    [MemberData(nameof(PredicateFixtures))]
    public void Predicate(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var (ours, nts) = Predicates[fixture.Algorithm];
        var (a, b) = (fixture.ReadA(factory), fixture.ReadB(factory));
        var (na, nb) = (Nts.Read(fixture.A), Nts.Read(fixture.B!));

        bool actual = ours(a, b);
        if (fixture.Tags.Contains("self-crossing"))
        {
            Assert.Equal(actual, ours(a, b));
            return;
        }

        // Leer (ADR 0013): nur Disjoint ist wahr – NTS weicht bei EqualsTopo(leer, leer) ab.
        bool expected = fixture.Expected is not null ? bool.Parse(fixture.Expected)
            : na.IsEmpty || nb.IsEmpty ? fixture.Algorithm == "disjoint"
            : nts(na, nb);
        Assert.Equal(expected, actual);
    }

    private static string Transpose(string m) => new([m[0], m[3], m[6], m[1], m[4], m[7], m[2], m[5], m[8]]);
}

public class IntersectionMatrixTests
{
    [Fact]
    public void Matches_patterns()
    {
        var factory = Imp.ImpGeometryFactory.Instance;
        var a = SpatialNET.IO.Wkt.Read("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))", factory);
        var b = SpatialNET.IO.Wkt.Read("POLYGON ((5 5, 15 5, 15 15, 5 15, 5 5))", factory);

        var m = a.Relate(b);

        Assert.Equal("212101212", m.ToString());
        Assert.True(m.Matches("T*T***T**"));
        Assert.True(a.Relate(b, "2********"));
        Assert.Throws<ArgumentException>(() => m.Matches("T*T"));
        Assert.Throws<ArgumentException>(() => m.Matches("X********"));
    }

    [Fact]
    public void Predicates_are_exact_without_grid()
    {
        // Abstand 1e-13: mit Overlay-Gitter (1e-12) zusammengefallen, exakt aber getrennt (ADR 0005).
        var factory = Imp.ImpGeometryFactory.Instance;
        var a = SpatialNET.IO.Wkt.Read("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", factory);
        var b = SpatialNET.IO.Wkt.Read("POLYGON ((1.0000000000001 0, 2 0, 2 1, 1.0000000000001 1, 1.0000000000001 0))", factory);

        Assert.False(a.Intersects(b));
    }

    [Fact]
    public void Empty_is_only_disjoint()
    {
        var factory = Imp.ImpGeometryFactory.Instance;
        var empty = SpatialNET.IO.Wkt.Read("POLYGON EMPTY", factory);

        Assert.True(empty.Disjoint(empty));
        Assert.False(empty.EqualsTopo(empty));
        Assert.False(empty.Intersects(empty));
    }
}
