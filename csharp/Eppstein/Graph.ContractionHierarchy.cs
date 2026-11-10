using System;
using System.Collections.Generic;

namespace Eppstein;

/// <summary>
/// Contraction hierarchy (CH) with PHAST: optional speed-up of the shortest-path tree computation.
/// </summary>
/// <remarks>
/// <para>
/// Preprocessing contracts the vertices one after the other, from unimportant to important. When a
/// vertex v is removed, a shortcut u→x with weight w(u,v) + w(v,x) is added for each pair of
/// neighbors whose shortest connection ran through v (a local "witness search" checks this). The
/// position in this order is the vertex's rank. Every shortest path then has an equivalent path
/// in the graph with shortcuts that first only goes up in rank and then only down.
/// </para>
/// <para>
/// PHAST (Delling, Goldberg, Nowatzyk, Werneck) computes the distances of all vertices to the goal
/// from that: a small search from the goal over edges to higher-ranked vertices, followed by one
/// linear sweep over all vertices from the highest to the lowest rank, without a priority queue.
/// The shortest-path tree is then built from the original edges whose weight matches the
/// distances. Results are exactly the same as with Dijkstra (paths of equal weight may come in a
/// different order).
/// </para>
/// </remarks>
public sealed partial class Graph
{
    /// <summary>Settled-vertex limit of a witness search while choosing the order.</summary>
    private const int WitnessLimitOrdering = 50;

    /// <summary>Settled-vertex limit of a witness search while contracting.</summary>
    private const int WitnessLimitContraction = 500;

    private bool chEnabled;
    private bool chValid;
    private int chShortcutCount;

    // Hierarchy, valid if chValid. Vertices are stored by sweep position p = n - 1 - rank, so the
    // sweep runs through the arrays in order.
    private int[] chPosition = Array.Empty<int>();         // Vertex -> sweep position
    private int[] chVertexAt = Array.Empty<int>();         // Sweep position -> vertex
    private int[] chUpStart = new int[1];                  // Edges to higher-ranked vertices, by position
    private int[] chUpTarget = Array.Empty<int>();         // ... as sweep position of the head
    private long[] chUpWeight = Array.Empty<long>();
    private int[] chDownStart = new int[1];                // Edges from higher-ranked vertices, by vertex
    private int[] chDownSource = Array.Empty<int>();       // ... their tails (vertex ids)
    private long[] chDownWeight = Array.Empty<long>();
    private long[] chDistance = Array.Empty<long>();       // By sweep position, reused per query

    /// <summary>
    /// True if the contraction hierarchy is enabled.
    /// </summary>
    public bool ContractionHierarchyEnabled => chEnabled;

    /// <summary>
    /// Number of shortcut edges of the contraction hierarchy (0 if it is not computed).
    /// </summary>
    public int ContractionHierarchyShortcuts => chValid ? chShortcutCount : 0;

    /// <summary>
    /// Enables the contraction hierarchy: the shortest-path tree of a query (all distances to the
    /// goal) is computed with PHAST instead of Dijkstra.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Worthwhile for many queries <b>without</b> a corridor on large graphs, where computing the
    /// distances of all vertices dominates. With a corridor (<see cref="MaxDetour"/>,
    /// <see cref="MaxDetourRatio"/>) the library keeps using Dijkstra, because it then only visits
    /// the vertices inside the corridor, while PHAST always visits all vertices.
    /// </para>
    /// <para>
    /// The preprocessing runs on the next query (or with <see cref="PrecomputeContractionHierarchy"/>)
    /// and again after every change of the graph, including weight changes. Results are exactly the
    /// same as without it. Arc flags are not used while the contraction hierarchy is enabled.
    /// </para>
    /// </remarks>
    public void EnableContractionHierarchy()
    {
        chEnabled = true;
        ready = false;
    }

    /// <summary>
    /// Disables the contraction hierarchy and releases its memory.
    /// </summary>
    public void DisableContractionHierarchy()
    {
        chEnabled = false;
        chValid = false;
        chShortcutCount = 0;
        chPosition = chVertexAt = chUpTarget = chDownSource = Array.Empty<int>();
        chUpStart = chDownStart = new int[1];
        chUpWeight = chDownWeight = chDistance = Array.Empty<long>();
        ready = false;
    }

    /// <summary>
    /// Runs the contraction now (otherwise it runs on the next query).
    /// </summary>
    /// <returns>True if the hierarchy is available, false if it is disabled.</returns>
    public bool PrecomputeContractionHierarchy()
    {
        EnsureContractionHierarchy();
        return chEnabled && chValid;
    }

    /// <summary>
    /// True if the next shortest-path tree should be computed with PHAST.
    /// </summary>
    private bool UsePhast => chEnabled && maxDetour == long.MaxValue && double.IsPositiveInfinity(maxDetourRatio);

    private void EnsureContractionHierarchy()
    {
        if (!chEnabled || chValid)
            return;
        Contract();
        chValid = true;
    }

    #region Contraction
    /// <summary>
    /// Builds the contraction hierarchy: chooses the order lazily by priority (edge difference plus
    /// contracted neighbors) and adds the shortcuts.
    /// </summary>
    private void Contract()
    {
        int n = labels.Count;
        ContractionState state = new(this, n);
        chShortcutCount = 0;
        PriorityQueue<int, (long Priority, int Vertex)> queue = new();
        for (int v = 0; v < n; v++)
            queue.Enqueue(v, (state.Priority(v), v));

        int[] rank = new int[n];
        List<(int Head, long Weight)>[] up = new List<(int, long)>[n];
        List<(int Tail, long Weight)>[] down = new List<(int, long)>[n];
        int next = 0;
        List<(int Tail, int Head, long Weight)> shortcuts = new();
        while (queue.TryDequeue(out int v, out _))
        {
            if (state.Contracted[v])
                continue;
            long priority = state.Priority(v);  // Lazy update: re-check before contracting
            if (queue.TryPeek(out _, out var top) && (priority, v).CompareTo(top) > 0)
            {
                queue.Enqueue(v, (priority, v));
                continue;
            }

            shortcuts.Clear();
            state.FindShortcuts(v, WitnessLimitContraction, shortcuts);
            foreach (var (tail, head, weight) in shortcuts)
                state.AddEdge(tail, head, weight);
            chShortcutCount += shortcuts.Count;

            // The remaining neighbors all get a higher rank: these are v's upward edges
            up[v] = state.LiveOut(v);
            down[v] = state.LiveIn(v);
            state.Contracted[v] = true;
            rank[v] = next++;
            foreach (var (head, _) in up[v])
                state.ContractedNeighbors[head]++;
            foreach (var (tail, _) in down[v])
                state.ContractedNeighbors[tail]++;
        }

        // Store by sweep position: highest rank first
        chPosition = new int[n];
        chVertexAt = new int[n];
        for (int v = 0; v < n; v++)
        {
            chPosition[v] = n - 1 - rank[v];
            chVertexAt[chPosition[v]] = v;
        }
        chUpStart = new int[n + 1];
        chDownStart = new int[n + 1];
        for (int p = 0; p < n; p++)
            chUpStart[p + 1] = chUpStart[p] + up[chVertexAt[p]].Count;
        for (int v = 0; v < n; v++)
            chDownStart[v + 1] = chDownStart[v] + down[v].Count;
        chUpTarget = new int[chUpStart[n]];
        chUpWeight = new long[chUpStart[n]];
        chDownSource = new int[chDownStart[n]];
        chDownWeight = new long[chDownStart[n]];
        for (int p = 0; p < n; p++)
        {
            int i = chUpStart[p];
            foreach (var (head, weight) in up[chVertexAt[p]])
            {
                chUpTarget[i] = chPosition[head];
                chUpWeight[i++] = weight;
            }
        }
        for (int v = 0; v < n; v++)
        {
            int i = chDownStart[v];
            foreach (var (tail, weight) in down[v])
            {
                chDownSource[i] = tail;
                chDownWeight[i++] = weight;
            }
        }
        chDistance = new long[n];
    }

    /// <summary>
    /// The graph during contraction: adjacency lists over the vertices not yet contracted (lists
    /// may contain contracted vertices, which are skipped), and witness searches.
    /// </summary>
    private sealed class ContractionState
    {
        public readonly bool[] Contracted;
        public readonly int[] ContractedNeighbors;
        private readonly List<(int Head, long Weight)>[] outEdges;
        private readonly List<(int Tail, long Weight)>[] inEdges;

        // Witness search
        private readonly long[] distance;
        private readonly int[] seen;
        private int search;
        private readonly PriorityQueue<int, long> queue = new();
        private readonly List<(int Tail, int Head, long Weight)> scratch = new();

        public ContractionState(Graph g, int n)
        {
            Contracted = new bool[n];
            ContractedNeighbors = new int[n];
            outEdges = new List<(int, long)>[n];
            inEdges = new List<(int, long)>[n];
            for (int v = 0; v < n; v++)
            {
                outEdges[v] = new List<(int, long)>();
                inEdges[v] = new List<(int, long)>();
            }
            for (int e = 0; e < g.edgeCount; e++)
            {
                if (g.edgeWeight[e] >= 0 && g.edgeTail[e] != g.edgeHead[e])  // Negative: ignored; self-loops: never needed
                    AddEdge(g.edgeTail[e], g.edgeHead[e], g.edgeWeight[e]);
            }
            distance = new long[n];
            seen = new int[n];
        }

        /// <summary>Adds an edge, or lowers the weight of an existing parallel edge.</summary>
        public void AddEdge(int tail, int head, long weight)
        {
            List<(int Head, long Weight)> outs = outEdges[tail];
            for (int i = 0; i < outs.Count; i++)
            {
                if (outs[i].Head == head)
                {
                    if (weight < outs[i].Weight)
                    {
                        outs[i] = (head, weight);
                        List<(int Tail, long Weight)> ins = inEdges[head];
                        for (int j = 0; j < ins.Count; j++)
                        {
                            if (ins[j].Tail == tail)
                                ins[j] = (tail, weight);
                        }
                    }
                    return;
                }
            }
            outs.Add((head, weight));
            inEdges[head].Add((tail, weight));
        }

        public List<(int, long)> LiveOut(int v) => outEdges[v].FindAll(e => !Contracted[e.Head]);

        public List<(int, long)> LiveIn(int v) => inEdges[v].FindAll(e => !Contracted[e.Tail]);

        /// <summary>
        /// Priority for the order: shortcuts needed minus edges removed (edge difference), plus the
        /// number of contracted neighbors (spreads the contraction evenly).
        /// </summary>
        public long Priority(int v)
        {
            scratch.Clear();
            FindShortcuts(v, WitnessLimitOrdering, scratch);
            int removed = 0;
            foreach (var (head, _) in outEdges[v])
                if (!Contracted[head]) removed++;
            foreach (var (tail, _) in inEdges[v])
                if (!Contracted[tail]) removed++;
            return 2L * scratch.Count - removed + ContractedNeighbors[v];
        }

        /// <summary>
        /// Finds the shortcuts needed when v is contracted: for every live in-neighbor u and
        /// out-neighbor x, a shortcut u→x unless a witness search from u (avoiding v) finds a path
        /// of at most w(u,v) + w(v,x). An incomplete search (settled limit) adds the shortcut, which
        /// is always correct, only not minimal.
        /// </summary>
        public void FindShortcuts(int v, int settledLimit, List<(int Tail, int Head, long Weight)> result)
        {
            foreach (var (u, inWeight) in inEdges[v])
            {
                if (Contracted[u])
                    continue;
                long maxTotal = 0;
                foreach (var (x, outWeight) in outEdges[v])
                {
                    if (!Contracted[x] && x != u)
                        maxTotal = Math.Max(maxTotal, SaturatingAdd(inWeight, outWeight));
                }
                if (maxTotal == 0 && !HasLiveTarget(v, u))
                    continue;

                WitnessSearch(u, v, maxTotal, settledLimit);
                foreach (var (x, outWeight) in outEdges[v])
                {
                    if (Contracted[x] || x == u)
                        continue;
                    long viaV = SaturatingAdd(inWeight, outWeight);
                    if (seen[x] != search || distance[x] > viaV)
                        result.Add((u, x, viaV));
                }
            }
        }

        private bool HasLiveTarget(int v, int u)
        {
            foreach (var (x, _) in outEdges[v])
            {
                if (!Contracted[x] && x != u)
                    return true;
            }
            return false;
        }

        /// <summary>Dijkstra from source over live vertices except the one being contracted.</summary>
        private void WitnessSearch(int source, int avoid, long maxDistance, int settledLimit)
        {
            if (++search == int.MaxValue)
            {
                Array.Clear(seen);
                search = 1;
            }
            queue.Clear();
            seen[source] = search;
            distance[source] = 0;
            queue.Enqueue(source, 0);
            int settled = 0;
            while (queue.TryDequeue(out int v, out long d))
            {
                if (d > distance[v])
                    continue;  // Outdated entry
                if (d > maxDistance || ++settled > settledLimit)
                    break;
                foreach (var (x, w) in outEdges[v])
                {
                    if (Contracted[x] || x == avoid)
                        continue;
                    long nd = SaturatingAdd(d, w);
                    if (seen[x] != search || nd < distance[x])
                    {
                        seen[x] = search;
                        distance[x] = nd;
                        queue.Enqueue(x, nd);
                    }
                }
            }
        }
    }
    #endregion

    #region PHAST query
    /// <summary>
    /// PHAST towards the goal: upward search from the goal over edges coming from higher-ranked
    /// vertices, then one sweep from the highest to the lowest rank. Afterwards every vertex that can
    /// reach the goal has its exact distance; the tree is built from tight original edges.
    /// </summary>
    private void BuildShortestPathTreePhast()
    {
        int n = labels.Count;
        int query = queryStamp;
        long[] d = chDistance;
        Array.Fill(d, long.MaxValue);

        // 1. Upward search from the goal (in reverse direction: edges x→y with rank(x) > rank(y))
        PriorityQueue<int, long> fringe = new();
        d[chPosition[target]] = 0;
        fringe.Enqueue(target, 0);
        while (fringe.TryDequeue(out int y, out long dy))
        {
            if (dy > d[chPosition[y]])
                continue;
            for (int i = chDownStart[y]; i < chDownStart[y + 1]; i++)
            {
                int x = chDownSource[i];
                long nd = SaturatingAdd(dy, chDownWeight[i]);
                if (nd < d[chPosition[x]])
                {
                    d[chPosition[x]] = nd;
                    fringe.Enqueue(x, nd);
                }
            }
        }

        // 2. Sweep: highest rank first; the heads of upward edges are already final
        for (int p = 0; p < n; p++)
        {
            long best = d[p];
            for (int i = chUpStart[p]; i < chUpStart[p + 1]; i++)
            {
                long dh = d[chUpTarget[i]];
                if (dh != long.MaxValue && SaturatingAdd(dh, chUpWeight[i]) < best)
                    best = SaturatingAdd(dh, chUpWeight[i]);
            }
            d[p] = best;
        }

        // 3. Tree from tight original edges, breadth-first from the goal (so zero-weight cycles
        //    cannot make it cyclic). Every vertex with a finite distance has a tight path to the goal.
        Queue<int> bfs = new();
        stamp[target] = query;
        distance[target] = 0;
        treeEdge[target] = -1;
        bfs.Enqueue(target);
        while (bfs.TryDequeue(out int y))
        {
            long dy = distance[y];
            for (int i = inStart[y]; i < inStart[y + 1]; i++)
            {
                int e = inEdges[i];
                int x = edgeTail[e];
                if (stamp[x] == query || edgeWeight[e] < 0)
                    continue;
                long dx = d[chPosition[x]];
                if (dx != long.MaxValue && dx == SaturatingAdd(edgeWeight[e], dy))
                {
                    stamp[x] = query;
                    distance[x] = dx;
                    treeEdge[x] = e;
                    bfs.Enqueue(x);
                }
            }
        }
    }
    #endregion
}
