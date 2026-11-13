using System.Diagnostics;
using System.Globalization;

namespace SpatialNET.Network.Building;

/// <summary>
/// Builds a network from the host's points and lines (ADR 0050): joins every line end and point within the tolerance into
/// one node, splits lines at real nodes and T junctions (and, if asked, at crossings), assigns shards from polygons and
/// writes all nodes, then all edges, to a writer. The geometry is not changed; no vertex moves.
/// </summary>
/// <typeparam name="TNodeId">The host's node id.</typeparam>
/// <typeparam name="TEdgeId">The host's edge id.</typeparam>
/// <typeparam name="TSource">The host's reference to a source object, passed on unchanged.</typeparam>
/// <remarks>
/// <para>A builder is used once: the <c>Add…</c> calls may come in any order, <see cref="Build"/> handles the points before the
/// lines, and every call after it throws. It is not thread-safe.</para>
/// <para>The same input in the same order gives the same nodes, edges, ids and output order; another order gives the same
/// nodes and edges. Every search inside goes through a grid or a spatial index.</para>
/// </remarks>
public sealed class NetworkBuilder<TNodeId, TEdgeId, TSource>
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    private readonly NetworkBuildOptions _options;
    private readonly INetworkIdFactory<TNodeId, TEdgeId, TSource> _ids;
    private readonly Dictionary<int, WeightDefinition> _weights;
    private readonly List<RealNode> _realNodes = [];
    private readonly HashSet<TNodeId> _realIds = [];
    private readonly List<Line> _lines = [];
    private bool _built;

    /// <summary>Creates a builder.</summary>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The tolerance or the fallback cell size is not finite and greater than 0.</exception>
    /// <exception cref="ArgumentException">A shard region or a weight definition is <c>null</c>, or two weights share an id.</exception>
    public NetworkBuilder(NetworkBuildOptions options, INetworkIdFactory<TNodeId, TEdgeId, TSource> ids)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ids);
        if (!double.IsFinite(options.Tolerance) || options.Tolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Tolerance, "The tolerance must be finite and greater than 0.");
        }

        if (!double.IsFinite(options.FallbackShardCellSize) || options.FallbackShardCellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.FallbackShardCellSize, "The fallback shard cell size must be finite and greater than 0.");
        }

        ArgumentNullException.ThrowIfNull(options.ShardRegions, nameof(options));
        ArgumentNullException.ThrowIfNull(options.Weights, nameof(options));
        if (options.ShardRegions.Any(r => r is null || r.Area is null))
        {
            throw new ArgumentException("A shard region or its area is null.", nameof(options));
        }

        _weights = [];
        foreach (var weight in options.Weights)
        {
            if (weight is null || !_weights.TryAdd(weight.Id, weight))
            {
                throw new ArgumentException($"A weight definition is null or its id {weight?.Id} is used twice.", nameof(options));
            }
        }

        _options = options;
        _ids = ids;
    }

    /// <summary>Adds a real node: a box, a transformer, a switch, a customer.</summary>
    /// <param name="source">The host's reference to the point.</param>
    /// <param name="id">The node id; unique.</param>
    /// <param name="point">The location; Z and M are not used.</param>
    /// <param name="typeId">The node type, not 0 (which is the untyped helper node).</param>
    /// <param name="blocked">The normal switching state.</param>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> or <paramref name="point"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="typeId"/> is 0.</exception>
    /// <exception cref="ArgumentException">The location is not finite, or the id was added before.</exception>
    /// <exception cref="InvalidOperationException">The builder has built already.</exception>
    public void AddNode(TSource source, TNodeId id, IPoint2D point, int typeId, bool blocked = false)
    {
        ArgumentNullException.ThrowIfNull(point);
        AddNode(source, id, new Coordinate(point.X, point.Y), typeId, blocked);
    }

    /// <summary>Adds a real node at a coordinate; Z and M are not used.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="typeId"/> is 0.</exception>
    /// <exception cref="ArgumentException">The location is not finite, or the id was added before.</exception>
    /// <exception cref="InvalidOperationException">The builder has built already.</exception>
    public void AddNode(TSource source, TNodeId id, Coordinate location, int typeId, bool blocked = false)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(id);
        if (typeId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(typeId), typeId, "Type 0 is the untyped helper node; a real node needs a type.");
        }

        if (!double.IsFinite(location.X) || !double.IsFinite(location.Y))
        {
            throw new ArgumentException($"The node {id} has a location that is not finite.", nameof(location));
        }

        if (!_realIds.Add(id))
        {
            throw new ArgumentException($"The node {id} was added before.", nameof(id));
        }

        _realNodes.Add(new RealNode(source, id, location, typeId, blocked));
    }

    /// <summary>Adds a line from any feature class: cable, overhead line, connector. Multi-part lines are allowed.</summary>
    /// <param name="source">The host's reference to the line.</param>
    /// <param name="line">The geometry; the coordinates are copied, so the host may release it.</param>
    /// <param name="typeId">The edge type.</param>
    /// <param name="attributes">Direction, switching state, surveyed length and weights; <c>null</c> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The surveyed length is negative or not finite, or the direction is unknown.</exception>
    /// <exception cref="ArgumentException">The line is empty, a path has fewer than two points, a coordinate is not finite, or a weight is not in the options.</exception>
    /// <exception cref="InvalidOperationException">The builder has built already.</exception>
    public void AddLine(TSource source, IPolyline2D line, int typeId, LineAttributes? attributes = null)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(line);
        attributes ??= Default;
        if (attributes.Length is { } surveyed && (!double.IsFinite(surveyed) || surveyed < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(attributes), surveyed, "The surveyed length must be finite and not negative.");
        }

        if (attributes.Direction is not (EdgeDirection.Both or EdgeDirection.Forward or EdgeDirection.Backward))
        {
            throw new ArgumentOutOfRangeException(nameof(attributes), attributes.Direction, "Unknown direction.");
        }

        foreach (var weight in attributes.Weights ?? [])
        {
            if (!_weights.ContainsKey(weight.WeightId))
            {
                throw new ArgumentException($"The line has a value for the weight {weight.WeightId}, which is not in the options.", nameof(attributes));
            }
        }

        if (line.PathCount == 0)
        {
            throw new ArgumentException("The line is empty.", nameof(line));
        }

        var paths = new double[line.PathCount][];
        double length = 0;
        for (int p = 0; p < paths.Length; p++)
        {
            var path = line[p];
            if (path.PointCount < 2)
            {
                throw new ArgumentException($"Path {p} of the line has fewer than two points.", nameof(line));
            }

            var xy = new double[2 * path.PointCount];
            for (int i = 0; i < path.PointCount; i++)
            {
                var point = path[i];
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                {
                    throw new ArgumentException($"Path {p} of the line has a coordinate that is not finite.", nameof(line));
                }

                xy[2 * i] = point.X;
                xy[2 * i + 1] = point.Y;
                if (i > 0)
                {
                    length += Math.Sqrt(Square(xy[2 * i] - xy[2 * i - 2]) + Square(xy[2 * i + 1] - xy[2 * i - 1]));
                }
            }

            paths[p] = xy;
        }

        _lines.Add(new Line(source, typeId, attributes, paths, length));
    }

    /// <summary>Joins, splits, assigns shards, checks the ids and writes every node and then every edge to the writer.</summary>
    /// <param name="writer">Where the network goes: the host's database, or an <c>InMemoryGraph</c>.</param>
    /// <param name="progress">Reported at most every 250 ms and at the end of each phase, on the building thread.</param>
    /// <param name="cancellationToken">Stops the build between two items.</param>
    /// <returns>The counts and the notes about the input.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    /// <exception cref="NetworkBuildException">The input cannot form a network; thrown before the first write.</exception>
    /// <exception cref="InvalidOperationException">The builder has built already.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public BuildResult Build(
        INetworkWriter<TNodeId, TEdgeId, TSource> writer,
        IProgress<BuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        EnsureNotBuilt();
        _built = true;
        var run = new Run(this, new Reporter(progress), cancellationToken);
        return run.Execute(writer);
    }

    private static readonly LineAttributes Default = new();

    private static double Square(double v) => v * v;

    private void EnsureNotBuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException("The builder has built already; a builder is used once.");
        }
    }

    private readonly record struct RealNode(TSource Source, TNodeId Id, Coordinate Location, int TypeId, bool Blocked);

    private sealed record Line(TSource Source, int TypeId, LineAttributes Attributes, double[][] Paths, double Length);

    /// <summary>Reports progress at most every 250 ms and at the end of each phase.</summary>
    private sealed class Reporter(IProgress<BuildProgress>? progress)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _last = long.MinValue;

        public void Report(BuildPhase phase, long done, long total)
        {
            if (progress is null)
            {
                return;
            }

            long now = _clock.ElapsedMilliseconds;
            if (done >= total || now - _last >= 250)
            {
                _last = now;
                progress.Report(new BuildProgress(phase, done, total));
            }
        }
    }

    /// <summary>One build: all intermediate state, so the builder itself keeps only its input.</summary>
    private sealed class Run(NetworkBuilder<TNodeId, TEdgeId, TSource> builder, Reporter reporter, CancellationToken cancellationToken)
    {
        private readonly double _tolerance = builder._options.Tolerance;
        private readonly List<BuildDiagnostic> _diagnostics = [];
        private readonly List<NodeConflict> _conflicts = [];

        // parts: one per path of every line
        private readonly List<Part> _parts = [];

        // nodes: the real nodes first (index = position in builder._realNodes), then the helper nodes
        private readonly List<Coordinate> _location = [];
        private readonly List<bool> _hasLineEnd = [];
        private int[] _partStart = [], _partEnd = [];
        private List<Split>[] _splits = [];
        private readonly List<EdgePlan> _edges = [];

        public BuildResult Execute(INetworkWriter<TNodeId, TEdgeId, TSource> writer)
        {
            CollectParts();
            JoinEnds();
            SplitLines();
            PlanEdges();
            FindHelpers();
            var shards = AssignShards();
            var nodeIds = AssignIds();
            if (_conflicts.Count > 0)
            {
                throw new NetworkBuildException(_conflicts);
            }

            return Write(writer, nodeIds, shards);
        }

        private int RealCount => builder._realNodes.Count;

        private void CollectParts()
        {
            for (int l = 0; l < builder._lines.Count; l++)
            {
                double start = 0;
                var line = builder._lines[l];
                for (int p = 0; p < line.Paths.Length; p++)
                {
                    var xy = line.Paths[p];
                    double length = 0;
                    for (int i = 2; i < xy.Length; i += 2)
                    {
                        length += Math.Sqrt(Square(xy[i] - xy[i - 2]) + Square(xy[i + 1] - xy[i - 1]));
                    }

                    _parts.Add(new Part(l, xy, start, length));
                    start += length;
                }
            }
        }

        // ---------------------------------------------------------------- joining

        private void JoinEnds()
        {
            // points: the real nodes, then start and end of every part
            int realCount = RealCount, count = realCount + 2 * _parts.Count;
            var x = new double[count];
            var y = new double[count];
            for (int i = 0; i < realCount; i++)
            {
                x[i] = builder._realNodes[i].Location.X;
                y[i] = builder._realNodes[i].Location.Y;
            }

            for (int p = 0; p < _parts.Count; p++)
            {
                var xy = _parts[p].XY;
                x[realCount + 2 * p] = xy[0];
                y[realCount + 2 * p] = xy[1];
                x[realCount + 2 * p + 1] = xy[^2];
                y[realCount + 2 * p + 1] = xy[^1];
            }

            var sets = new UnionFind(count);
            var grid = new PointGrid(_tolerance);
            double tolerance2 = _tolerance * _tolerance;
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (int j in grid.Near(x[i], y[i]))
                {
                    if (Square(x[i] - x[j]) + Square(y[i] - y[j]) <= tolerance2)
                    {
                        sets.Union(i, j);
                    }
                }

                grid.Add(i, x[i], y[i]);
                reporter.Report(BuildPhase.JoiningEnds, i + 1, count);
            }

            reporter.Report(BuildPhase.JoiningEnds, count, count);

            // the groups, in the order of their first point
            var groupOf = new Dictionary<int, List<int>>();
            var order = new List<List<int>>();
            for (int i = 0; i < count; i++)
            {
                int root = sets.Find(i);
                if (!groupOf.TryGetValue(root, out var members))
                {
                    members = [];
                    groupOf[root] = members;
                    order.Add(members);
                }

                members.Add(i);
            }

            for (int i = 0; i < realCount; i++)
            {
                _location.Add(builder._realNodes[i].Location);
                _hasLineEnd.Add(false);
            }

            var nodeOfPoint = new int[count];
            foreach (var members in order)
            {
                var reals = members.Where(m => m < realCount).ToList();
                int node;
                if (reals.Count > 0)
                {
                    node = reals[0];
                    if (reals.Count > 1)
                    {
                        _conflicts.Add(new NodeConflict(_location[node], NodeConflictKind.RealNodesTooClose,
                            reals.Select(r => (object?)builder._realNodes[r].Source).ToList()));
                    }
                }
                else
                {
                    // the mean of the ends, summed in an order of the coordinates, so that it does not depend on the input order
                    var sorted = members.OrderBy(m => x[m]).ThenBy(m => y[m]).ToList();
                    double sx = 0, sy = 0;
                    foreach (int m in sorted)
                    {
                        sx += x[m];
                        sy += y[m];
                    }

                    node = _location.Count;
                    _location.Add(new Coordinate(sx / sorted.Count, sy / sorted.Count));
                    _hasLineEnd.Add(false);
                }

                foreach (int m in members)
                {
                    nodeOfPoint[m] = node;
                    if (m >= realCount)
                    {
                        _hasLineEnd[node] = true;
                    }
                }

                if (Wide(members, x, y, tolerance2))
                {
                    _diagnostics.Add(new BuildDiagnostic(BuildDiagnosticKind.ClusterWiderThanTolerance, _location[node],
                        $"{members.Count} line ends and points were joined into one node over more than the tolerance {_tolerance.ToString(CultureInfo.InvariantCulture)}."));
                }
            }

            _partStart = new int[_parts.Count];
            _partEnd = new int[_parts.Count];
            for (int p = 0; p < _parts.Count; p++)
            {
                _partStart[p] = nodeOfPoint[realCount + 2 * p];
                _partEnd[p] = nodeOfPoint[realCount + 2 * p + 1];
            }
        }

        /// <summary>Whether two members of a group lie further apart than the tolerance (a chain); by the envelope for large groups.</summary>
        private static bool Wide(List<int> members, double[] x, double[] y, double tolerance2)
        {
            if (members.Count > 64)
            {
                double w = members.Max(m => x[m]) - members.Min(m => x[m]), h = members.Max(m => y[m]) - members.Min(m => y[m]);
                return w * w + h * h > tolerance2;
            }

            for (int i = 0; i < members.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (Square(x[members[i]] - x[members[j]]) + Square(y[members[i]] - y[members[j]]) > tolerance2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- splitting

        private void SplitLines()
        {
            _splits = new List<Split>[_parts.Count];
            if (_parts.Count == 0)
            {
                reporter.Report(BuildPhase.Splitting, 0, 0);
                return;
            }

            var index = new SpatialIndex<int>(Enumerable.Range(0, _parts.Count).Select(p => (Grow(_parts[p].Envelope(), _tolerance), p)));
            var candidates = new List<int>();
            var options = builder._options;
            for (int node = 0; node < _location.Count; node++)
            {
                bool real = node < RealCount;
                if ((options.SplitAtNodes && real) || (options.SplitAtLineEnds && _hasLineEnd[node]))
                {
                    candidates.Add(node);
                }
            }

            long total = candidates.Count + (options.NodeAtCrossings ? _parts.Count : 0), done = 0;
            var hits = new List<int>();
            foreach (int node in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var at = _location[node];
                hits.Clear();
                index.Query(new Envelope2D(at.X - _tolerance, at.Y - _tolerance, at.X + _tolerance, at.Y + _tolerance), hits);
                foreach (int p in hits)
                {
                    if (_partStart[p] == node || _partEnd[p] == node)
                    {
                        continue;
                    }

                    var (distance, measure) = _parts[p].Project(at);
                    if (distance <= _tolerance && measure > 0 && measure < _parts[p].Length)
                    {
                        AddSplit(p, measure, node);
                    }
                }

                reporter.Report(BuildPhase.Splitting, ++done, total);
            }

            if (options.NodeAtCrossings)
            {
                SplitAtCrossings(index, ref done, total);
            }

            reporter.Report(BuildPhase.Splitting, total, total);
        }

        private void AddSplit(int part, double measure, int node) => (_splits[part] ??= []).Add(new Split(measure, node));

        private void SplitAtCrossings(SpatialIndex<int> index, ref long done, long total)
        {
            var crossings = new List<(double X, double Y, int P, double MP, int Q, double MQ)>();
            var hits = new List<int>();
            for (int p = 0; p < _parts.Count; p++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hits.Clear();
                index.Query(_parts[p].Envelope(), hits);
                foreach (int q in hits)
                {
                    if (q >= p)
                    {
                        _parts[p].Crossings(_parts[q], p == q, _tolerance, (cx, cy, mp, mq) => crossings.Add((cx, cy, p, mp, q, mq)));
                    }
                }

                reporter.Report(BuildPhase.Splitting, ++done, total);
            }

            // a crossing near an existing node uses it; crossings near each other become one helper node
            var nodes = new PointGrid(_tolerance);
            for (int n = 0; n < _location.Count; n++)
            {
                nodes.Add(n, _location[n].X, _location[n].Y);
            }

            double tolerance2 = _tolerance * _tolerance;
            var sets = new UnionFind(crossings.Count);
            var grid = new PointGrid(_tolerance);
            var existing = new int[crossings.Count];
            for (int c = 0; c < crossings.Count; c++)
            {
                var (cx, cy, _, _, _, _) = crossings[c];
                existing[c] = -1;
                double best = double.PositiveInfinity;
                foreach (int n in nodes.Near(cx, cy))
                {
                    double d = Square(cx - _location[n].X) + Square(cy - _location[n].Y);
                    if (d <= tolerance2 && (d < best || (d == best && n < existing[c])))
                    {
                        best = d;
                        existing[c] = n;
                    }
                }

                if (existing[c] >= 0)
                {
                    continue;
                }

                foreach (int other in grid.Near(cx, cy))
                {
                    if (Square(cx - crossings[other].X) + Square(cy - crossings[other].Y) <= tolerance2)
                    {
                        sets.Union(c, other);
                    }
                }

                grid.Add(c, cx, cy);
            }

            var helperOf = new Dictionary<int, int>();
            foreach (var group in Enumerable.Range(0, crossings.Count).Where(c => existing[c] < 0).GroupBy(sets.Find))
            {
                var sorted = group.OrderBy(c => crossings[c].X).ThenBy(c => crossings[c].Y).ToList();
                int node = _location.Count;
                _location.Add(new Coordinate(sorted.Average(c => crossings[c].X), sorted.Average(c => crossings[c].Y)));
                _hasLineEnd.Add(false);
                helperOf[group.Key] = node;
            }

            for (int c = 0; c < crossings.Count; c++)
            {
                int node = existing[c] >= 0 ? existing[c] : helperOf[sets.Find(c)];
                var (_, _, p, mp, q, mq) = crossings[c];
                if (_partStart[p] != node && _partEnd[p] != node)
                {
                    AddSplit(p, mp, node);
                }

                if (_partStart[q] != node && _partEnd[q] != node)
                {
                    AddSplit(q, mq, node);
                }
            }
        }

        // ---------------------------------------------------------------- edges

        private void PlanEdges()
        {
            int lineOf = -1, firstOfLine = 0;
            for (int p = 0; p < _parts.Count; p++)
            {
                var part = _parts[p];
                if (part.Line != lineOf)
                {
                    lineOf = part.Line;
                    firstOfLine = _edges.Count;
                }

                var stops = new List<Split> { new(0, _partStart[p]) };
                if (_splits[p] is { } splits)
                {
                    // one stop per node and place: splits of the same node within the tolerance (several crossings joined
                    // into one node) keep the smallest measure, so the result does not depend on the order they were found in
                    foreach (var split in splits.OrderBy(s => s.Measure).ThenBy(s => s.Node))
                    {
                        if (!stops.Any(t => t.Node == split.Node && split.Measure - t.Measure <= _tolerance))
                        {
                            stops.Add(split);
                        }
                    }
                }

                stops.Add(new Split(part.Length, _partEnd[p]));
                for (int s = 0; s + 1 < stops.Count; s++)
                {
                    var (a, b) = (stops[s], stops[s + 1]);
                    double length = b.Measure - a.Measure;
                    if (a.Node == b.Node && length < _tolerance)
                    {
                        _diagnostics.Add(new BuildDiagnostic(BuildDiagnosticKind.ShortPartDropped, _location[a.Node],
                            $"A part of line {part.Line} of length {length.ToString(CultureInfo.InvariantCulture)} starts and ends at the same node and was left out."));
                        continue;
                    }

                    _edges.Add(new EdgePlan(part.Line, a.Node, b.Node, part.Start + a.Measure, part.Start + b.Measure, length, -1));
                }

                if (p + 1 == _parts.Count || _parts[p + 1].Line != lineOf)
                {
                    // number the parts of the finished line
                    int partCount = _edges.Count - firstOfLine;
                    for (int e = firstOfLine; e < _edges.Count; e++)
                    {
                        _edges[e] = _edges[e] with { Part = e - firstOfLine, PartCount = partCount };
                    }
                }
            }
        }

        // ---------------------------------------------------------------- ids

        /// <summary>The helper nodes that keep an edge, in the order of their location; real nodes without any edge are reported.</summary>
        private void FindHelpers()
        {
            var degree = new int[_location.Count];
            foreach (var edge in _edges)
            {
                degree[edge.From]++;
                if (edge.To != edge.From)
                {
                    degree[edge.To]++;
                }
            }

            for (int n = 0; n < RealCount; n++)
            {
                if (degree[n] == 0)
                {
                    _diagnostics.Add(new BuildDiagnostic(BuildDiagnosticKind.IsolatedNode, _location[n], $"The node {builder._realNodes[n].Id} has no line."));
                }
            }

            _helpers = Enumerable.Range(RealCount, _location.Count - RealCount)
                .Where(n => degree[n] > 0)
                .OrderBy(n => _location[n].X).ThenBy(n => _location[n].Y)
                .ToArray();
        }

        private TNodeId[] AssignIds()
        {
            var ids = new TNodeId[_location.Count];
            var taken = new Dictionary<TNodeId, int>();
            for (int n = 0; n < RealCount; n++)
            {
                ids[n] = builder._realNodes[n].Id;
                taken[ids[n]] = n;
            }

            foreach (int n in _helpers)
            {
                ids[n] = builder._ids.NewHelperNode(_location[n]);
                if (ids[n] is null)
                {
                    throw new InvalidOperationException("The id factory returned null for a helper node.");
                }

                if (!taken.TryAdd(ids[n], n))
                {
                    _conflicts.Add(new NodeConflict(_location[n], NodeConflictKind.HelperIdTaken, [ids[n], DescribeNode(taken[ids[n]], ids)]));
                }
            }

            reporter.Report(BuildPhase.Checking, 0, _edges.Count);
            var edgeIds = new HashSet<TEdgeId>();
            for (int e = 0; e < _edges.Count; e++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var plan = _edges[e];
                var line = builder._lines[plan.Line];
                var id = builder._ids.NewEdge(line.Source, plan.Part, plan.PartCount)
                    ?? throw new InvalidOperationException("The id factory returned null for an edge.");
                _edges[e] = plan with { Id = id };
                if (!edgeIds.Add(id))
                {
                    _conflicts.Add(new NodeConflict(_location[plan.From], NodeConflictKind.DuplicateEdgeId, [id, line.Source]));
                }

                reporter.Report(BuildPhase.Checking, e + 1, _edges.Count);
            }

            return ids;
        }

        private int[] _helpers = [];

        private object? DescribeNode(int node, TNodeId[] ids) => node < RealCount ? builder._realNodes[node].Source : ids[node];

        // ---------------------------------------------------------------- shards

        private long[] AssignShards()
        {
            var options = builder._options;
            var regions = options.ShardRegions;
            var prepared = regions.Select(r => r.Area.Prepare()).ToArray();
            var index = new SpatialIndex<int>(Enumerable.Range(0, regions.Count).Select(r => (regions[r].Area.Envelope(), r)));

            // the edge types at each helper node, for regions that apply to some types only
            Dictionary<int, HashSet<int>>? helperEdgeTypes = null;
            if (regions.Any(r => r.AppliesTo is not null))
            {
                helperEdgeTypes = [];
                foreach (var edge in _edges)
                {
                    int type = builder._lines[edge.Line].TypeId;
                    foreach (int n in new[] { edge.From, edge.To })
                    {
                        if (n >= RealCount)
                        {
                            (helperEdgeTypes.TryGetValue(n, out var types) ? types : helperEdgeTypes[n] = []).Add(type);
                        }
                    }
                }
            }

            var shard = new long[_location.Count];
            var outside = new Dictionary<long, (Coordinate First, int Count)>();
            var overlaps = new HashSet<(int, int)>();
            var hits = new List<int>();
            var nodes = Enumerable.Range(0, RealCount).Concat(_helpers).ToArray();
            for (int k = 0; k < nodes.Length; k++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int node = nodes[k];
                var at = _location[node];
                hits.Clear();
                index.Query(new Envelope2D(at.X, at.Y, at.X, at.Y), hits);
                hits.Sort();
                int winner = -1;
                foreach (int r in hits)
                {
                    if (!Applies(regions[r], node, helperEdgeTypes) || prepared[r].Locate(at) == Location.Outside)
                    {
                        continue;
                    }

                    if (winner < 0)
                    {
                        winner = r;
                    }
                    else if (overlaps.Add((winner, r)))
                    {
                        _diagnostics.Add(new BuildDiagnostic(BuildDiagnosticKind.OverlappingShardRegions, at,
                            $"The shard regions {winner} (shard {regions[winner].ShardId}) and {r} (shard {regions[r].ShardId}) overlap; the first one wins."));
                    }
                }

                if (winner >= 0)
                {
                    shard[node] = regions[winner].ShardId;
                }
                else
                {
                    shard[node] = options.DefaultShard ?? CellKey(at, options.FallbackShardCellSize);
                    outside[shard[node]] = outside.TryGetValue(shard[node], out var o) ? (o.First, o.Count + 1) : (at, 1);
                }

                reporter.Report(BuildPhase.AssigningShards, k + 1, nodes.Length);
            }

            reporter.Report(BuildPhase.AssigningShards, nodes.Length, nodes.Length);
            foreach (var (key, (first, count)) in outside)
            {
                _diagnostics.Add(new BuildDiagnostic(BuildDiagnosticKind.OutsideShardRegions, first,
                    $"{count} node(s) outside every shard region got the shard {key}."));
            }

            return shard;
        }

        private bool Applies(ShardRegion region, int node, Dictionary<int, HashSet<int>>? helperEdgeTypes)
        {
            if (region.AppliesTo is not { } types)
            {
                return true;
            }

            if (node < RealCount)
            {
                return types.Contains(builder._realNodes[node].TypeId);
            }

            return helperEdgeTypes!.TryGetValue(node, out var edgeTypes) && edgeTypes.Any(types.Contains);
        }

        private static long CellKey(Coordinate at, double size)
        {
            long cx = (long)Math.Floor(at.X / size), cy = (long)Math.Floor(at.Y / size);
            return (cx << 32) ^ (cy & 0xFFFFFFFFL);
        }

        // ---------------------------------------------------------------- writing

        private BuildResult Write(INetworkWriter<TNodeId, TEdgeId, TSource> writer, TNodeId[] ids, long[] shard)
        {
            int nodeCount = RealCount + _helpers.Length;
            for (int n = 0; n < RealCount; n++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var real = builder._realNodes[n];
                writer.WriteNode(new BuiltNode<TNodeId, TSource>(
                    new GraphNode<TNodeId>(real.Id, real.TypeId, real.Location, real.Blocked), shard[n], false, real.Source));
                reporter.Report(BuildPhase.WritingNodes, n + 1, nodeCount);
            }

            for (int h = 0; h < _helpers.Length; h++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int n = _helpers[h];
                writer.WriteNode(new BuiltNode<TNodeId, TSource>(new GraphNode<TNodeId>(ids[n], 0, _location[n]), shard[n], true, default));
                reporter.Report(BuildPhase.WritingNodes, RealCount + h + 1, nodeCount);
            }

            reporter.Report(BuildPhase.WritingNodes, nodeCount, nodeCount);
            for (int e = 0; e < _edges.Count; e++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var plan = _edges[e];
                var line = builder._lines[plan.Line];
                var attributes = line.Attributes;
                double share = line.Length > 0 ? plan.Length / line.Length : 0;
                double length = attributes.Length is { } surveyed ? surveyed * share : plan.Length;
                var edge = new GraphEdge<TNodeId, TEdgeId>(
                    plan.Id!, ids[plan.From], ids[plan.To], line.TypeId, length, attributes.Direction, attributes.Disabled, Weights(attributes, share));
                writer.WriteEdge(new BuiltEdge<TNodeId, TEdgeId, TSource>(edge, line.Source, plan.FromMeasure, plan.ToMeasure, shard[plan.From], shard[plan.To]));
                reporter.Report(BuildPhase.WritingEdges, e + 1, _edges.Count);
            }

            reporter.Report(BuildPhase.WritingEdges, _edges.Count, _edges.Count);
            return new BuildResult(nodeCount, _edges.Count, _diagnostics);
        }

        private ReadOnlyMemory<EdgeWeight> Weights(LineAttributes attributes, double share)
        {
            if (attributes.Weights is not { Count: > 0 } weights)
            {
                return default;
            }

            var result = new EdgeWeight[weights.Count];
            for (int i = 0; i < weights.Count; i++)
            {
                var w = weights[i];
                result[i] = builder._weights[w.WeightId].Kind == WeightKind.Absolute ? w with { Value = w.Value * share } : w;
            }

            return result;
        }

        private static Envelope2D Grow(Envelope2D e, double by) => new(e.MinX - by, e.MinY - by, e.MaxX + by, e.MaxY + by);
    }

    /// <summary>One path of a line: its coordinates as x, y pairs, where it starts on the line and its length.</summary>
    private readonly record struct Part(int Line, double[] XY, double Start, double Length)
    {
        public Envelope2D Envelope()
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            for (int i = 0; i < XY.Length; i += 2)
            {
                minX = Math.Min(minX, XY[i]);
                maxX = Math.Max(maxX, XY[i]);
                minY = Math.Min(minY, XY[i + 1]);
                maxY = Math.Max(maxY, XY[i + 1]);
            }

            return new Envelope2D(minX, minY, maxX, maxY);
        }

        /// <summary>The distance from a point to the part, and the length along the part to the nearest point (the first one on ties).</summary>
        public (double Distance, double Measure) Project(Coordinate at)
        {
            double best = double.PositiveInfinity, measure = 0, along = 0;
            for (int i = 2; i < XY.Length; i += 2)
            {
                double x0 = XY[i - 2], y0 = XY[i - 1], dx = XY[i] - x0, dy = XY[i + 1] - y0;
                double length2 = dx * dx + dy * dy, length = Math.Sqrt(length2);
                double t = length2 > 0 ? Math.Clamp(((at.X - x0) * dx + (at.Y - y0) * dy) / length2, 0, 1) : 0;
                double d = Square(at.X - (x0 + t * dx)) + Square(at.Y - (y0 + t * dy));
                if (d < best)
                {
                    best = d;
                    measure = along + t * length;
                }

                along += length;
            }

            return (Math.Sqrt(best), measure);
        }

        /// <summary>The crossings of this part with another (or with itself), away from the ends of both parts.</summary>
        public void Crossings(Part other, bool self, double tolerance, Action<double, double, double, double> found)
        {
            double along = 0;
            for (int i = 2; i < XY.Length; i += 2)
            {
                double ax = XY[i - 2], ay = XY[i - 1], bx = XY[i], by = XY[i + 1];
                double lengthA = Math.Sqrt(Square(bx - ax) + Square(by - ay));
                double otherAlong = 0;
                for (int j = 2; j < other.XY.Length; j += 2)
                {
                    double cx = other.XY[j - 2], cy = other.XY[j - 1], dx = other.XY[j], dy = other.XY[j + 1];
                    double lengthB = Math.Sqrt(Square(dx - cx) + Square(dy - cy));
                    if (!(self && j <= i + 2) && Intersect(ax, ay, bx, by, cx, cy, dx, dy, out _, out _))
                    {
                        // the same point whichever part comes first: computed on the segments in a fixed order and direction
                        var (x, y) = CrossingPoint(ax, ay, bx, by, cx, cy, dx, dy);
                        double mp = along + Math.Min(lengthA, Math.Sqrt(Square(x - ax) + Square(y - ay)));
                        double mq = otherAlong + Math.Min(lengthB, Math.Sqrt(Square(x - cx) + Square(y - cy)));
                        if (mp > tolerance && mp < Length - tolerance && mq > tolerance && mq < other.Length - tolerance)
                        {
                            found(x, y, mp, mq);
                        }
                    }

                    otherAlong += lengthB;
                }

                along += lengthA;
            }
        }

        private static (double X, double Y) CrossingPoint(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            if (Before(bx, by, ax, ay))
            {
                (ax, ay, bx, by) = (bx, by, ax, ay);
            }

            if (Before(dx, dy, cx, cy))
            {
                (cx, cy, dx, dy) = (dx, dy, cx, cy);
            }

            if (Before(cx, cy, ax, ay) || (cx == ax && cy == ay && Before(dx, dy, bx, by)))
            {
                (ax, ay, bx, by, cx, cy, dx, dy) = (cx, cy, dx, dy, ax, ay, bx, by);
            }

            Intersect(ax, ay, bx, by, cx, cy, dx, dy, out double s, out _);
            return (ax + s * (bx - ax), ay + s * (by - ay));
        }

        private static bool Before(double x0, double y0, double x1, double y1) => x0 < x1 || (x0 == x1 && y0 < y1);

        private static bool Intersect(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy, out double s, out double t)
        {
            double rx = bx - ax, ry = by - ay, qx = dx - cx, qy = dy - cy;
            double denominator = rx * qy - ry * qx;
            s = t = 0;
            if (denominator == 0)
            {
                return false; // parallel or collinear: no single crossing point
            }

            s = ((cx - ax) * qy - (cy - ay) * qx) / denominator;
            t = ((cx - ax) * ry - (cy - ay) * rx) / denominator;
            return s >= 0 && s <= 1 && t >= 0 && t <= 1;
        }
    }

    private readonly record struct Split(double Measure, int Node);

    private readonly record struct EdgePlan(int Line, int From, int To, double FromMeasure, double ToMeasure, double Length, int Part, int PartCount = 0)
    {
        public TEdgeId? Id { get; init; }
    }

    /// <summary>Union-find with path halving and union by size; iterative, so long chains cannot overflow the stack.</summary>
    private sealed class UnionFind
    {
        private readonly int[] _parent, _size;

        public UnionFind(int count)
        {
            _parent = new int[count];
            _size = new int[count];
            for (int i = 0; i < count; i++)
            {
                _parent[i] = i;
                _size[i] = 1;
            }
        }

        public int Find(int i)
        {
            while (_parent[i] != i)
            {
                _parent[i] = _parent[_parent[i]];
                i = _parent[i];
            }

            return i;
        }

        public void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a == b)
            {
                return;
            }

            if (_size[a] < _size[b])
            {
                (a, b) = (b, a);
            }

            _parent[b] = a;
            _size[a] += _size[b];
        }
    }

    /// <summary>A hash grid of points with cells of the tolerance; a point looks only at the 3 × 3 cells around it.</summary>
    private sealed class PointGrid(double cell)
    {
        private readonly Dictionary<(long, long), int> _head = [];
        private readonly List<(int Item, int Next)> _entries = [];

        public void Add(int item, double x, double y)
        {
            var key = Key(x, y);
            _entries.Add((item, _head.TryGetValue(key, out int head) ? head : -1));
            _head[key] = _entries.Count - 1;
        }

        public IEnumerable<int> Near(double x, double y)
        {
            var (cx, cy) = Key(x, y);
            for (long i = cx - 1; i <= cx + 1; i++)
            {
                for (long j = cy - 1; j <= cy + 1; j++)
                {
                    if (_head.TryGetValue((i, j), out int entry))
                    {
                        for (; entry >= 0; entry = _entries[entry].Next)
                        {
                            yield return _entries[entry].Item;
                        }
                    }
                }
            }
        }

        private (long, long) Key(double x, double y) => ((long)Math.Floor(x / cell), (long)Math.Floor(y / cell));
    }
}
