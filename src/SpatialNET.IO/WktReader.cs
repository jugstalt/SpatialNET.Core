using System.Globalization;

namespace SpatialNET.IO;

/// <summary>Rekursiver Parser für WKT; erzeugt Geometrien über die Factory.</summary>
internal sealed class WktReader(string text, IGeometryFactory factory)
{
    private enum Dim { Unknown, XY, Z, M, ZM }

    private int _pos;

    /// <summary>Liest genau eine Geometrie; eine GeometryCollection wird flachgeklopft, wenn <paramref name="flatten"/> gesetzt ist.</summary>
    public List<IGeometry2D> ReadDocument(bool flatten)
    {
        var result = new List<IGeometry2D>();
        ReadTagged(result, flatten, nested: false);
        SkipWhitespace();
        if (_pos < text.Length)
        {
            throw Error("Unerwarteter Text nach dem Ende der Geometrie");
        }

        return result;
    }

    private void ReadTagged(List<IGeometry2D> result, bool flatten, bool nested)
    {
        int start = _pos;
        string type = ReadWord();
        var dim = ReadDimension();

        switch (type)
        {
            case "POINT":
                if (TryEmpty()) { result.Add(factory.CreateMultiPoint([], null)); return; }
                Expect('(');
                var p = ReadCoordinate(dim);
                Expect(')');
                result.Add(factory.CreatePoint(p, null));
                return;

            case "MULTIPOINT":
                result.Add(factory.CreateMultiPoint(TryEmpty() ? [] : ReadMultiPointBody(dim), null));
                return;

            case "LINESTRING":
                result.Add(factory.CreatePolyline(TryEmpty() ? [] : [ReadSequence(dim)], null));
                return;

            case "MULTILINESTRING":
                result.Add(factory.CreatePolyline(TryEmpty() ? [] : ReadList(() => ReadSequenceOrEmpty(dim)).Where(s => s.Length > 0).Select(s => (ReadOnlyMemory<Coordinate>)s).ToArray(), null));
                return;

            case "POLYGON":
                result.Add(factory.CreatePolygon(TryEmpty() ? [] : [ReadPolygonBody(dim)], null));
                return;

            case "MULTIPOLYGON":
                result.Add(factory.CreatePolygon(TryEmpty() ? [] : ReadList(() => TryEmpty() ? (PolygonPart?)null : ReadPolygonBody(dim))
                    .OfType<PolygonPart>().ToArray(), null));
                return;

            case "GEOMETRYCOLLECTION":
                if (!flatten)
                {
                    throw new NotSupportedException(
                        "GEOMETRYCOLLECTION wird als Einzelgeometrie nicht unterstützt (ADR 0009); Wkt.ReadAll liest sie als Liste.");
                }

                if (TryEmpty()) return;
                Expect('(');
                do
                {
                    ReadTagged(result, flatten, nested: true);
                }
                while (TryConsume(','));
                Expect(')');
                return;

            default:
                _pos = start;
                throw Error($"Unbekannter Geometrie-Typ '{type}'");
        }
    }

    private Coordinate[] ReadMultiPointBody(Dim dim)
    {
        // Beide Schreibweisen: MULTIPOINT ((1 2), (3 4)) und MULTIPOINT (1 2, 3 4)
        return ReadList(() =>
        {
            if (TryConsume('('))
            {
                var c = ReadCoordinate(dim);
                Expect(')');
                return (Coordinate?)c;
            }

            return TryEmpty() ? null : ReadCoordinate(dim);
        }).OfType<Coordinate>().ToArray();
    }

    private PolygonPart ReadPolygonBody(Dim dim)
    {
        var rings = ReadList(() => ReadSequenceOrEmpty(dim)).Where(r => r.Length > 0).ToList();
        if (rings.Count == 0)
        {
            return new PolygonPart(ReadOnlyMemory<Coordinate>.Empty);
        }

        return new PolygonPart(rings[0], rings.Skip(1).Select(r => (ReadOnlyMemory<Coordinate>)r).ToArray());
    }

    private Coordinate[] ReadSequenceOrEmpty(Dim dim) => TryEmpty() ? [] : ReadSequence(dim);

    private Coordinate[] ReadSequence(Dim dim) => ReadList(() => ReadCoordinate(dim)).ToArray();

    private List<T> ReadList<T>(Func<T> item)
    {
        Expect('(');
        var list = new List<T> { item() };
        while (TryConsume(','))
        {
            list.Add(item());
        }

        Expect(')');
        return list;
    }

    private Coordinate ReadCoordinate(Dim dim)
    {
        var values = new List<double>(4);
        SkipWhitespace();
        while (_pos < text.Length && IsNumberStart(text[_pos]))
        {
            values.Add(ReadNumber());
            SkipWhitespace();
        }

        int expected = dim switch { Dim.XY => 2, Dim.Z or Dim.M => 3, Dim.ZM => 4, _ => -1 };
        if (values.Count < 2 || values.Count > 4 || (expected > 0 && values.Count != expected))
        {
            throw Error($"Koordinate mit {values.Count} Werten passt nicht zur Dimension {dim}");
        }

        // Ohne Dimensionsangabe: 3 Werte = Z, 4 Werte = ZM. M wird verworfen (ADR 0009).
        bool hasZ = dim is Dim.Z or Dim.ZM || (dim == Dim.Unknown && values.Count >= 3);
        return hasZ ? new Coordinate(values[0], values[1], values[2]) : new Coordinate(values[0], values[1]);
    }

    private double ReadNumber()
    {
        int start = _pos;
        while (_pos < text.Length && !char.IsWhiteSpace(text[_pos]) && text[_pos] is not (',' or '(' or ')'))
        {
            _pos++;
        }

        var token = text.AsSpan(start, _pos - start);
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            _pos = start;
            throw Error($"Ungültige Zahl '{token}'");
        }

        return value;
    }

    private static bool IsNumberStart(char c) => char.IsAsciiDigit(c) || c is '-' or '+' or '.' or 'N' or 'I';

    private Dim ReadDimension()
    {
        SkipWhitespace();
        int start = _pos;
        if (_pos < text.Length && char.IsAsciiLetter(text[_pos]))
        {
            switch (ReadWord())
            {
                case "Z": return Dim.Z;
                case "M": return Dim.M;
                case "ZM": return Dim.ZM;
                case "EMPTY": _pos = start; return Dim.Unknown;
                default: _pos = start; throw Error("Erwartet Z, M, ZM, EMPTY oder '('");
            }
        }

        return Dim.Unknown;
    }

    private bool TryEmpty()
    {
        SkipWhitespace();
        int start = _pos;
        if (_pos < text.Length && char.IsAsciiLetter(text[_pos]))
        {
            if (ReadWord() == "EMPTY")
            {
                return true;
            }

            _pos = start;
        }

        return false;
    }

    private string ReadWord()
    {
        SkipWhitespace();
        int start = _pos;
        while (_pos < text.Length && char.IsAsciiLetter(text[_pos]))
        {
            _pos++;
        }

        if (_pos == start)
        {
            throw Error("Erwartet ein Schlüsselwort");
        }

        return text[start.._pos].ToUpperInvariant();
    }

    private void Expect(char c)
    {
        if (!TryConsume(c))
        {
            throw Error($"Erwartet '{c}'");
        }
    }

    private bool TryConsume(char c)
    {
        SkipWhitespace();
        if (_pos < text.Length && text[_pos] == c)
        {
            _pos++;
            return true;
        }

        return false;
    }

    private void SkipWhitespace()
    {
        while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
        {
            _pos++;
        }
    }

    private FormatException Error(string message)
    {
        string found = _pos < text.Length ? $"'{text[_pos]}'" : "Ende des Textes";
        return new FormatException($"Ungültiges WKT an Position {_pos} ({found}): {message}.");
    }
}
