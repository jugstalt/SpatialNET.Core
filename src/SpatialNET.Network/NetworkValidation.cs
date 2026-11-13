namespace SpatialNET.Network;

/// <summary>
/// Checks <see cref="NetworkRule"/>s on an <see cref="IGraph{TNodeId, TEdgeId}"/> (ADR 0052). The methods stream: they
/// read their input in blocks, ask the graph for the nodes and the edges of a block in one batch each, and yield the
/// violations of the block before they read the next one. Pass the elements sorted by shard, so that the neighbours of
/// a block come from the shards that are already in the cache.
/// </summary>
/// <remarks>
/// <para>An edge rule is checked at each chosen end on its own. The edge itself never counts, parallel edges count one by
/// one, a loop counts once, and the switching state does not matter: a disabled edge counts like any other.</para>
/// <para>A loop has two ends that are the same node; an edge rule checks both, in the order of the ends.</para>
/// <para>Thread-safe as far as the graph is: a check has no shared state.</para>
/// </remarks>
public static class NetworkValidation
{
    private const int BlockSize = 1000;

    /// <summary>
    /// Checks the node rules of <paramref name="rules"/> for each given node. Meant for streams over the whole network; it
    /// keeps no set of checked nodes, so a node given twice is checked twice.
    /// </summary>
    /// <param name="graph">The network.</param>
    /// <param name="nodes">The ids of the nodes to check, ideally sorted by shard.</param>
    /// <param name="rules">The rules; the edge rules are ignored. Ids must be unique.</param>
    /// <param name="cancellationToken">Stops the check between two blocks.</param>
    /// <returns>The violations in the order of the input, for one node in the order of the rules.</returns>
    /// <exception cref="ArgumentNullException">An argument or a rule is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Two rules have the same id.</exception>
    /// <exception cref="InvalidGraphException">While enumerating: a given node is not in the graph.</exception>
    /// <exception cref="OperationCanceledException">While enumerating: the token was cancelled.</exception>
    public static IEnumerable<RuleViolation<TNodeId, TEdgeId>> ValidateNodes<TNodeId, TEdgeId>(
        this IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<TNodeId> nodes,
        IEnumerable<NetworkRule> rules,
        CancellationToken cancellationToken = default)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(nodes);
        var (nodeRules, _) = SplitRules(rules);
        return ValidateNodesCore(graph, nodes, nodeRules, cancellationToken);
    }

    /// <summary>
    /// Checks the edge rules of <paramref name="rules"/> for each given edge record. It keeps no set of checked edges, so
    /// an edge given twice is checked twice.
    /// </summary>
    /// <param name="graph">The network.</param>
    /// <param name="edges">The edges to check as the host read them, ideally sorted by shard.</param>
    /// <param name="rules">The rules; the node rules are ignored. Ids must be unique.</param>
    /// <param name="cancellationToken">Stops the check between two blocks.</param>
    /// <returns>The violations in the order of the input, for one edge in the order of the rules, at <c>From</c> before <c>To</c>.</returns>
    /// <exception cref="ArgumentNullException">An argument or a rule is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Two rules have the same id.</exception>
    /// <exception cref="InvalidGraphException">While enumerating: the graph does not return the node at an end of an edge.</exception>
    /// <exception cref="OperationCanceledException">While enumerating: the token was cancelled.</exception>
    public static IEnumerable<RuleViolation<TNodeId, TEdgeId>> ValidateEdges<TNodeId, TEdgeId>(
        this IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<GraphEdge<TNodeId, TEdgeId>> edges,
        IEnumerable<NetworkRule> rules,
        CancellationToken cancellationToken = default)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(edges);
        var (_, edgeRules) = SplitRules(rules);
        return ValidateEdgesCore(graph, edges, edgeRules, cancellationToken);
    }

    /// <summary>
    /// Checks the node rules for the given nodes and the edge rules for every edge at them, each edge once. It remembers
    /// the nodes and edges it has checked, so it is meant for a bounded area such as one local network; over the whole
    /// network use <see cref="ValidateNodes{TNodeId, TEdgeId}"/> and <see cref="ValidateEdges{TNodeId, TEdgeId}"/>.
    /// </summary>
    /// <param name="graph">The network.</param>
    /// <param name="nodes">The ids of the nodes of the area; a node given twice is checked once. Edges of the nodes may lead to nodes outside the area, which count as context and are not checked.</param>
    /// <param name="rules">The rules. Ids must be unique.</param>
    /// <param name="cancellationToken">Stops the check between two blocks.</param>
    /// <returns>The violations in the order of the nodes: for each node its node rules, then the edge rules of the edges first met at it.</returns>
    /// <exception cref="ArgumentNullException">An argument or a rule is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Two rules have the same id.</exception>
    /// <exception cref="InvalidGraphException">While enumerating: a given node or the node at an end of an edge is not in the graph.</exception>
    /// <exception cref="OperationCanceledException">While enumerating: the token was cancelled.</exception>
    public static IEnumerable<RuleViolation<TNodeId, TEdgeId>> Validate<TNodeId, TEdgeId>(
        this IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<TNodeId> nodes,
        IEnumerable<NetworkRule> rules,
        CancellationToken cancellationToken = default)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(nodes);
        var (nodeRules, edgeRules) = SplitRules(rules);
        return ValidateCore(graph, nodes, nodeRules, edgeRules, cancellationToken);
    }

    private static (NetworkRule[] NodeRules, NetworkRule[] EdgeRules) SplitRules(IEnumerable<NetworkRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var nodeRules = new List<NetworkRule>();
        var edgeRules = new List<NetworkRule>();
        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule, nameof(rules));
            if (!ids.Add(rule.Id))
            {
                throw new ArgumentException($"The rule id '{rule.Id}' is used twice.", nameof(rules));
            }

            (rule.Subject == RuleSubject.Node ? nodeRules : edgeRules).Add(rule);
        }

        return ([.. nodeRules], [.. edgeRules]);
    }

    private static IEnumerable<RuleViolation<TNodeId, TEdgeId>> ValidateNodesCore<TNodeId, TEdgeId>(
        IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<TNodeId> nodes,
        NetworkRule[] nodeRules,
        CancellationToken cancellationToken)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        if (nodeRules.Length == 0)
        {
            yield break;
        }

        foreach (var block in Blocks(nodes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = LoadNodeBlock(graph, block);
            foreach (var id in block)
            {
                foreach (var violation in CheckNode(data, id, nodeRules))
                {
                    yield return violation;
                }
            }
        }
    }

    private static IEnumerable<RuleViolation<TNodeId, TEdgeId>> ValidateEdgesCore<TNodeId, TEdgeId>(
        IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<GraphEdge<TNodeId, TEdgeId>> edges,
        NetworkRule[] edgeRules,
        CancellationToken cancellationToken)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        if (edgeRules.Length == 0)
        {
            yield break;
        }

        foreach (var block in Blocks(edges))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var perEdge = CheckEdgeBlock(graph, block, edgeRules);
            foreach (var list in perEdge)
            {
                if (list is not null)
                {
                    foreach (var violation in list)
                    {
                        yield return violation;
                    }
                }
            }
        }
    }

    private static IEnumerable<RuleViolation<TNodeId, TEdgeId>> ValidateCore<TNodeId, TEdgeId>(
        IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<TNodeId> nodes,
        NetworkRule[] nodeRules,
        NetworkRule[] edgeRules,
        CancellationToken cancellationToken)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        if (nodeRules.Length == 0 && edgeRules.Length == 0)
        {
            yield break;
        }

        var seenNodes = new HashSet<TNodeId>();
        var seenEdges = new HashSet<TEdgeId>();
        foreach (var block in Blocks(nodes.Where(seenNodes.Add)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = LoadNodeBlock(graph, block);

            // The edges first met at each node of the block, so that an edge is checked once and yielded at its first node.
            var newEdges = new List<GraphEdge<TNodeId, TEdgeId>>();
            var rangeOfNode = new (int Start, int Count)[block.Count];
            if (edgeRules.Length > 0)
            {
                for (int i = 0; i < block.Count; i++)
                {
                    int start = newEdges.Count;
                    foreach (var edge in data.EdgesAt[block[i]])
                    {
                        if (seenEdges.Add(edge.Id))
                        {
                            newEdges.Add(edge);
                        }
                    }

                    rangeOfNode[i] = (start, newEdges.Count - start);
                }
            }

            var perEdge = newEdges.Count == 0 ? [] : CheckEdgeBlock(graph, newEdges, edgeRules);
            for (int i = 0; i < block.Count; i++)
            {
                foreach (var violation in CheckNode(data, block[i], nodeRules))
                {
                    yield return violation;
                }

                var (start, count) = rangeOfNode[i];
                for (int e = start; e < start + count; e++)
                {
                    if (perEdge[e] is { } list)
                    {
                        foreach (var violation in list)
                        {
                            yield return violation;
                        }
                    }
                }
            }
        }
    }

    // The nodes of a block with the edges at them (a loop once), as one batch of each.
    private static NodeBlock<TNodeId, TEdgeId> LoadNodeBlock<TNodeId, TEdgeId>(IGraph<TNodeId, TEdgeId> graph, List<TNodeId> block)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        var distinct = Distinct(block);
        var nodes = new Dictionary<TNodeId, GraphNode<TNodeId>>(distinct.Count);
        foreach (var node in graph.GetNodes(distinct))
        {
            nodes[node.Id] = node;
        }

        foreach (var id in distinct)
        {
            if (!nodes.ContainsKey(id))
            {
                throw new InvalidGraphException($"The node {id} is not in the graph.");
            }
        }

        var edgesAt = new Dictionary<TNodeId, List<GraphEdge<TNodeId, TEdgeId>>>(distinct.Count);
        foreach (var id in distinct)
        {
            edgesAt[id] = [];
        }

        foreach (var edge in graph.GetEdges(distinct))
        {
            if (edgesAt.TryGetValue(edge.From, out var atFrom))
            {
                atFrom.Add(edge);
            }

            if (!edge.To.Equals(edge.From) && edgesAt.TryGetValue(edge.To, out var atTo))
            {
                atTo.Add(edge);
            }
        }

        return new NodeBlock<TNodeId, TEdgeId>(nodes, edgesAt);
    }

    private static IEnumerable<RuleViolation<TNodeId, TEdgeId>> CheckNode<TNodeId, TEdgeId>(
        NodeBlock<TNodeId, TEdgeId> data, TNodeId id, NetworkRule[] nodeRules)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        var node = data.Nodes[id];
        var edges = data.EdgesAt[id];
        foreach (var rule in nodeRules)
        {
            if (!rule.SubjectTypes.Contains(node.TypeId))
            {
                continue;
            }

            int count = 0;
            foreach (var edge in edges)
            {
                if (rule.CountTypes.Contains(edge.TypeId))
                {
                    count++;
                }
            }

            if (Violates(rule, count))
            {
                yield return new RuleViolation<TNodeId, TEdgeId>(
                    rule.Id, rule.Severity, ElementKind.Node, node.Id, default, node.TypeId, count, rule.Min, rule.Max, node.Location);
            }
        }
    }

    // The violations of each edge of the block, parallel to the block; null where an edge has none.
    private static List<RuleViolation<TNodeId, TEdgeId>>?[] CheckEdgeBlock<TNodeId, TEdgeId>(
        IGraph<TNodeId, TEdgeId> graph, IReadOnlyList<GraphEdge<TNodeId, TEdgeId>> block, NetworkRule[] edgeRules)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        var result = new List<RuleViolation<TNodeId, TEdgeId>>?[block.Count];
        var ends = new List<TNodeId>();
        var endSet = new HashSet<TNodeId>();
        bool Applies(GraphEdge<TNodeId, TEdgeId> edge) => edgeRules.Any(rule => rule.SubjectTypes.Contains(edge.TypeId));
        var relevant = new bool[block.Count];
        for (int i = 0; i < block.Count; i++)
        {
            if (relevant[i] = Applies(block[i]))
            {
                if (endSet.Add(block[i].From))
                {
                    ends.Add(block[i].From);
                }

                if (endSet.Add(block[i].To))
                {
                    ends.Add(block[i].To);
                }
            }
        }

        if (ends.Count == 0)
        {
            return result;
        }

        var nodes = new Dictionary<TNodeId, GraphNode<TNodeId>>(ends.Count);
        foreach (var node in graph.GetNodes(ends))
        {
            nodes[node.Id] = node;
        }

        foreach (var id in ends)
        {
            if (!nodes.ContainsKey(id))
            {
                throw new InvalidGraphException($"The node {id} at the end of an edge is not in the graph.");
            }
        }

        var edgesAt = new Dictionary<TNodeId, List<GraphEdge<TNodeId, TEdgeId>>>(ends.Count);
        foreach (var id in ends)
        {
            edgesAt[id] = [];
        }

        foreach (var edge in graph.GetEdges(ends))
        {
            if (edgesAt.TryGetValue(edge.From, out var atFrom))
            {
                atFrom.Add(edge);
            }

            if (!edge.To.Equals(edge.From) && edgesAt.TryGetValue(edge.To, out var atTo))
            {
                atTo.Add(edge);
            }
        }

        for (int i = 0; i < block.Count; i++)
        {
            if (!relevant[i])
            {
                continue;
            }

            var edge = block[i];
            foreach (var rule in edgeRules)
            {
                if (!rule.SubjectTypes.Contains(edge.TypeId))
                {
                    continue;
                }

                foreach (var end in (ReadOnlySpan<TNodeId>)[edge.From, edge.To])
                {
                    var node = nodes[end];
                    if (rule.EndFilter != RuleEndFilter.All && rule.EndTypes!.Contains(node.TypeId) != (rule.EndFilter == RuleEndFilter.NodeIs))
                    {
                        continue;
                    }

                    int count = 0;
                    foreach (var other in edgesAt[end])
                    {
                        if (!other.Id.Equals(edge.Id) && rule.CountTypes.Contains(other.TypeId))
                        {
                            count++;
                        }
                    }

                    if (Violates(rule, count))
                    {
                        (result[i] ??= []).Add(new RuleViolation<TNodeId, TEdgeId>(
                            rule.Id, rule.Severity, ElementKind.Edge, node.Id, edge.Id, edge.TypeId, count, rule.Min, rule.Max, node.Location));
                    }
                }
            }
        }

        return result;
    }

    private static bool Violates(NetworkRule rule, int count) => count < rule.Min || (rule.Max is { } max && count > max);

    private static List<T> Distinct<T>(List<T> items)
        where T : notnull, IEquatable<T>
    {
        var seen = new HashSet<T>();
        var result = new List<T>(items.Count);
        foreach (var item in items)
        {
            if (seen.Add(item))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static IEnumerable<List<T>> Blocks<T>(IEnumerable<T> source)
    {
        var block = new List<T>(BlockSize);
        foreach (var item in source)
        {
            block.Add(item);
            if (block.Count == BlockSize)
            {
                yield return block;
                block = new List<T>(BlockSize);
            }
        }

        if (block.Count > 0)
        {
            yield return block;
        }
    }

    private sealed record NodeBlock<TNodeId, TEdgeId>(
        Dictionary<TNodeId, GraphNode<TNodeId>> Nodes,
        Dictionary<TNodeId, List<GraphEdge<TNodeId, TEdgeId>>> EdgesAt)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>;
}
