using System.Reflection;

namespace SpatialNET.Tests;

/// <summary>
/// Prüft die Paketgrenzen: Bibliotheken hängen nur voneinander ab wie spezifiziert,
/// NetTopologySuite kommt ausschließlich im Testprojekt vor (ADR 0001).
/// </summary>
public class ArchitectureTests
{
    private static readonly Dictionary<string, string[]> AllowedSpatialNetReferences = new()
    {
        ["SpatialNET.Core"] = [],
        ["SpatialNET.Extensions"] = ["SpatialNET.Core"],
        ["SpatialNET.Imp"] = ["SpatialNET.Core"],
        ["SpatialNET.IO"] = ["SpatialNET.Core", "SpatialNET.Extensions"],
    };

    public static TheoryData<string> Libraries => new(AllowedSpatialNetReferences.Keys);

    [Theory]
    [MemberData(nameof(Libraries))]
    public void Library_references_only_allowed_SpatialNET_assemblies(string library)
    {
        var references = ReferencedAssemblyNames(library)
            .Where(name => name.StartsWith("SpatialNET.", StringComparison.Ordinal));

        Assert.All(references, name => Assert.Contains(name, AllowedSpatialNetReferences[library]));
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void Library_has_no_third_party_geometry_engine(string library)
    {
        var references = ReferencedAssemblyNames(library);

        Assert.DoesNotContain(references, name =>
            name.StartsWith("NetTopologySuite", StringComparison.Ordinal) ||
            name.StartsWith("Clipper", StringComparison.Ordinal));
    }

    private static IEnumerable<string> ReferencedAssemblyNames(string library) =>
        Assembly.Load(library).GetReferencedAssemblies().Select(a => a.Name!);
}
