using System;
using System.Collections.Generic;

namespace Eppstein;

/// <summary>
/// k shortest simple paths (no repeated vertices): Yen's algorithm with Lawler's improvement,
/// A* searches guided by the exact distances to the goal, and the detour corridor.
/// </summary>
/// <remarks>
/// <para>
/// The paths are found one after the other. Every new path is the best one among the candidates;
/// a candidate is a path found so far up to some vertex (the root), followed by the best way from
/// there to the goal that leaves the root along an edge no found path with the same root uses (the
/// spur). Lawler: a path only needs spurs from the vertex where it deviated from the path it was
/// derived from; the spurs before that vertex were already tried for that earlier path.
/// </para>
/// <para>
/// Each spur is a shortest-path search with some vertices and edges blocked. Blocking only makes
/// distances longer, so the distances to the goal from the shortest path tree are exact lower
/// bounds: the searches run as A*, which goes straight towards the goal. With a corridor, a search
/// stops as soon as no path within the limit is possible.
/// </para>
/// </remarks>
public sealed partial class Graph
{
    private bool simplePathsOnly;

    // State of the current query in simple mode
    private readonly PriorityQueue<SimpleCandidate, (long Weight, long Sequence)> simpleCandidates = new();
    private readonly HashSet<int[]> simpleSeen = new(EdgeSequenceComparer.Instance);
    private PathTrieNode simpleTrie = new();
    private SimpleCandidate? simplePending;  // Last returned path whose spurs are not generated yet
    private long simpleMaxWeight;            // Largest allowed path weight (corridor)
    private long simpleSequence;

    // A* search state, valid where the stamp matches the current search
    private int spurStamp;
    private int[] spurOpenStamp = Array.Empty<int>();
    private int[] spurClosedStamp = Array.Empty<int>();
    private long[] spurCost = Array.Empty<long>();
    private int[] spurParent = Array.Empty<int>();
    private int pathStamp;
    private int[] onPathStamp = Array.Empty<int>();
    private int[] onPathPosition = Array.Empty<int>();
    private readonly PriorityQueue<int, long> spurOpen = new();

    /// <summary>
    /// Number of spur searches since the last FindShortestPath (for tests and tuning).
    /// </summary>
    internal long SpurSearches { get; private set; }

    /// <summary>
    /// Only return simple paths, i.e. paths that visit no vertex twice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By default (false) the k shortest paths may repeat vertices and edges (Eppstein's algorithm).
    /// In networks with edges in both directions, such as road networks, most of these are the
    /// shortest path with a loop added (drive A→B→A), not real alternatives. With true, the paths are
    /// the k shortest simple paths (Yen's algorithm with Lawler's improvement).
    /// </para>
    /// <para>
    /// Simple paths cost more: every returned path needs a few shortest-path searches (guided by the
    /// distances to the goal, and limited by <see cref="MaxDetour"/>/<see cref="MaxDetourRatio"/>),
    /// while Eppstein needs one search in total. The number of simple paths is finite, so an
    /// enumeration always ends. <see cref="EnumeratePathWeights(string, string)"/> is not faster
    /// than <see cref="EnumeratePaths(string, string)"/> in this mode. Arc flags only speed up the
    /// first search. Changing it requires a new call of FindShortestPath.
    /// </para>
    /// </remarks>
    public bool SimplePathsOnly
    {
        get => simplePathsOnly;
        set
        {
            simplePathsOnly = value;
            OnSettingsChanged();
        }
    }

    /// <summary>
    /// Prepares a simple-paths query after the shortest path tree has been built: the first
    /// candidate is the shortest path itself.
    /// </summary>
    private void StartSimplePaths(bool anyPath)
    {
        simpleCandidates.Clear();
        simpleSeen.Clear();
        simpleTrie = new PathTrieNode();
        simplePending = null;
        simpleSequence = 0;
        SpurSearches = 0;
        if (!anyPath)
            return;

        int n = labels.Count;
        if (spurOpenStamp.Length < n)
        {
            spurOpenStamp = new int[n];
            spurClosedStamp = new int[n];
            spurCost = new long[n];
            spurParent = new int[n];
            onPathStamp = new int[n];
            onPathPosition = new int[n];
            spurStamp = pathStamp = 0;
        }

        // The shortest path follows the tree; it is simple because tree paths are
        List<int> edges = new();
        for (int v = source; v != target; v = edgeHead[treeEdge[v]])
            edges.Add(treeEdge[v]);
        simpleMaxWeight = SaturatingAdd(distance[source], pathBound);
        AddSimpleCandidate(edges.ToArray(), distance[source], deviation: 0);
    }

    /// <summary>
    /// Returns the next simple path: first generates the spurs of the path returned before (lazily,
    /// so asking for one path costs no spur searches), then takes the best candidate.
    /// </summary>
    private bool TryNextSimplePath(out GraphPath? path, out long weight)
    {
        path = null;
        weight = 0;
        if (!ready)
            return false;
        if (simplePending != null)
        {
            GenerateSpurs(simplePending);
            simplePending = null;
        }
        if (!simpleCandidates.TryDequeue(out SimpleCandidate? next, out _))
            return false;

        // Found paths form a trie: the children of a root are the edges to block at its end
        PathTrieNode node = simpleTrie;
        foreach (int e in next.Edges)
            node = node.Child(e);

        simplePending = next;
        weight = next.Weight;
        path = new GraphPath(this, next.Edges, next.Weight);
        return true;
    }

    /// <summary>
    /// Yen with Lawler's improvement: for every vertex of the path from its deviation vertex on, finds
    /// the best way to the goal that keeps the path up to that vertex (the root), avoids the root's
    /// vertices and leaves along an edge no found path with the same root uses.
    /// </summary>
    private void GenerateSpurs(SimpleCandidate path)
    {
        int[] edges = path.Edges;

        // Positions of the path's vertices: vertex i is blocked for spurs at positions after i
        if (++pathStamp == int.MaxValue)
        {
            Array.Clear(onPathStamp);
            pathStamp = 1;
        }
        int v = source;
        for (int i = 0; ; i++)
        {
            onPathStamp[v] = pathStamp;
            onPathPosition[v] = i;
            if (i == edges.Length)
                break;
            v = edgeHead[edges[i]];
        }

        PathTrieNode node = simpleTrie;
        long rootWeight = 0;
        v = source;
        for (int i = 0; i < edges.Length; i++)
        {
            if (i >= path.Deviation)
            {
                int[]? spur = SpurSearch(v, i, node, simpleMaxWeight - rootWeight, out long spurWeight);
                if (spur != null)
                {
                    int[] candidate = new int[i + spur.Length];
                    Array.Copy(edges, candidate, i);
                    Array.Copy(spur, 0, candidate, i, spur.Length);
                    AddSimpleCandidate(candidate, rootWeight + spurWeight, deviation: i);
                }
            }
            rootWeight += edgeWeight[edges[i]];
            node = node.Child(edges[i]);
            v = edgeHead[edges[i]];
        }
    }

    private void AddSimpleCandidate(int[] edges, long weight, int deviation)
    {
        if (!simpleSeen.Add(edges))
            return;  // Same path reached from another root
        simpleCandidates.Enqueue(new SimpleCandidate(edges, weight, deviation), (weight, simpleSequence++));
    }

    /// <summary>
    /// A* search from a spur vertex to the goal, with the root's vertices and the trie children's
    /// edges blocked, guided by the exact distances to the goal (a consistent lower bound).
    /// </summary>
    /// <param name="spurVertex">Start of the search: vertex at position <paramref name="position"/> of the path.</param>
    /// <param name="position">Position of the spur vertex; path vertices before it are blocked.</param>
    /// <param name="root">Trie node of the root; its children are the blocked first edges.</param>
    /// <param name="maxCost">Largest allowed weight of the spur (corridor).</param>
    /// <param name="cost">Weight of the found spur.</param>
    /// <returns>Edges of the spur, null if there is none within the limit.</returns>
    private int[]? SpurSearch(int spurVertex, int position, PathTrieNode root, long maxCost, out long cost)
    {
        cost = 0;
        int query = queryStamp;
        if (maxCost < 0 || stamp[spurVertex] != query || distance[spurVertex] > maxCost)
            return null;
        SpurSearches++;
        if (++spurStamp == int.MaxValue)
        {
            Array.Clear(spurOpenStamp);
            Array.Clear(spurClosedStamp);
            spurStamp = 1;
        }
        int search = spurStamp;

        spurOpen.Clear();
        spurCost[spurVertex] = 0;
        spurParent[spurVertex] = -1;
        spurOpenStamp[spurVertex] = search;
        spurOpen.Enqueue(spurVertex, distance[spurVertex]);
        while (spurOpen.TryDequeue(out int v, out long estimate))
        {
            if (spurClosedStamp[v] == search)
                continue;
            if (estimate > maxCost)
                return null;  // Consistent bound: every remaining path is longer
            spurClosedStamp[v] = search;
            if (v == target)
            {
                cost = spurCost[v];
                List<int> spur = new();
                for (int x = v; spurParent[x] >= 0; x = edgeTail[spurParent[x]])
                    spur.Add(spurParent[x]);
                spur.Reverse();
                return spur.ToArray();
            }

            for (int i = outStart[v]; i < outStart[v + 1]; i++)
            {
                int e = outEdges[i];
                int head = edgeHead[e];
                if (edgeWeight[e] < 0 || stamp[head] != query || spurClosedStamp[head] == search)
                    continue;  // Negative, cannot reach the goal (or outside the corridor), or done
                if (v == spurVertex && root.HasChild(e))
                    continue;  // A found path with the same root leaves along this edge
                if (onPathStamp[head] == pathStamp && onPathPosition[head] < position)
                    continue;  // Vertex of the root: the path would not be simple
                long newCost = spurCost[v] + edgeWeight[e];
                if (spurOpenStamp[head] == search && spurCost[head] <= newCost)
                    continue;
                spurOpenStamp[head] = search;
                spurCost[head] = newCost;
                spurParent[head] = e;
                spurOpen.Enqueue(head, newCost + distance[head]);
            }
        }
        return null;
    }

    /// <summary>A candidate simple path and the position where it deviated from its parent.</summary>
    private sealed class SimpleCandidate
    {
        public readonly int[] Edges;
        public readonly long Weight;
        public readonly int Deviation;

        public SimpleCandidate(int[] edges, long weight, int deviation)
        {
            Edges = edges;
            Weight = weight;
            Deviation = deviation;
        }
    }

    /// <summary>Trie of the found paths by edge; the children of a node are the edges to block.</summary>
    private sealed class PathTrieNode
    {
        private Dictionary<int, PathTrieNode>? children;

        public PathTrieNode Child(int edge)
        {
            children ??= new Dictionary<int, PathTrieNode>();
            if (!children.TryGetValue(edge, out PathTrieNode? child))
                children[edge] = child = new PathTrieNode();
            return child;
        }

        public bool HasChild(int edge) => children != null && children.ContainsKey(edge);
    }

    /// <summary>Compares edge sequences by content (to recognise a candidate found twice).</summary>
    private sealed class EdgeSequenceComparer : IEqualityComparer<int[]>
    {
        public static readonly EdgeSequenceComparer Instance = new();

        public bool Equals(int[]? x, int[]? y) =>
            ReferenceEquals(x, y) || (x != null && y != null && x.AsSpan().SequenceEqual(y));

        public int GetHashCode(int[] obj)
        {
            HashCode hash = new();
            foreach (int e in obj)
                hash.Add(e);
            return hash.ToHashCode();
        }
    }
}
