using SpatialNET.Internal;
using SpatialNET.Internal.Topology;

namespace SpatialNET;

/// <summary>
/// Die DE-9IM-Schnittmatrix: Dimension des Schnitts von Innerem (I), Rand (B) und Äußerem (E) zweier Geometrien;
/// −1 bedeutet leer („F“).
/// </summary>
public readonly struct IntersectionMatrix : IEquatable<IntersectionMatrix>
{
    private readonly int[] _values;

    internal IntersectionMatrix(int[] values) => _values = values;

    /// <summary>Inneres(a) ∩ Inneres(b).</summary>
    public int II => _values[0];
    /// <summary>Inneres(a) ∩ Rand(b).</summary>
    public int IB => _values[1];
    /// <summary>Inneres(a) ∩ Äußeres(b).</summary>
    public int IE => _values[2];
    /// <summary>Rand(a) ∩ Inneres(b).</summary>
    public int BI => _values[3];
    /// <summary>Rand(a) ∩ Rand(b).</summary>
    public int BB => _values[4];
    /// <summary>Rand(a) ∩ Äußeres(b).</summary>
    public int BE => _values[5];
    /// <summary>Äußeres(a) ∩ Inneres(b).</summary>
    public int EI => _values[6];
    /// <summary>Äußeres(a) ∩ Rand(b).</summary>
    public int EB => _values[7];
    /// <summary>Äußeres(a) ∩ Äußeres(b).</summary>
    public int EE => _values[8];

    /// <summary>
    /// Prüft gegen ein DE-9IM-Muster aus 9 Zeichen: <c>T</c> (nicht leer), <c>F</c> (leer), <c>*</c> (beliebig),
    /// <c>0</c>/<c>1</c>/<c>2</c> (genau diese Dimension).
    /// </summary>
    /// <param name="pattern">Das Muster, z. B. <c>T*F**F***</c>.</param>
    /// <exception cref="ArgumentException">Das Muster hat nicht 9 gültige Zeichen.</exception>
    public bool Matches(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.Length != 9)
        {
            throw new ArgumentException("Ein DE-9IM-Muster hat genau 9 Zeichen.", nameof(pattern));
        }

        for (int i = 0; i < 9; i++)
        {
            bool ok = char.ToUpperInvariant(pattern[i]) switch
            {
                '*' => true,
                'T' => _values[i] >= 0,
                'F' => _values[i] < 0,
                '0' => _values[i] == 0,
                '1' => _values[i] == 1,
                '2' => _values[i] == 2,
                _ => throw new ArgumentException($"Ungültiges Zeichen '{pattern[i]}' im DE-9IM-Muster.", nameof(pattern)),
            };
            if (!ok) return false;
        }

        return true;
    }

    /// <summary>Die Matrix als 9 Zeichen (z. B. <c>212101212</c>), zeilenweise I, B, E.</summary>
    public override string ToString() => string.Concat(_values.Select(v => v < 0 ? "F" : v.ToString()));

    /// <inheritdoc />
    public bool Equals(IntersectionMatrix other) => _values.AsSpan().SequenceEqual(other._values);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is IntersectionMatrix m && Equals(m);

    /// <inheritdoc />
    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);

    /// <summary>Gleichheit.</summary>
    public static bool operator ==(IntersectionMatrix a, IntersectionMatrix b) => a.Equals(b);

    /// <summary>Ungleichheit.</summary>
    public static bool operator !=(IntersectionMatrix a, IntersectionMatrix b) => !a.Equals(b);
}

/// <summary>
/// Räumliche Prädikate nach DE-9IM (ADR 0013): exakt auf den Eingabekoordinaten, ohne Gitter. Flächen nach
/// Even-Odd (ADR 0003), Linienränder nach Mod-2. Ist eine Geometrie leer, ist nur <see cref="Disjoint"/> wahr.
/// Bei ungültigen Eingaben deterministisch und ohne Exception, aber ohne Zusage gegenseitiger Konsistenz.
/// </summary>
public static class Relations
{
    /// <summary>Die DE-9IM-Schnittmatrix von <paramref name="a"/> und <paramref name="b"/>.</summary>
    /// <param name="a">Die erste Geometrie.</param>
    /// <param name="b">Die zweite Geometrie.</param>
    /// <exception cref="ArgumentNullException">Eine Geometrie ist <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">Eine Geometrie ist keine bekannte Geometrie-Art.</exception>
    public static IntersectionMatrix Relate(this IGeometry2D a, IGeometry2D b)
    {
        GeometryAccess.EnsureKnown(a, nameof(a));
        GeometryAccess.EnsureKnown(b, nameof(b));
        return new IntersectionMatrix(RelateComputer.Compute(a, b));
    }

    /// <summary>Ob die Schnittmatrix dem DE-9IM-Muster entspricht.</summary>
    /// <param name="a">Die erste Geometrie.</param>
    /// <param name="b">Die zweite Geometrie.</param>
    /// <param name="pattern">Das Muster aus 9 Zeichen (<c>T F * 0 1 2</c>).</param>
    public static bool Relate(this IGeometry2D a, IGeometry2D b, string pattern) => a.Relate(b).Matches(pattern);

    /// <summary>Ob sich die Geometrien in mindestens einem Punkt treffen.</summary>
    public static bool Intersects(this IGeometry2D a, IGeometry2D b) => !a.Disjoint(b);

    /// <summary>Ob die Geometrien keinen Punkt gemeinsam haben (<c>FF*FF****</c>); wahr, wenn eine leer ist.</summary>
    public static bool Disjoint(this IGeometry2D a, IGeometry2D b)
    {
        GeometryAccess.EnsureKnown(a, nameof(a));
        GeometryAccess.EnsureKnown(b, nameof(b));
        if (GeometryAccess.IsEmpty(a) || GeometryAccess.IsEmpty(b))
        {
            return true;
        }

        // Schneller exakter Weg (Segment-Schnitt, dann Enthaltensein je Teil) statt voller DE-9IM-Matrix.
        return new PreparedGeometry(a).Disjoint(b);
    }

    /// <summary>Ob <paramref name="b"/> in <paramref name="a"/> liegt und ein Inneres gemeinsam haben (<c>T*****FF*</c>).</summary>
    public static bool Contains(this IGeometry2D a, IGeometry2D b) => NonEmpty(a, b) && a.Relate(b).Matches("T*****FF*");

    /// <summary>Ob <paramref name="a"/> in <paramref name="b"/> liegt (<c>T*F**F***</c>).</summary>
    public static bool Within(this IGeometry2D a, IGeometry2D b) => NonEmpty(a, b) && a.Relate(b).Matches("T*F**F***");

    /// <summary>Ob kein Punkt von <paramref name="b"/> außerhalb von <paramref name="a"/> liegt; Randpunkte zählen mit.</summary>
    public static bool Covers(this IGeometry2D a, IGeometry2D b)
    {
        if (!NonEmpty(a, b)) return false;
        var m = a.Relate(b);
        return m.EI < 0 && m.EB < 0 && (m.II >= 0 || m.IB >= 0 || m.BI >= 0 || m.BB >= 0);
    }

    /// <summary>Ob kein Punkt von <paramref name="a"/> außerhalb von <paramref name="b"/> liegt; Randpunkte zählen mit.</summary>
    public static bool CoveredBy(this IGeometry2D a, IGeometry2D b) => b.Covers(a);

    /// <summary>Ob sich die Geometrien nur in Randpunkten treffen.</summary>
    public static bool Touches(this IGeometry2D a, IGeometry2D b)
    {
        if (!NonEmpty(a, b)) return false;
        var m = a.Relate(b);
        return m.II < 0 && (m.IB >= 0 || m.BI >= 0 || m.BB >= 0);
    }

    /// <summary>Ob sich die Geometrien „kreuzen“ (OGC; abhängig von den Dimensionen).</summary>
    public static bool Crosses(this IGeometry2D a, IGeometry2D b)
    {
        if (!NonEmpty(a, b)) return false;
        int da = RelateComputer.DimensionOf(a), db = RelateComputer.DimensionOf(b);
        var m = a.Relate(b);
        if (da < db) return m.Matches("T*T******");
        if (da > db) return m.Matches("T*****T**");
        return da == 1 && m.Matches("0********");
    }

    /// <summary>Ob sich gleichdimensionale Geometrien teilweise überlappen.</summary>
    public static bool Overlaps(this IGeometry2D a, IGeometry2D b)
    {
        if (!NonEmpty(a, b)) return false;
        int da = RelateComputer.DimensionOf(a), db = RelateComputer.DimensionOf(b);
        if (da != db) return false;
        return a.Relate(b).Matches(da == 1 ? "1*T***T**" : "T*T***T**");
    }

    /// <summary>Ob die Geometrien topologisch gleich sind (<c>T*F**FFF*</c>); falsch, wenn eine leer ist.</summary>
    public static bool EqualsTopo(this IGeometry2D a, IGeometry2D b) => NonEmpty(a, b) && a.Relate(b).Matches("T*F**FFF*");

    private static bool NonEmpty(IGeometry2D a, IGeometry2D b)
    {
        GeometryAccess.EnsureKnown(a, nameof(a));
        GeometryAccess.EnsureKnown(b, nameof(b));
        return !GeometryAccess.IsEmpty(a) && !GeometryAccess.IsEmpty(b);
    }
}
