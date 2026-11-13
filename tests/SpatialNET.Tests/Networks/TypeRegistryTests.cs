using SpatialNET.Network;

namespace SpatialNET.Tests.Networks;

// Typen als Facetten und Typmuster (ADR 0049), mit den Typen aus dem Konzept.
public class TypeRegistryTests
{
    internal static readonly Facet[] Facets =
    [
        new("Ebene", new Dictionary<int, string> { [1] = "Niederspannung", [2] = "Mittelspannung" }),
        new("Objektart", new Dictionary<int, string>
        {
            [1] = "Leitung", [2] = "Kastenverbinder", [3] = "Anlagenverbinder", [4] = "Kasten", [5] = "Trafo", [6] = "Kunde", [7] = "Schalter",
        }),
        new("Bauart", new Dictionary<int, string> { [1] = "Kabel", [2] = "Freileitung" }),
    ];

    internal static readonly NetworkType[] Types =
    [
        new(11, ElementKind.Edge, [1, 1, 1]),
        new(12, ElementKind.Edge, [1, 1, 2]),
        new(13, ElementKind.Edge, [1, 2]),
        new(14, ElementKind.Edge, [1, 3, 0]),
        new(15, ElementKind.Node, [1, 4]),
        new(16, ElementKind.Node, [1, 6]),
        new(17, ElementKind.Node, [1, 7]),
        new(21, ElementKind.Edge, [2, 1, 1]),
        new(22, ElementKind.Edge, [2, 1, 2]),
        new(23, ElementKind.Edge, [2, 2]),
        new(24, ElementKind.Edge, [2, 3]),
        new(25, ElementKind.Node, [2, 5], "MS-Trafo"),
        new(26, ElementKind.Node, [2, 7]),
    ];

    private static readonly TypeRegistry Registry = new(Facets, Types);

    [Theory]
    [InlineData("[*, Kastenverbinder, *]", new[] { 13, 23 })]
    [InlineData("[*, 2, *]", new[] { 13, 23 })]
    [InlineData("*.Kastenverbinder", new[] { 13, 23 })]
    [InlineData("Niederspannung.*", new[] { 11, 12, 13, 14 })]
    [InlineData("niederspannung.leitung.*", new[] { 11, 12 })]
    [InlineData("[*, Leitung, Kabel]", new[] { 11, 21 })]
    [InlineData("Niederspannung.Kastenverbinder", new[] { 13 })]
    [InlineData("[1, 2, 0]", new[] { 13 })]
    [InlineData("*", new[] { 11, 12, 13, 14, 21, 22, 23, 24 })]
    [InlineData("[*, Kastenverbinder, *] | [*, Anlagenverbinder, *]", new[] { 13, 14, 23, 24 })]
    public void Edge_patterns_resolve_to_type_ids(string pattern, int[] expected)
    {
        Assert.Equal(expected, Registry.Edges(pattern).TypeIds);
    }

    [Fact]
    public void Node_patterns_and_the_untyped_helper_nodes()
    {
        Assert.Equal([15], Registry.Nodes("[*, Kasten, *]").TypeIds);
        Assert.Equal([15, 25], Registry.Nodes("[*, Kasten, *]", "[*, Trafo, *]").TypeIds);
        Assert.Equal([15, 16, 17, 25, 26], Registry.Nodes("*").TypeIds);
        Assert.False(Registry.Nodes("*").Contains(0));
        Assert.Equal([0], Registry.Nodes("untyped").TypeIds);
        Assert.Equal([0, 17, 26], Registry.Nodes("UNTYPED | [*, Schalter, *]").TypeIds);
        Assert.Equal(Registry.Nodes("*").TypeIds, Registry.AllNodes.TypeIds);
        Assert.Equal(Registry.Edges("*").TypeIds, Registry.AllEdges.TypeIds);
    }

    [Theory]
    [InlineData("Niederspannung.Leitung")]       // Leitungen haben eine Bauart, "nicht belegt" trifft keine
    [InlineData("[*, Kasten, *]")]               // ein Knotentyp
    [InlineData("[*, Unbekannt, *]")]
    [InlineData("[*, Leitung]")]
    [InlineData("[*, 9, *]")]
    [InlineData("Niederspannung.Leitung.Kabel.Rot")]
    [InlineData("[*, Leitung, *")]
    [InlineData("untyped")]
    [InlineData("[*, Leitung, *] | ")]
    public void Edge_patterns_that_are_errors(string pattern)
    {
        Assert.Throws<ArgumentException>(() => Registry.Edges(pattern));
    }

    [Fact]
    public void At_least_one_pattern_is_needed()
    {
        Assert.Throws<ArgumentException>(() => Registry.Nodes());
        Assert.Throws<ArgumentNullException>(() => Registry.Nodes(null!));
    }

    [Fact]
    public void Display_joins_the_value_names()
    {
        Assert.Equal("Niederspannung.Leitung.Kabel", Registry.Display(11));
        Assert.Equal("Niederspannung.Kasten", Registry.Display(15));
        Assert.Equal("MS-Trafo", Registry.Display(25));
        Assert.Equal("untyped", Registry.Display(0));
        Assert.Equal("999", Registry.Display(999));
        Assert.Equal(ElementKind.Node, Registry[15].Kind);
        Assert.Throws<KeyNotFoundException>(() => Registry[999]);
        Assert.False(Registry.TryGetType(999, out _));
    }

    public static TheoryData<Facet[], NetworkType[]> InvalidTables => new()
    {
        { [], Types },
        { [new("A", new Dictionary<int, string> { [0] = "x" })], [] },
        { [new("A", new Dictionary<int, string> { [1] = "x", [2] = "X" })], [] },
        { [new("A", new Dictionary<int, string> { [1] = "a.b" })], [] },
        { [new("A", new Dictionary<int, string> { [1] = "untyped" })], [] },
        { [new("", new Dictionary<int, string> { [1] = "x" })], [] },
        { Facets, [new(0, ElementKind.Node, [1, 4])] },
        { Facets, [new(1, ElementKind.Node, [1, 9])] },
        { Facets, [new(1, ElementKind.Node, [1, 4, 1, 1])] },
        { Facets, [new(1, ElementKind.Node, [0, 0])] },
        { Facets, [new(1, ElementKind.Node, [1, 4]), new(1, ElementKind.Edge, [1, 1])] },
    };

    [Theory]
    [MemberData(nameof(InvalidTables))]
    public void An_inconsistent_table_is_rejected(Facet[] facets, NetworkType[] types)
    {
        Assert.Throws<ArgumentException>(() => new TypeRegistry(facets, types));
    }

    [Fact]
    public void TypeSet_basics()
    {
        var set = TypeSet.Of(5, 3, 5);
        Assert.Equal([3, 5], set.TypeIds);
        Assert.True(set.Contains(3));
        Assert.False(set.Contains(4));
        Assert.Equal("{3, 5}", set.ToString());
        Assert.Equal([0, 3, 5], set.Union(TypeSet.Untyped).TypeIds);
        Assert.Equal(0, TypeSet.Empty.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => TypeSet.Of(-1));
    }
}
