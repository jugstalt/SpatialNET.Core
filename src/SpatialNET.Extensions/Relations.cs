using SpatialNET.Internal;
using SpatialNET.Internal.Topology;

namespace SpatialNET;

/// <summary>
/// The DE-9IM intersection matrix: dimension of the intersection of interior (I), boundary (B) and exterior (E) of two geometries;
/// −1 means empty ("F").
/// </summary>
public readonly struct IntersectionMatrix : IEquatable<IntersectionMatrix>
{
    private readonly int[] _values;

    internal IntersectionMatrix(int[] values) => _values = values;

    /// <summary>Interior(a) ∩ Interior(b).</summary>
    public int II => _values[0];
    /// <summary>Interior(a) ∩ Boundary(b).</summary>
    public int IB => _values[1];
    /// <summary>Interior(a) ∩ Exterior(b).</summary>
    public int IE => _values[2];
    /// <summary>Boundary(a) ∩ Interior(b).</summary>
    public int BI => _values[3];
    /// <summary>Boundary(a) ∩ Boundary(b).</summary>
    public int BB => _values[4];
    /// <summary>Boundary(a) ∩ Exterior(b).</summary>
    public int BE => _values[5];
    /// <summary>Exterior(a) ∩ Interior(b).</summary>
    public int EI => _values[6];
    /// <summary>Exterior(a) ∩ Boundary(b).</summary>
    public int EB => _values[7];
    /// <summary>Exterior(a) ∩ Exterior(b).</summary>
    public int EE => _values[8];

    /// <summary>
    /// Checks against a DE-9IM pattern of 9 characters: <c>T</c> (not empty), <c>F</c> (empty), <c>*</c> (any),
    /// <c>0</c>/<c>1</c>/<c>2</c> (exactly this dimension).
    /// </summary>
    /// <param name="pattern">The pattern, e.g. <c>T*F**F***</c>.</param>
    /// <exception cref="ArgumentException">The pattern does not consist of 9 valid characters.</exception>
    public bool Matches(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.Length != 9)
        {
            throw new ArgumentException("A DE-9IM pattern has exactly 9 characters.", nameof(pattern));
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
                _ => throw new ArgumentException($"Invalid character '{pattern[i]}' in DE-9IM pattern.", nameof(pattern)),
            };
            if (!ok) return false;
        }

        return true;
    }

    /// <summary>The matrix as 9 characters (e.g. <c>212101212</c>), row by row I, B, E.</summary>
    public override string ToString() => string.Concat(_values.Select(v => v < 0 ? "F" : v.ToString()));

    /// <inheritdoc />
    public bool Equals(IntersectionMatrix other) => _values.AsSpan().SequenceEqual(other._values);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is IntersectionMatrix m && Equals(m);

    /// <inheritdoc />
    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);

    /// <summary>Equality.</summary>
    public static bool operator ==(IntersectionMatrix a, IntersectionMatrix b) => a.Equals(b);

    /// <summary>Inequality.</summary>
    public static bool operator !=(IntersectionMatrix a, IntersectionMatrix b) => !a.Equals(b);
}

/// <summary>
/// Spatial predicates based on DE-9IM (ADR 0013): exact on the input coordinates, without a grid. Areas by
/// even-odd (ADR 0003), line boundaries by mod-2. If a geometry is empty, only <see cref="Disjoint"/> is true.
/// Invalid input is handled deterministically and without exceptions, but without any guarantee of mutual consistency.
/// </summary>
public static class Relations
{
    /// <summary>The DE-9IM intersection matrix of <paramref name="a"/> and <paramref name="b"/>.</summary>
    /// <param name="a">The first geometry.</param>
    /// <param name="b">The second geometry.</param>
    /// <exception cref="ArgumentNullException">A geometry is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">A geometry is not a known geometry kind.</exception>
    public static IntersectionMatrix Relate(this IGeometry2D a, IGeometry2D b)
    {
        GeometryAccess.EnsureKnown(a, nameof(a));
        GeometryAccess.EnsureKnown(b, nameof(b));
        return new IntersectionMatrix(RelateComputer.Compute(a, b));
    }

    /// <summary>Whether the intersection matrix matches the DE-9IM pattern.</summary>
    /// <param name="a">The first geometry.</param>
    /// <param name="b">The second geometry.</param>
    /// <param name="pattern">The pattern of 9 characters (<c>T F * 0 1 2</c>).</param>
    public static bool Relate(this IGeometry2D a, IGeometry2D b, string pattern) => a.Relate(b).Matches(pattern);

    /// <summary>Whether the geometries meet in at least one point.</summary>
    public static bool Intersects(this IGeometry2D a, IGeometry2D b) => !a.Disjoint(b);

    /// <summary>Whether the geometries share no point (<c>FF*FF****</c>); true if one is empty.</summary>
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

    /// <summary>Whether <paramref name="b"/> lies in <paramref name="a"/> and they share an interior (<c>T*****FF*</c>).</summary>
    public static bool Contains(this IGeometry2D a, IGeometry2D b) => NonEmpty(a, b) && a.Relate(b).Matches("T*****FF*");

    /// <summary>Whether <paramref name="a"/> lies in <paramref name="b"/> (<c>T*F**F***</c>).</summary>
    public static bool Within(this IGeometry2D a, IGeometry2D b) => NonEmpty(a, b) && a.Relate(b).Matches("T*F**F***");

    /// <summary>Whether no point of <paramref name="b"/> lies outside <paramref name="a"/>; boundary points count.</summary>
    public static bool Covers(this IGeometry2D a, IGeometry2D b)
    {
        if (!NonEmpty(a, b)) return false;
        var m = a.Relate(b);
        return m.EI < 0 && m.EB < 0 && (m.II >= 0 || m.IB >= 0 || m.BI >= 0 || m.BB >= 0);
    }

    /// <summary>Whether no point of <paramref name="a"/> lies outside <paramref name="b"/>; boundary points count.</summary>
    public static bool CoveredBy(this IGeometry2D a, IGeometry2D b) => b.Covers(a);

    /// <summary>Whether the geometries meet only in boundary points.</summary>
    public static bool Touches(this IGeometry2D a, IGeometry2D b)
    {
        if (!NonEmpty(a, b)) return false;
        var m = a.Relate(b);
        return m.II < 0 && (m.IB >= 0 || m.BI >= 0 || m.BB >= 0);
    }

    /// <summary>Whether the geometries "cross" (OGC; depends on the dimensions).</summary>
    public static bool Crosses(this IGeometry2D a, IGeometry2D b)
    {
        if (!NonEmpty(a, b)) return false;
        int da = RelateComputer.DimensionOf(a), db = RelateComputer.DimensionOf(b);
        var m = a.Relate(b);
        if (da < db) return m.Matches("T*T******");
        if (da > db) return m.Matches("T*****T**");
        return da == 1 && m.Matches("0********");
    }

    /// <summary>Whether geometries of equal dimension partly overlap.</summary>
    public static bool Overlaps(this IGeometry2D a, IGeometry2D b)
    {
        if (!NonEmpty(a, b)) return false;
        int da = RelateComputer.DimensionOf(a), db = RelateComputer.DimensionOf(b);
        if (da != db) return false;
        return a.Relate(b).Matches(da == 1 ? "1*T***T**" : "T*T***T**");
    }

    /// <summary>Whether the geometries are topologically equal (<c>T*F**FFF*</c>); false if one is empty.</summary>
    public static bool EqualsTopo(this IGeometry2D a, IGeometry2D b) => NonEmpty(a, b) && a.Relate(b).Matches("T*F**FFF*");

    private static bool NonEmpty(IGeometry2D a, IGeometry2D b)
    {
        GeometryAccess.EnsureKnown(a, nameof(a));
        GeometryAccess.EnsureKnown(b, nameof(b));
        return !GeometryAccess.IsEmpty(a) && !GeometryAccess.IsEmpty(b);
    }
}
