using NetTopologySuite.Algorithm.Distance;
using NetTopologySuite.Geometries;
using SpatialNET.Tests.Fixtures;

namespace SpatialNET.Tests.Oracle;

/// <summary>
/// Vergleich mit dem Orakel und zentrale Toleranzen (ADR 0008). Verglichen wird nie über
/// Koordinatengleichheit, sondern über Maße, symmetrische Differenz, Hausdorff-Distanz,
/// Topologie-Kennzahlen und Validität.
/// </summary>
public static class OracleAssert
{
    public const double Relative = 1e-9;
    public const double BufferArea = 1e-6;

    /// <summary>Relativer Vergleich mit absolutem Mindestmaß 1 (für Werte nahe 0).</summary>
    public static void Close(double expected, double actual, double relative = Relative)
    {
        if (double.IsNaN(expected))
        {
            Assert.True(double.IsNaN(actual), $"Erwartet NaN, erhalten {actual:R}.");
            return;
        }

        double tolerance = relative * Math.Max(1, Math.Abs(expected));
        Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"Erwartet {expected:R}, erhalten {actual:R} (Toleranz {tolerance:R}).");
    }

    public static void Close(double expected, double actual, Fixture fixture, string kind, double relative = Relative) =>
        Close(expected, actual, fixture.ToleranceFor(kind, relative));

    /// <summary>
    /// Vergleicht ein Ergebnis mit dem Orakel-Soll: gleiche Dimension bzw. beide leer, gültig,
    /// gleiche Kennzahlen, symmetrische Differenz und Hausdorff-Distanz innerhalb der Toleranz.
    /// </summary>
    /// <param name="gridSize">Gitterweite der Rechnung; bestimmt die Hausdorff-Toleranz 2·g·√2.</param>
    public static void SameGeometry(Geometry expected, IGeometry2D actual, double gridSize, Fixture? fixture = null)
    {
        var result = Nts.ToNts(actual);

        if (expected.IsEmpty || result.IsEmpty)
        {
            Assert.True(expected.IsEmpty && result.IsEmpty,
                $"Erwartet {(expected.IsEmpty ? "leer" : expected.AsText())}, erhalten {(result.IsEmpty ? "leer" : result.AsText())}.");
            return;
        }

        // Ist schon das Orakel-Ergebnis ungültig (z. B. Linie ohne Länge), wird die Gültigkeit nicht gefordert.
        Assert.True(result.IsValid || !expected.IsValid, $"Ergebnis ist ungültig: {result.AsText()}\nSoll: {expected.AsText()}");
        Assert.Equal(expected.Dimension, result.Dimension);
        Assert.Equal(Parts(expected), Parts(result));

        double relative = fixture?.ToleranceFor("symdiff", Relative) ?? Relative;
        if (expected.Dimension == Dimension.Surface)
        {
            double symDiff = expected.SymmetricDifference(result).Area;
            Assert.True(symDiff <= relative * Math.Max(expected.Area, 1),
                $"Symmetrische Differenz {symDiff:R} zu groß.\nSoll: {expected.AsText()}\nIst:  {result.AsText()}");
        }
        else if (expected.Dimension == Dimension.Curve)
        {
            // Linien: Länge relativ und Hausdorff – eine symmetrische Differenz wäre bei Abweichungen im ULP-Bereich die ganze Länge
            Assert.True(Math.Abs(expected.Length - result.Length) <= relative * Math.Max(expected.Length, 1),
                $"Länge {result.Length:R} statt {expected.Length:R}.\nSoll: {expected.AsText()}\nIst:  {result.AsText()}");
        }

        double hausdorff = DiscreteHausdorffDistance.Distance(expected, result);
        double maxHausdorff = fixture?.ToleranceFor("hausdorff", 2 * gridSize * Math.Sqrt(2)) ?? 2 * gridSize * Math.Sqrt(2);
        Assert.True(hausdorff <= maxHausdorff * (1 + 1e-9),
            $"Hausdorff-Distanz {hausdorff:R} > {maxHausdorff:R}.\nSoll: {expected.AsText()}\nIst:  {result.AsText()}");
    }

    /// <summary>Topologie-Kennzahlen: (Teile, Löcher bzw. Pfade).</summary>
    private static (int Parts, int Holes) Parts(Geometry g) => g switch
    {
        Polygon p => (1, p.NumInteriorRings),
        MultiPolygon mp => (mp.NumGeometries, mp.Geometries.Cast<Polygon>().Sum(p => p.NumInteriorRings)),
        _ => (g.NumGeometries, 0),
    };
}
