using System.Collections.Frozen;
using System.Globalization;

namespace SpatialNET.Network;

/// <summary>
/// One property that a type is made of, with its own values, for example the voltage level (ADR 0049). The value of a
/// facet means the same under every other facet; <c>0</c> is reserved for "not set".
/// </summary>
/// <param name="Name">The name, for messages.</param>
/// <param name="Values">The values with their names; keys greater than 0.</param>
public sealed record Facet(string Name, IReadOnlyDictionary<int, string> Values);

/// <summary>A type of nodes or edges as a combination of facet values (ADR 0049).</summary>
/// <param name="Id">The id stored in the records; greater than 0 (0 is the untyped helper node).</param>
/// <param name="Kind">Whether it is a node type or an edge type.</param>
/// <param name="Facets">One value per facet, in the order of the facets; missing trailing values are 0 ("not set").</param>
/// <param name="Name">An optional display name; by default the names of the values joined with dots.</param>
public sealed record NetworkType(int Id, ElementKind Kind, IReadOnlyList<int> Facets, string? Name = null);

/// <summary>
/// A set of type ids, the result of resolving type patterns once (ADR 0049). Searches and rules compare integers only.
/// Immutable and thread-safe.
/// </summary>
public sealed class TypeSet
{
    private readonly FrozenSet<int> _set;
    private readonly int[] _sorted;

    private TypeSet(IEnumerable<int> typeIds, string? patternText = null)
    {
        _sorted = typeIds.Distinct().Order().ToArray();
        _set = _sorted.ToFrozenSet();
        PatternText = patternText;
    }

    /// <summary>
    /// The pattern text the set was resolved from by a <see cref="TypeRegistry"/>, or <c>null</c> for sets made from ids.
    /// Rules keep it so that <see cref="NetworkRule.ToDefinition"/> can give the text of a rule back (ADR 0052).
    /// </summary>
    internal string? PatternText { get; }

    /// <summary>The set with only the untyped helper nodes (type 0).</summary>
    public static TypeSet Untyped { get; } = new([0], "untyped");

    internal static TypeSet FromPattern(IEnumerable<int> typeIds, string patternText) => new(typeIds, patternText);

    /// <summary>The empty set.</summary>
    public static TypeSet Empty { get; } = new([]);

    /// <summary>A set of the given type ids, for hosts that know the ids already.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="typeIds"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An id is negative.</exception>
    public static TypeSet Of(params IEnumerable<int> typeIds)
    {
        ArgumentNullException.ThrowIfNull(typeIds);
        var ids = typeIds.ToArray();
        foreach (int id in ids)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(id, nameof(typeIds));
        }

        return new TypeSet(ids);
    }

    /// <summary>The type ids, ascending.</summary>
    public IReadOnlyList<int> TypeIds => _sorted;

    /// <summary>The number of type ids.</summary>
    public int Count => _sorted.Length;

    /// <summary>Whether the set contains the type id.</summary>
    public bool Contains(int typeId) => _set.Contains(typeId);

    /// <summary>The union with another set.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <c>null</c>.</exception>
    public TypeSet Union(TypeSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new TypeSet(_sorted.Concat(other._sorted), PatternText is { } a && other.PatternText is { } b ? a + " | " + b : null);
    }

    /// <inheritdoc/>
    public override string ToString() => "{" + string.Join(", ", _sorted) + "}";
}

/// <summary>
/// The types of a network as facet vectors, built from the host's type table (ADR 0049). Resolves type patterns into
/// <see cref="TypeSet"/>s. Immutable and thread-safe.
/// </summary>
/// <remarks>
/// <para>Patterns, several of them separated by <c>|</c> (OR):</para>
/// <list type="bullet">
/// <item><c>[Niederspannung, *, Kabel]</c>: one entry per facet, a value name, a number or <c>*</c> (any value, also "not set").</item>
/// <item><c>Niederspannung.Leitung</c>: the values from the first facet on; the facets after them must be "not set".</item>
/// <item><c>Niederspannung.*</c>: a trailing <c>*</c> means any value for all remaining facets, so it contains <c>Niederspannung</c> itself.</item>
/// <item><c>*</c>: every type of the kind, but not the untyped helper nodes; <c>untyped</c>: only the helper nodes.</item>
/// </list>
/// <para>Value names are compared ignoring case. A pattern that matches no type of the asked kind is an error.</para>
/// </remarks>
public sealed class TypeRegistry
{
    private const string UntypedKeyword = "untyped";
    private const int Any = -1;

    private readonly Facet[] _facets;
    private readonly Dictionary<string, int>[] _valuesByName;
    private readonly Dictionary<int, NetworkType> _types;
    private readonly NetworkType[] _ordered;
    private readonly int[][] _vectors; // per type in _ordered, one value per facet

    /// <summary>Builds the registry and checks the table.</summary>
    /// <param name="facets">The facets, at least one.</param>
    /// <param name="types">The types; ids greater than 0 and unique.</param>
    /// <exception cref="ArgumentNullException">An argument or an element is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The table is inconsistent; the message names the facet or the type.</exception>
    public TypeRegistry(IReadOnlyList<Facet> facets, IEnumerable<NetworkType> types)
    {
        ArgumentNullException.ThrowIfNull(facets);
        ArgumentNullException.ThrowIfNull(types);
        if (facets.Count == 0)
        {
            throw new ArgumentException("A type registry needs at least one facet.", nameof(facets));
        }

        _facets = facets.ToArray();
        _valuesByName = new Dictionary<string, int>[_facets.Length];
        for (int f = 0; f < _facets.Length; f++)
        {
            var facet = _facets[f] ?? throw new ArgumentNullException(nameof(facets), $"The facet at position {f} is null.");
            if (string.IsNullOrWhiteSpace(facet.Name))
            {
                throw new ArgumentException($"The facet at position {f} has no name.", nameof(facets));
            }

            ArgumentNullException.ThrowIfNull(facet.Values, nameof(facets));
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (value, name) in facet.Values)
            {
                if (value <= 0)
                {
                    throw new ArgumentException($"The facet '{facet.Name}' has the value {value}; values must be greater than 0 (0 means \"not set\").", nameof(facets));
                }

                if (!IsValidValueName(name))
                {
                    throw new ArgumentException($"The facet '{facet.Name}' has the value name '{name}'; a name must not be empty and must not contain [ ] , | . * or start or end with a space.", nameof(facets));
                }

                if (string.Equals(name, UntypedKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"The facet '{facet.Name}' uses the reserved value name '{UntypedKeyword}'.", nameof(facets));
                }

                if (!byName.TryAdd(name, value))
                {
                    throw new ArgumentException($"The facet '{facet.Name}' has the value name '{name}' twice (names are compared ignoring case).", nameof(facets));
                }
            }

            _valuesByName[f] = byName;
        }

        _types = [];
        foreach (var type in types)
        {
            ArgumentNullException.ThrowIfNull(type, nameof(types));
            if (type.Id <= 0)
            {
                throw new ArgumentException($"The type {type.Id} has an id that is not greater than 0 (0 is the untyped helper node).", nameof(types));
            }

            if (type.Kind is not (ElementKind.Node or ElementKind.Edge))
            {
                throw new ArgumentException($"The type {type.Id} has the unknown kind {type.Kind}.", nameof(types));
            }

            ArgumentNullException.ThrowIfNull(type.Facets, nameof(types));
            if (type.Facets.Count > _facets.Length)
            {
                throw new ArgumentException($"The type {type.Id} has {type.Facets.Count} facet values, the registry has {_facets.Length} facets.", nameof(types));
            }

            bool anySet = false;
            for (int f = 0; f < type.Facets.Count; f++)
            {
                int value = type.Facets[f];
                if (value != 0 && !_facets[f].Values.ContainsKey(value))
                {
                    throw new ArgumentException($"The type {type.Id} has the value {value} for the facet '{_facets[f].Name}', which the facet does not define.", nameof(types));
                }

                anySet |= value != 0;
            }

            if (!anySet)
            {
                throw new ArgumentException($"The type {type.Id} sets no facet.", nameof(types));
            }

            if (!_types.TryAdd(type.Id, type))
            {
                throw new ArgumentException($"The type id {type.Id} is used twice.", nameof(types));
            }
        }

        _ordered = _types.Values.OrderBy(t => t.Id).ToArray();
        _vectors = _ordered.Select(t =>
        {
            var vector = new int[_facets.Length];
            for (int f = 0; f < t.Facets.Count; f++)
            {
                vector[f] = t.Facets[f];
            }

            return vector;
        }).ToArray();

        AllNodes = TypeSet.FromPattern(_ordered.Where(t => t.Kind == ElementKind.Node).Select(t => t.Id), "*");
        AllEdges = TypeSet.FromPattern(_ordered.Where(t => t.Kind == ElementKind.Edge).Select(t => t.Id), "*");
    }

    /// <summary>The facets, in order.</summary>
    public IReadOnlyList<Facet> Facets => _facets;

    /// <summary>The types, by ascending id.</summary>
    public IReadOnlyList<NetworkType> Types => _ordered;

    /// <summary>Every node type (not the untyped helper nodes).</summary>
    public TypeSet AllNodes { get; }

    /// <summary>Every edge type.</summary>
    public TypeSet AllEdges { get; }

    /// <summary>The type with the id.</summary>
    /// <exception cref="KeyNotFoundException">There is no type with this id.</exception>
    public NetworkType this[int typeId] =>
        _types.TryGetValue(typeId, out var type) ? type : throw new KeyNotFoundException($"There is no type with the id {typeId}.");

    /// <summary>Looks up a type by id.</summary>
    public bool TryGetType(int typeId, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out NetworkType type) =>
        _types.TryGetValue(typeId, out type);

    /// <summary>
    /// A name for messages: the display name of the type, otherwise the names of its values joined with dots
    /// (<c>Niederspannung.Leitung.Kabel</c>); <c>untyped</c> for 0, the number for an unknown id.
    /// </summary>
    public string Display(int typeId)
    {
        if (typeId == 0)
        {
            return UntypedKeyword;
        }

        if (!_types.TryGetValue(typeId, out var type))
        {
            return typeId.ToString(CultureInfo.InvariantCulture);
        }

        if (type.Name is { Length: > 0 } name)
        {
            return name;
        }

        int last = type.Facets.Count - 1;
        while (last >= 0 && type.Facets[last] == 0)
        {
            last--;
        }

        return string.Join('.', Enumerable.Range(0, last + 1).Select(f =>
            type.Facets[f] == 0 ? "0" : _facets[f].Values[type.Facets[f]]));
    }

    /// <summary>The node types that match one of the patterns (OR); a pattern may hold alternatives separated by <c>|</c>.</summary>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">No pattern is given, a pattern cannot be read, or it matches no node type.</exception>
    public TypeSet Nodes(params string[] patterns) => Resolve(ElementKind.Node, patterns);

    /// <summary>The edge types that match one of the patterns (OR); a pattern may hold alternatives separated by <c>|</c>.</summary>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">No pattern is given, a pattern cannot be read, or it matches no edge type.</exception>
    public TypeSet Edges(params string[] patterns) => Resolve(ElementKind.Edge, patterns);

    private TypeSet Resolve(ElementKind kind, string[] patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        if (patterns.Length == 0)
        {
            throw new ArgumentException("At least one pattern is needed.", nameof(patterns));
        }

        var ids = new List<int>();
        foreach (var text in patterns)
        {
            ArgumentNullException.ThrowIfNull(text, nameof(patterns));
            foreach (var alternative in text.Split('|'))
            {
                ids.AddRange(ResolveOne(kind, alternative.Trim(), text));
            }
        }

        return TypeSet.FromPattern(ids, string.Join(" | ", patterns));
    }

    private IEnumerable<int> ResolveOne(ElementKind kind, string pattern, string text)
    {
        if (pattern.Length == 0)
        {
            throw new ArgumentException($"The pattern '{text}' has an empty alternative.", "patterns");
        }

        if (string.Equals(pattern, UntypedKeyword, StringComparison.OrdinalIgnoreCase))
        {
            if (kind != ElementKind.Node)
            {
                throw new ArgumentException($"The pattern '{UntypedKeyword}' only applies to nodes.", "patterns");
            }

            return [0];
        }

        var vector = pattern == "*" ? AnyVector() : pattern.StartsWith('[') ? ParseBrackets(pattern) : ParseDotted(pattern);
        var matches = new List<int>();
        for (int i = 0; i < _ordered.Length; i++)
        {
            if (_ordered[i].Kind == kind && Matches(vector, _vectors[i]))
            {
                matches.Add(_ordered[i].Id);
            }
        }

        if (matches.Count == 0)
        {
            throw new ArgumentException($"The pattern '{pattern}' matches no {(kind == ElementKind.Node ? "node" : "edge")} type.", "patterns");
        }

        return matches;
    }

    private static bool Matches(int[] pattern, int[] vector)
    {
        for (int f = 0; f < pattern.Length; f++)
        {
            if (pattern[f] != Any && pattern[f] != vector[f])
            {
                return false;
            }
        }

        return true;
    }

    private int[] AnyVector() => Enumerable.Repeat(Any, _facets.Length).ToArray();

    private int[] ParseBrackets(string pattern)
    {
        if (!pattern.EndsWith(']'))
        {
            throw new ArgumentException($"The pattern '{pattern}' starts with '[' but does not end with ']'.", "patterns");
        }

        var entries = pattern[1..^1].Split(',');
        if (entries.Length != _facets.Length)
        {
            throw new ArgumentException($"The pattern '{pattern}' has {entries.Length} entries, the registry has {_facets.Length} facets.", "patterns");
        }

        var vector = new int[_facets.Length];
        for (int f = 0; f < entries.Length; f++)
        {
            vector[f] = ParseEntry(entries[f].Trim(), f, pattern);
        }

        return vector;
    }

    private int[] ParseDotted(string pattern)
    {
        var segments = pattern.Split('.');
        bool restAny = segments[^1].Trim() == "*" && segments.Length > 1;
        int given = restAny ? segments.Length - 1 : segments.Length;
        if (given > _facets.Length)
        {
            throw new ArgumentException($"The pattern '{pattern}' has {given} values, the registry has {_facets.Length} facets.", "patterns");
        }

        var vector = new int[_facets.Length];
        for (int f = 0; f < _facets.Length; f++)
        {
            vector[f] = f < given ? ParseEntry(segments[f].Trim(), f, pattern) : restAny ? Any : 0;
        }

        return vector;
    }

    private int ParseEntry(string entry, int facet, string pattern)
    {
        if (entry == "*")
        {
            return Any;
        }

        if (int.TryParse(entry, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
        {
            if (number != 0 && !_facets[facet].Values.ContainsKey(number))
            {
                throw new ArgumentException($"The pattern '{pattern}' uses the value {number} for the facet '{_facets[facet].Name}', which the facet does not define.", "patterns");
            }

            return number;
        }

        if (_valuesByName[facet].TryGetValue(entry, out int value))
        {
            return value;
        }

        throw new ArgumentException($"The pattern '{pattern}' uses '{entry}' for the facet '{_facets[facet].Name}', which is no value of that facet.", "patterns");
    }

    private static bool IsValidValueName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Trim().Length == name.Length && name.IndexOfAny(['[', ']', ',', '|', '.', '*']) < 0;
}
