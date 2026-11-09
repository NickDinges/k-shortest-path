using System;
using System.Collections.Generic;

namespace Eppstein;

public sealed partial class Graph
{
    // Query state. A vertex's entries are valid only if its stamp equals the current query stamp,
    // so a new query does not have to reset all vertices (only the vertices it reaches are touched).
    private int queryStamp;
    private int[] stamp = Array.Empty<int>();
    private long[] distance = Array.Empty<long>();
    private int[] treeEdge = Array.Empty<int>();
    private int[] heapStamp = Array.Empty<int>();
    private SidetrackHeapNode?[] sidetrackHeap = Array.Empty<SidetrackHeapNode?>();

    private bool ready;
    private int source = -1;
    private int target = -1;
    private long pathBound;
    private PriorityQueue<Candidate, (long Weight, long Sequence)> candidates = new();
    private long candidateSequence;
    private readonly List<int> pathBuffer = new();
    private readonly List<int> sidetrackBuffer = new();

    /// <summary>
    /// Number of candidate paths created since the last FindShortestPath (for tests and tuning).
    /// </summary>
    internal long CandidatesCreated { get; private set; }

    private long maxDetour = long.MaxValue;
    private double maxDetourRatio = double.PositiveInfinity;

    #region Corridor
    /// <summary>
    /// Maximum detour: only paths at most this much longer than the shortest path are returned.
    /// </summary>
    /// <remarks>
    /// Default <c>long.MaxValue</c> (no limit). With a limit, the shortest path tree is only computed
    /// for vertices whose distance to the goal is at most (shortest path + detour), which makes queries
    /// between nearby vertices of a large graph much faster, and the number of paths is finite.
    /// Changing it requires a new call of FindShortestPath.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long MaxDetour
    {
        get => maxDetour;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            maxDetour = value;
            OnSettingsChanged();
        }
    }

    /// <summary>
    /// Maximum detour relative to the shortest path weight, e.g. 0.2 for paths at most 20% longer.
    /// </summary>
    /// <remarks>
    /// Default <c>double.PositiveInfinity</c> (no limit). If both MaxDetour and MaxDetourRatio are
    /// set, the smaller limit applies. Changing it requires a new call of FindShortestPath.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or NaN.</exception>
    public double MaxDetourRatio
    {
        get => maxDetourRatio;
        set
        {
            if (double.IsNaN(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxDetourRatio must not be negative");
            maxDetourRatio = value;
            OnSettingsChanged();
        }
    }

    private void OnSettingsChanged()
    {
        changeCount++;
        ready = false;
    }

    /// <summary>
    /// Allowed detour for a given shortest path weight.
    /// </summary>
    private long DetourBound(long shortest)
    {
        long bound = maxDetour;
        if (!double.IsPositiveInfinity(maxDetourRatio))
        {
            double relative = Math.Floor(maxDetourRatio * shortest);
            if (relative < bound)
                bound = (long)relative;
        }
        return bound;
    }

    private static long SaturatingAdd(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
    #endregion

    #region Queries
    /// <summary>
    /// Calculates the shortest path tree towards <paramref name="goal"/> and returns the shortest path
    /// from <paramref name="start"/>. Further paths, in order of weight, come from
    /// <see cref="FindNextShortestPath"/>.
    /// </summary>
    /// <param name="start">Label of the start vertex (case-insensitive).</param>
    /// <param name="goal">Label of the goal vertex (case-insensitive).</param>
    /// <returns>The shortest path; empty if there is none, if a label does not exist or if start
    /// and goal are the same vertex.</returns>
    public GraphPath FindShortestPath(string start, string goal)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(goal);
        return FindShortestPath(GetVertexIndex(start), GetVertexIndex(goal));
    }

    /// <summary>
    /// Like <see cref="FindShortestPath(string, string)"/>, with vertices given by index.
    /// </summary>
    /// <returns>The shortest path; empty if there is none or an index is not valid.</returns>
    public GraphPath FindShortestPath(int start, int goal)
    {
        if (!StartQuery(start, goal))
            return GraphPath.Empty;
        return FindNextShortestPath();
    }

    /// <summary>
    /// Returns the next shortest path of the current query.
    /// </summary>
    /// <remarks>
    /// Paths may repeat vertices and edges, so a graph with a cycle between start and goal has
    /// infinitely many paths: limit the number of calls, or set a corridor
    /// (<see cref="MaxDetour"/>, <see cref="MaxDetourRatio"/>).
    /// </remarks>
    /// <returns>The next path; empty if there are no more, or if the graph or the query settings
    /// changed since FindShortestPath.</returns>
    public GraphPath FindNextShortestPath() =>
        TryNextResult(buildPath: true, out GraphPath? path, out _) ? path! : GraphPath.Empty;

    /// <summary>
    /// All paths from <paramref name="start"/> to <paramref name="goal"/> in order of weight, for use
    /// with <c>foreach</c> or LINQ, e.g. <c>graph.EnumeratePaths("S", "T").Take(10)</c>.
    /// </summary>
    /// <remarks>
    /// The query starts when the enumeration starts. The graph keeps one query at a time: changing the
    /// graph or starting another query while enumerating makes the enumeration throw an
    /// <see cref="InvalidOperationException"/>. Without a corridor, a graph with cycles has infinitely
    /// many paths, so limit the enumeration (e.g. with <c>Take</c>).
    /// </remarks>
    public IEnumerable<GraphPath> EnumeratePaths(string start, string goal)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(goal);
        return EnumeratePaths(GetVertexIndex(start), GetVertexIndex(goal));
    }

    /// <summary>
    /// Like <see cref="EnumeratePaths(string, string)"/>, with vertices given by index.
    /// </summary>
    public IEnumerable<GraphPath> EnumeratePaths(int start, int goal)
    {
        foreach ((GraphPath? path, long _) in EnumerateResults(start, goal, buildPaths: true))
            yield return path!;
    }

    /// <summary>
    /// The weights of all paths from <paramref name="start"/> to <paramref name="goal"/> in order,
    /// without building the paths themselves (faster if only the weights are needed).
    /// </summary>
    /// <remarks>Same rules as <see cref="EnumeratePaths(string, string)"/>.</remarks>
    public IEnumerable<long> EnumeratePathWeights(string start, string goal)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(goal);
        return EnumeratePathWeights(GetVertexIndex(start), GetVertexIndex(goal));
    }

    /// <summary>
    /// Like <see cref="EnumeratePathWeights(string, string)"/>, with vertices given by index.
    /// </summary>
    public IEnumerable<long> EnumeratePathWeights(int start, int goal)
    {
        foreach ((GraphPath? _, long weight) in EnumerateResults(start, goal, buildPaths: false))
            yield return weight;
    }

    /// <summary>
    /// Next path of the current query, from Eppstein's enumeration or, with
    /// <see cref="SimplePathsOnly"/>, from Yen's algorithm.
    /// </summary>
    /// <param name="buildPath">False if only the weight is needed (Eppstein then skips building the path).</param>
    /// <param name="path">The path, or null if not built.</param>
    /// <param name="weight">Total weight of the path.</param>
    /// <returns>False if there are no more paths or there is no current query.</returns>
    private bool TryNextResult(bool buildPath, out GraphPath? path, out long weight)
    {
        if (simplePathsOnly)
            return TryNextSimplePath(out path, out weight);

        Candidate? candidate = NextCandidate();
        if (candidate == null)
        {
            path = null;
            weight = 0;
            return false;
        }
        weight = distance[source] + candidate.Weight;
        path = buildPath ? BuildPath(candidate) : null;
        return true;
    }

    private IEnumerable<(GraphPath? Path, long Weight)> EnumerateResults(int start, int goal, bool buildPaths)
    {
        if (!StartQuery(start, goal))
            yield break;
        int query = queryStamp;
        int changes = changeCount;
        while (true)
        {
            if (query != queryStamp || changes != changeCount)
                throw new InvalidOperationException(
                    "The graph was changed or another query was started during the enumeration of paths.");
            if (!TryNextResult(buildPaths, out GraphPath? path, out long weight))
                yield break;
            yield return (path, weight);
        }
    }
    #endregion

    #region Query state accessors (used by Vertex)
    internal long DistanceOf(int vertex) =>
        vertex < stamp.Length && stamp[vertex] == queryStamp && queryStamp != 0 ? distance[vertex] : Vertex.Unreached;

    internal Edge? TreeEdgeOf(int vertex) =>
        DistanceOf(vertex) != Vertex.Unreached && treeEdge[vertex] >= 0 ? new Edge(this, treeEdge[vertex]) : null;

    /// <summary>
    /// True after FindShortestPath while the graph and the settings are unchanged.
    /// </summary>
    private bool HasCurrentQuery => ready;
    #endregion

    #region Algorithm
    /// <summary>
    /// Prepares a query: shortest path tree towards the goal and the first candidate.
    /// </summary>
    /// <returns>False if an index is not valid.</returns>
    private bool StartQuery(int start, int goal)
    {
        ready = false;
        int n = labels.Count;
        if ((uint)start >= (uint)n || (uint)goal >= (uint)n)
            return false;

        EnsureAdjacency();
        EnsureArcFlags();
        if (stamp.Length < n)
        {
            int size = Math.Max(n, stamp.Length * 2);
            Array.Resize(ref stamp, size);
            Array.Resize(ref distance, size);
            Array.Resize(ref treeEdge, size);
            Array.Resize(ref heapStamp, size);
            Array.Resize(ref sidetrackHeap, size);
        }
        if (++queryStamp == int.MaxValue)  // Stamps wrapped around: start over
        {
            Array.Clear(stamp);
            Array.Clear(heapStamp);
            queryStamp = 1;
        }

        source = start;
        target = goal;
        BuildShortestPathTree();
        pathBound = stamp[source] == queryStamp ? DetourBound(distance[source]) : -1;

        candidates.Clear();
        candidateSequence = 0;
        CandidatesCreated = 0;
        // No paths if the goal cannot be reached, or start and goal are the same vertex
        bool anyPath = stamp[source] == queryStamp && source != target;
        if (simplePathsOnly)
            StartSimplePaths(anyPath);
        else if (anyPath)
        {
            candidates.Enqueue(Candidate.Root, (0, candidateSequence++));
            CandidatesCreated = 1;
        }
        ready = true;
        return true;
    }

    /// <summary>
    /// Dijkstra on reversed edges from the goal. Negative edges are ignored. With a maximum detour,
    /// the search stops at distance (shortest path + detour): farther vertices cannot be part of any
    /// returned path.
    /// </summary>
    private void BuildShortestPathTree()
    {
        int query = queryStamp;
        PriorityQueue<int, long> fringe = new();  // edges by distance of their tail to the goal
        int targetRegion = ArcFlagsRegionOf(target);
        long radius = source == target ? DetourBound(0) : long.MaxValue;

        stamp[target] = query;
        distance[target] = 0;
        treeEdge[target] = -1;
        int v = target;
        while (true)
        {
            if (v >= 0)
            {
                long dv = distance[v];
                for (int i = inStart[v]; i < inStart[v + 1]; i++)
                {
                    int e = inEdges[i];
                    if (edgeWeight[e] >= 0 && stamp[edgeTail[e]] != query && HasArcFlag(e, targetRegion))
                        fringe.Enqueue(e, edgeWeight[e] + dv);
                }
            }
            if (!fringe.TryDequeue(out int edge, out long d) || d > radius)
                break;  // No pending edges, or outside of the corridor

            v = edgeTail[edge];
            if (stamp[v] == query)
            {
                v = -1;  // Already settled
                continue;
            }
            stamp[v] = query;
            distance[v] = d;
            treeEdge[v] = edge;
            if (v == source)
                radius = SaturatingAdd(d, DetourBound(d));
        }
    }

    /// <summary>
    /// Extracts the next candidate of the current query and enqueues its successors (Eppstein's path
    /// graph): the two heap children of its last sidetrack (same prefix, next best alternatives for
    /// the last sidetrack) and the best sidetrack after the last one (root of H(head)). Every
    /// sidetrack list is reached exactly once, heap keys never decrease from parent to child and
    /// deltas are non-negative, so candidates come out in order of weight; at most 3 new candidates
    /// per extracted path.
    /// </summary>
    /// <returns>The candidate, null if there are no more or there is no current query.</returns>
    private Candidate? NextCandidate()
    {
        if (!HasCurrentQuery || !candidates.TryDequeue(out Candidate? candidate, out _))
            return null;

        SidetrackHeapNode? last = candidate.Heap;
        if (last == null)  // Shortest path (no sidetracks): best first sidetrack
        {
            EnqueueCandidate(candidate, SidetrackHeapOf(source));
        }
        else
        {
            EnqueueCandidate(candidate.Parent!, last.Left);   // Alternatives for the last sidetrack
            EnqueueCandidate(candidate.Parent!, last.Right);
            EnqueueCandidate(candidate, SidetrackHeapOf(edgeHead[last.Edge]));  // One more sidetrack
        }
        return candidate;
    }

    /// <summary>
    /// Enqueues the parent's sidetracks plus the sidetrack of a heap node, unless it exceeds the
    /// maximum detour.
    /// </summary>
    private void EnqueueCandidate(Candidate parent, SidetrackHeapNode? node)
    {
        if (node == null || parent.Weight + node.Key > pathBound)
            return;
        Candidate child = new(parent, node);
        candidates.Enqueue(child, (child.Weight, candidateSequence++));
        CandidatesCreated++;
    }

    /// <summary>
    /// Returns the heap of all sidetracks on the shortest path tree path from a vertex to the goal,
    /// building it (and the heaps of the following vertices) if needed.
    /// </summary>
    /// <returns>Heap root; null if there are no sidetracks or the vertex cannot reach the goal.</returns>
    private SidetrackHeapNode? SidetrackHeapOf(int vertex)
    {
        int query = queryStamp;
        if (stamp[vertex] != query)
            return null;

        // Vertices on the tree path whose heap is not built yet (iterative, no deep recursion)
        List<int> pending = new();
        for (int x = vertex; x >= 0 && heapStamp[x] != query; x = treeEdge[x] < 0 ? -1 : edgeHead[treeEdge[x]])
            pending.Add(x);

        // Builds heaps from the goal towards the vertex: H(x) = H(next(x)) + sidetracks leaving x
        List<SidetrackHeapNode> own = new();
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            int x = pending[i];
            own.Clear();
            for (int j = outStart[x]; j < outStart[x + 1]; j++)
            {
                int e = outEdges[j];
                int head = edgeHead[e];
                if (e != treeEdge[x] && edgeWeight[e] >= 0 && stamp[head] == query)  // Head must reach the goal
                    own.Add(new SidetrackHeapNode(e, edgeWeight[e] + distance[head] - distance[x]));
            }
            own.Sort(static (a, b) => a.Key.CompareTo(b.Key));

            SidetrackHeapNode? next = treeEdge[x] < 0 ? null : sidetrackHeap[edgeHead[treeEdge[x]]];
            sidetrackHeap[x] = SidetrackHeapNode.Merge(next, SidetrackHeapNode.FromSorted(own));
            heapStamp[x] = query;
        }
        return sidetrackHeap[vertex];
    }

    /// <summary>
    /// Builds the full path of a candidate: from the start along the shortest path tree, crossing its
    /// sidetracks in order.
    /// </summary>
    private GraphPath BuildPath(Candidate candidate)
    {
        sidetrackBuffer.Clear();
        for (Candidate? c = candidate; c != null && c.Heap != null; c = c.Parent)
            sidetrackBuffer.Add(c.Heap.Edge);
        sidetrackBuffer.Reverse();

        pathBuffer.Clear();
        long weight = 0;
        int v = source;
        int next = 0;
        while (true)
        {
            int e;
            if (next < sidetrackBuffer.Count && edgeTail[sidetrackBuffer[next]] == v)
                e = sidetrackBuffer[next++];  // Cross the next sidetrack
            else if (treeEdge[v] >= 0 && v != target)
                e = treeEdge[v];  // Follow the shortest path tree
            else if (v == target && next < sidetrackBuffer.Count)
                throw new InvalidOperationException("Sidetrack not on the path (internal error)");
            else
                break;
            pathBuffer.Add(e);
            weight += edgeWeight[e];
            v = edgeHead[e];
        }
        return new GraphPath(this, pathBuffer.ToArray(), weight);
    }
    #endregion
}
