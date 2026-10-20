using System.Reflection;
using System.Runtime.CompilerServices;

namespace SpatialNET.Tests.Fixtures;

/// <summary>
/// Erzwingt ADR 0008: jede öffentliche Methode von SpatialNET.Extensions hat Fixtures (oder einen
/// <see cref="CoversAttribute"/>-Test), und die Fixtures jedes Algorithmus decken den Pflicht-Tag-Katalog ab.
/// </summary>
public class FixtureCompletenessTests
{
    private static readonly Assembly Extensions = typeof(SpatialNET.Measures).Assembly;

    public static TheoryData<string> PublicMethods()
    {
        var data = new TheoryData<string>();
        foreach (var name in Extensions.GetExportedTypes()
                     .Where(t => t is { IsAbstract: true, IsSealed: true }) // statische Klassen
                     .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                     .Where(m => !m.IsSpecialName)
                     .Select(m => m.Name)
                     .Distinct()
                     .Order())
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PublicMethods))]
    public void Every_public_method_has_fixtures_or_a_covering_test(string method)
    {
        bool hasFixtures = FixtureCatalog.For(Algorithm(method)).Any();
        bool hasCoversTest = CoveringTests().Contains(method);

        Assert.True(hasFixtures || hasCoversTest,
            $"{method}: weder Fixtures unter samples/{Algorithm(method)}/ noch ein Test mit [Covers(\"{method}\")].");
    }

    [Theory]
    [MemberData(nameof(PublicMethods))]
    public void Fixtures_cover_the_required_tag_catalog(string method)
    {
        var algorithm = Algorithm(method);
        var fixtures = FixtureCatalog.For(algorithm).ToList();
        if (fixtures.Count == 0)
        {
            return; // durch [Covers] abgedeckt, z. B. Hilfsmethoden ohne Geometrie-Ergebnis
        }

        var notApplicable = FixtureCatalog.NotApplicable(algorithm);
        var missing = FixtureCatalog.RequiredTags
            .Where(tag => !notApplicable.ContainsKey(tag) && !fixtures.Any(f => f.Tags.Contains(tag)))
            .ToList();

        Assert.True(missing.Count == 0,
            $"samples/{algorithm}/: fehlende Tags {string.Join(", ", missing)} (oder in _catalog.json begründet ausnehmen).");
    }

    [Fact]
    public void All_fixtures_are_readable_by_SpatialNET_and_NTS()
    {
        foreach (var fixture in FixtureCatalog.Fixtures)
        {
            foreach (var wkt in new[] { fixture.A, fixture.B }.OfType<string>())
            {
                SpatialNET.IO.Wkt.ReadAll(wkt, Imp.ImpGeometryFactory.Instance);
                Oracle.Nts.Read(wkt);
            }
        }
    }

    private static string Algorithm(string method) => char.ToLowerInvariant(method[0]) + method[1..];

    private static HashSet<string> CoveringTests() =>
        typeof(FixtureCompletenessTests).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<CoversAttribute>()
                .Concat(t.GetMethods().SelectMany(m => m.GetCustomAttributes<CoversAttribute>())))
            .Select(a => a.Method)
            .ToHashSet();
}
