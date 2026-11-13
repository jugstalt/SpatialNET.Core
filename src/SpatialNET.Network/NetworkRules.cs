using System.Globalization;

namespace SpatialNET.Network;

/// <summary>How serious a rule violation is, for the host to sort and to show (ADR 0052).</summary>
public enum RuleSeverity
{
    /// <summary>The data is wrong (the default).</summary>
    Error,

    /// <summary>The data is suspicious.</summary>
    Warning,
}

/// <summary>What a rule checks: nodes or edges.</summary>
public enum RuleSubject
{
    /// <summary>The rule checks nodes and counts the edges at them.</summary>
    Node,

    /// <summary>The rule checks edges and counts the other edges at their ends.</summary>
    Edge,
}

/// <summary>Which ends of an edge a rule looks at.</summary>
public enum RuleEndFilter
{
    /// <summary>Both ends.</summary>
    All,

    /// <summary>Only ends whose node has one of the end types.</summary>
    NodeIs,

    /// <summary>Only ends whose node has none of the end types.</summary>
    NodeIsNot,
}

/// <summary>
/// A rule as a row of plain values, so that the host can keep its rules in a table (ADR 0052). Resolve it with
/// <see cref="NetworkRule.FromDefinition"/>.
/// </summary>
/// <param name="Id">The id of the rule; not empty, unique within a list of rules.</param>
/// <param name="Severity">How serious a violation is.</param>
/// <param name="Subject">Whether the rule checks nodes or edges.</param>
/// <param name="SubjectTypes">Pattern text of the types that are checked; several patterns separated by <c>|</c>.</param>
/// <param name="EndFilter">For an edge rule, which ends are checked; <see cref="RuleEndFilter.All"/> for a node rule.</param>
/// <param name="EndTypes">Pattern text of node types for <see cref="RuleEndFilter.NodeIs"/> and <see cref="RuleEndFilter.NodeIsNot"/>; <c>null</c> or empty otherwise.</param>
/// <param name="CountTypes">Pattern text of the edge types that are counted.</param>
/// <param name="Min">The smallest allowed count; not negative.</param>
/// <param name="Max">The largest allowed count, or <c>null</c> for no upper limit; not below <paramref name="Min"/>.</param>
public sealed record NetworkRuleDefinition(
    string Id,
    RuleSeverity Severity,
    RuleSubject Subject,
    string SubjectTypes,
    RuleEndFilter EndFilter,
    string? EndTypes,
    string CountTypes,
    int Min,
    int? Max);

/// <summary>
/// A rule that counts the direct neighbours of an element and compares the count with an allowed range (ADR 0052).
/// Build one with <see cref="ForNodes"/> or <see cref="ForEdges"/>, or resolve a <see cref="NetworkRuleDefinition"/> with
/// <see cref="FromDefinition"/>. Immutable and thread-safe; use it for any number of checks (<see cref="NetworkValidation"/>).
/// </summary>
public sealed class NetworkRule
{
    internal NetworkRule(
        string id,
        RuleSeverity severity,
        RuleSubject subject,
        TypeSet subjectTypes,
        RuleEndFilter endFilter,
        TypeSet? endTypes,
        TypeSet countTypes,
        int min,
        int? max)
    {
        Id = id;
        Severity = severity;
        Subject = subject;
        SubjectTypes = subjectTypes;
        EndFilter = endFilter;
        EndTypes = endTypes;
        CountTypes = countTypes;
        Min = min;
        Max = max;
    }

    /// <summary>The id of the rule.</summary>
    public string Id { get; }

    /// <summary>How serious a violation is.</summary>
    public RuleSeverity Severity { get; }

    /// <summary>Whether the rule checks nodes or edges.</summary>
    public RuleSubject Subject { get; }

    /// <summary>The types that are checked.</summary>
    public TypeSet SubjectTypes { get; }

    /// <summary>For an edge rule, which ends are checked.</summary>
    public RuleEndFilter EndFilter { get; }

    /// <summary>The node types of <see cref="EndFilter"/>; <c>null</c> for <see cref="RuleEndFilter.All"/>.</summary>
    public TypeSet? EndTypes { get; }

    /// <summary>The edge types that are counted.</summary>
    public TypeSet CountTypes { get; }

    /// <summary>The smallest allowed count.</summary>
    public int Min { get; }

    /// <summary>The largest allowed count; <c>null</c> for no upper limit.</summary>
    public int? Max { get; }

    /// <summary>Starts a rule that checks the nodes of the given types and counts the edges at them.</summary>
    /// <param name="id">The id of the rule; not empty.</param>
    /// <param name="subjectTypes">The node types to check; <see cref="TypeSet.Untyped"/> addresses helper nodes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="subjectTypes"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is <c>null</c>, empty or white space.</exception>
    public static NodeRuleBuilder ForNodes(string id, TypeSet subjectTypes)
    {
        ThrowIfNoId(id);
        ArgumentNullException.ThrowIfNull(subjectTypes);
        return new NodeRuleBuilder(id, subjectTypes);
    }

    /// <summary>Starts a rule that checks the edges of the given types and counts the other edges at their ends.</summary>
    /// <param name="id">The id of the rule; not empty.</param>
    /// <param name="subjectTypes">The edge types to check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="subjectTypes"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is <c>null</c>, empty or white space.</exception>
    public static EdgeRuleBuilder ForEdges(string id, TypeSet subjectTypes)
    {
        ThrowIfNoId(id);
        ArgumentNullException.ThrowIfNull(subjectTypes);
        return new EdgeRuleBuilder(id, subjectTypes, RuleEndFilter.All, null);
    }

    /// <summary>Resolves a row into a rule, with the patterns resolved against the type registry.</summary>
    /// <param name="definition">The row.</param>
    /// <param name="types">The types of the network.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// The definition cannot be a rule: no id, a pattern that cannot be read, that matches no type or only a type of the
    /// wrong kind, an end filter on a node rule, or end types without an end filter. The message names the rule id.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><c>Min</c> is below 0 or above <c>Max</c>; the message names the rule id.</exception>
    public static NetworkRule FromDefinition(NetworkRuleDefinition definition, TypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(types);
        ThrowIfNoId(definition.Id);
        string id = definition.Id;
        if (!Enum.IsDefined(definition.Severity))
        {
            throw new ArgumentException($"Rule '{id}': the severity {definition.Severity} is unknown.", nameof(definition));
        }

        if (!Enum.IsDefined(definition.Subject))
        {
            throw new ArgumentException($"Rule '{id}': the subject {definition.Subject} is unknown.", nameof(definition));
        }

        if (!Enum.IsDefined(definition.EndFilter))
        {
            throw new ArgumentException($"Rule '{id}': the end filter {definition.EndFilter} is unknown.", nameof(definition));
        }

        bool isNode = definition.Subject == RuleSubject.Node;
        var subjectTypes = ResolvePattern(id, "subject types", definition.SubjectTypes, isNode ? types.Nodes : types.Edges);
        var countTypes = ResolvePattern(id, "count types", definition.CountTypes, types.Edges);

        TypeSet? endTypes = null;
        bool hasEndText = !string.IsNullOrWhiteSpace(definition.EndTypes);
        if (definition.EndFilter == RuleEndFilter.All)
        {
            if (hasEndText)
            {
                throw new ArgumentException($"Rule '{id}': end types are given, but the end filter is All.", nameof(definition));
            }
        }
        else
        {
            if (isNode)
            {
                throw new ArgumentException($"Rule '{id}': a node rule has no ends, the end filter must be All.", nameof(definition));
            }

            if (!hasEndText)
            {
                throw new ArgumentException($"Rule '{id}': the end filter {definition.EndFilter} needs end types.", nameof(definition));
            }

            endTypes = ResolvePattern(id, "end types", definition.EndTypes, types.Nodes);
        }

        ThrowIfBadRange(id, definition.Min, definition.Max);
        return new NetworkRule(id, definition.Severity, definition.Subject, subjectTypes, definition.EndFilter, endTypes, countTypes, definition.Min, definition.Max);
    }

    /// <summary>Gives the rule as a row, so that <see cref="FromDefinition"/> resolves it to an equal rule.</summary>
    /// <exception cref="InvalidOperationException">
    /// A type set of the rule was made from type ids (<see cref="TypeSet.Of"/>) and has no pattern text. Rules from
    /// <see cref="FromDefinition"/> and from sets resolved by a <see cref="TypeRegistry"/> always have it.
    /// </exception>
    public NetworkRuleDefinition ToDefinition() =>
        new(Id, Severity, Subject, TextOf(SubjectTypes, "subject types"), EndFilter, EndTypes is null ? null : TextOf(EndTypes, "end types"), TextOf(CountTypes, "count types"), Min, Max);

    /// <summary>A readable form for reports, for example <c>KV-FREIES-ENDE: edges [*, Kastenverbinder, *], ends not [*, Kasten, *], other edges [*, Leitung, *] = 1</c>. For people, not for parsing.</summary>
    public override string ToString()
    {
        var text = new System.Text.StringBuilder(Id).Append(": ");
        if (Subject == RuleSubject.Node)
        {
            text.Append("nodes ").Append(Show(SubjectTypes)).Append(", edges ").Append(Show(CountTypes));
        }
        else
        {
            text.Append("edges ").Append(Show(SubjectTypes));
            if (EndFilter != RuleEndFilter.All && EndTypes is not null)
            {
                text.Append(EndFilter == RuleEndFilter.NodeIs ? ", ends " : ", ends not ").Append(Show(EndTypes));
            }

            text.Append(", other edges ").Append(Show(CountTypes));
        }

        text.Append(' ');
        if (Min == Max)
        {
            text.Append("= ").Append(Min.ToString(CultureInfo.InvariantCulture));
        }
        else if (Max is null)
        {
            text.Append(">= ").Append(Min.ToString(CultureInfo.InvariantCulture));
        }
        else if (Min == 0)
        {
            text.Append("<= ").Append(Max.Value.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            text.Append("between ").Append(Min.ToString(CultureInfo.InvariantCulture)).Append(" and ").Append(Max.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Severity == RuleSeverity.Warning)
        {
            text.Append(" (warning)");
        }

        return text.ToString();
    }

    /// <summary>The same rule with the severity <see cref="RuleSeverity.Warning"/>.</summary>
    public NetworkRule AsWarning() =>
        new(Id, RuleSeverity.Warning, Subject, SubjectTypes, EndFilter, EndTypes, CountTypes, Min, Max);

    internal static void ThrowIfBadRange(string id, int min, int? max)
    {
        if (min < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(min), min, $"Rule '{id}': Min must not be negative.");
        }

        if (max is { } upper && upper < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, $"Rule '{id}': Max ({upper}) must not be below Min ({min}).");
        }
    }

    private static void ThrowIfNoId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A rule needs an id that is not empty.", nameof(id));
        }
    }

    private static TypeSet ResolvePattern(string id, string what, string? text, Func<string[], TypeSet> resolve)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException($"Rule '{id}': the {what} are empty.", "definition");
        }

        try
        {
            return resolve([text]);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException($"Rule '{id}': the {what} are not valid. {exception.Message}", "definition", exception);
        }
    }

    private string TextOf(TypeSet set, string what) =>
        set.PatternText ?? throw new InvalidOperationException(
            $"Rule '{Id}': the {what} were made from type ids and have no pattern text; resolve them with a TypeRegistry to get a definition.");

    private static string Show(TypeSet set) => set.PatternText ?? set.ToString();
}

/// <summary>The start of a node rule (<see cref="NetworkRule.ForNodes"/>): what to count.</summary>
public sealed class NodeRuleBuilder
{
    private readonly string _id;
    private readonly TypeSet _subjectTypes;

    internal NodeRuleBuilder(string id, TypeSet subjectTypes)
    {
        _id = id;
        _subjectTypes = subjectTypes;
    }

    /// <summary>Counts the edges at the node whose type is in <paramref name="types"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <c>null</c>.</exception>
    public RuleCountBuilder CountEdges(TypeSet types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return new RuleCountBuilder(_id, RuleSubject.Node, _subjectTypes, RuleEndFilter.All, null, types);
    }
}

/// <summary>The start of an edge rule (<see cref="NetworkRule.ForEdges"/>): which ends, and what to count. Immutable.</summary>
public sealed class EdgeRuleBuilder
{
    private readonly string _id;
    private readonly TypeSet _subjectTypes;
    private readonly RuleEndFilter _filter;
    private readonly TypeSet? _endTypes;

    internal EdgeRuleBuilder(string id, TypeSet subjectTypes, RuleEndFilter filter, TypeSet? endTypes)
    {
        _id = id;
        _subjectTypes = subjectTypes;
        _filter = filter;
        _endTypes = endTypes;
    }

    /// <summary>Checks only the ends whose node type is in <paramref name="types"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <c>null</c>.</exception>
    public EdgeRuleBuilder AtEndsWhereNode(TypeSet types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return new EdgeRuleBuilder(_id, _subjectTypes, RuleEndFilter.NodeIs, types);
    }

    /// <summary>Checks only the ends whose node type is not in <paramref name="types"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <c>null</c>.</exception>
    public EdgeRuleBuilder AtEndsWhereNodeIsNot(TypeSet types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return new EdgeRuleBuilder(_id, _subjectTypes, RuleEndFilter.NodeIsNot, types);
    }

    /// <summary>Counts the other edges at the end whose type is in <paramref name="types"/>; the edge itself never counts.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <c>null</c>.</exception>
    public RuleCountBuilder CountOtherEdges(TypeSet types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return new RuleCountBuilder(_id, RuleSubject.Edge, _subjectTypes, _filter, _endTypes, types);
    }
}

/// <summary>The last step of a rule: the allowed range of the count. Each method returns the finished <see cref="NetworkRule"/>.</summary>
public sealed class RuleCountBuilder
{
    private readonly string _id;
    private readonly RuleSubject _subject;
    private readonly TypeSet _subjectTypes;
    private readonly RuleEndFilter _filter;
    private readonly TypeSet? _endTypes;
    private readonly TypeSet _countTypes;

    internal RuleCountBuilder(string id, RuleSubject subject, TypeSet subjectTypes, RuleEndFilter filter, TypeSet? endTypes, TypeSet countTypes)
    {
        _id = id;
        _subject = subject;
        _subjectTypes = subjectTypes;
        _filter = filter;
        _endTypes = endTypes;
        _countTypes = countTypes;
    }

    /// <summary>The count must lie between <paramref name="min"/> and <paramref name="max"/>, both included; no <paramref name="max"/> means no upper limit.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="min"/> is negative or above <paramref name="max"/>.</exception>
    public NetworkRule Between(int min, int? max)
    {
        NetworkRule.ThrowIfBadRange(_id, min, max);
        return new NetworkRule(_id, RuleSeverity.Error, _subject, _subjectTypes, _filter, _endTypes, _countTypes, min, max);
    }

    /// <summary>The count must be at least <paramref name="min"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="min"/> is negative.</exception>
    public NetworkRule AtLeast(int min) => Between(min, null);

    /// <summary>The count must be exactly <paramref name="count"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public NetworkRule Exactly(int count) => Between(count, count);

    /// <summary>There must be nothing to count (a forbidden neighbour).</summary>
    public NetworkRule None() => Between(0, 0);
}

/// <summary>
/// A violation of a <see cref="NetworkRule"/> (ADR 0052).
/// </summary>
/// <typeparam name="TNodeId">The node id.</typeparam>
/// <typeparam name="TEdgeId">The edge id.</typeparam>
/// <param name="RuleId">The id of the rule.</param>
/// <param name="Severity">The severity of the rule.</param>
/// <param name="Kind">Whether a node rule or an edge rule was violated.</param>
/// <param name="Node">The node that was checked; for an edge rule, the end of the edge where the edges were counted.</param>
/// <param name="Edge">Only for an edge rule: the edge that was checked; the default value for a node rule.</param>
/// <param name="TypeId">The type of the checked element (the node, or the edge).</param>
/// <param name="Count">The count that was found.</param>
/// <param name="Min">The smallest allowed count.</param>
/// <param name="Max">The largest allowed count; <c>null</c> for no upper limit.</param>
/// <param name="Location">Where <paramref name="Node"/> lies.</param>
public readonly record struct RuleViolation<TNodeId, TEdgeId>(
    string RuleId,
    RuleSeverity Severity,
    ElementKind Kind,
    TNodeId Node,
    TEdgeId? Edge,
    int TypeId,
    int Count,
    int Min,
    int? Max,
    Coordinate Location)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>;
