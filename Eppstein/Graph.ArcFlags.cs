using System;
using System.Collections.Generic;

namespace Eppstein;

/// <summary>
/// Arc flags: optional speed-up of the shortest path tree computation for many queries.
/// </summary>
/// <remarks>
/// The vertices are partitioned into regions. During preprocessing every edge (u,v) gets one flag per
/// region r, set if the edge lies on a shortest path from u to some vertex of r. The search towards a
/// goal then only uses edges flagged for the goal's region. Every shortest path u→goal only uses
/// such edges, so all distances (and therefore all results) stay exact. Flags are recomputed lazily
/// on the next query after the graph has been changed.
/// </remarks>
public sealed partial class Graph
{
    private bool arcFlagsEnabled;
    private bool arcFlagsValid;
    private int regionCount;
    private int autoRegionCount;
    private string? manualRegions;
    private int[] vertexRegion = Array.Empty<int>();
    private bool[] arcFlags = Array.Empty<bool>();  // flag of edge e for region r: arcFlags[e * regionCount + r]

    /// <summary>
    /// True if the arc-flags optimization is enabled.
    /// </summary>
    public bool ArcFlagsEnabled => arcFlagsEnabled;

    /// <summary>
    /// Number of arc-flags regions, 0 if disabled. Runs pending preprocessing if needed.
    /// </summary>
    public int ArcFlagsRegionCount
    {
        get
        {
            EnsureArcFlags();
            return arcFlagsEnabled ? regionCount : 0;
        }
    }

    /// <summary>
    /// Enables arc flags with an automatic partition of the vertices into regions (connected chunks of
    /// a breadth-first ordering).
    /// </summary>
    /// <param name="regionCount">Desired number of regions, at least 1.</param>
    /// <returns>True if enabled, false if <paramref name="regionCount"/> is less than 1.</returns>
    public bool EnableArcFlags(int regionCount)
    {
        if (regionCount < 1)
            return false;
        autoRegionCount = regionCount;
        manualRegions = null;
        arcFlagsEnabled = true;
        InvalidateArcFlags();
        ready = false;
        return true;
    }

    /// <summary>
    /// Enables arc flags with a given partition of the vertices into regions.
    /// </summary>
    /// <param name="regions">Semicolon-separated regions, each a comma-separated list of labels, e.g.
    /// <c>"S,A,B,C;D,E,F"</c>. Vertices not listed form one additional region.</param>
    /// <returns>True if enabled, false if a label does not exist or is listed twice.</returns>
    public bool EnableArcFlags(string regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (regions.Length == 0 || ParsePartition(regions) == null)
            return false;
        manualRegions = regions;
        autoRegionCount = 0;
        arcFlagsEnabled = true;
        InvalidateArcFlags();
        ready = false;
        return true;
    }

    /// <summary>
    /// Disables arc flags and releases their memory.
    /// </summary>
    public void DisableArcFlags()
    {
        arcFlagsEnabled = false;
        autoRegionCount = 0;
        manualRegions = null;
        arcFlagsValid = false;
        regionCount = 0;
        vertexRegion = Array.Empty<int>();
        arcFlags = Array.Empty<bool>();
        ready = false;
    }

    /// <summary>
    /// Runs the arc-flags preprocessing now (otherwise it runs on the next query).
    /// </summary>
    /// <returns>True if flags are available, false if arc flags are disabled.</returns>
    public bool PrecomputeArcFlags()
    {
        EnsureArcFlags();
        return arcFlagsEnabled && arcFlagsValid;
    }

    internal bool HasArcFlag(int edge, int region) =>
        region < 0 || !arcFlagsValid || region >= regionCount || arcFlags[edge * regionCount + region];

    internal int RegionOf(int vertex) => arcFlagsValid && vertex < vertexRegion.Length ? vertexRegion[vertex] : -1;

    /// <summary>
    /// Region used to prune edges for a goal, -1 if arc flags are not used.
    /// </summary>
    private int ArcFlagsRegionOf(int vertex) => arcFlagsEnabled ? RegionOf(vertex) : -1;

    private void InvalidateArcFlags() => arcFlagsValid = false;

    /// <summary>
    /// Recomputes partition and flags if enabled and outdated.
    /// </summary>
    private void EnsureArcFlags()
    {
        if (!arcFlagsEnabled || arcFlagsValid)
            return;
        EnsureAdjacency();
        int[]? partition = manualRegions != null ? ParsePartition(manualRegions) : AutomaticPartition(autoRegionCount);
        if (partition == null)
        {
            DisableArcFlags();  // Partition no longer valid (vertices were recreated)
            return;
        }
        vertexRegion = partition;
        regionCount = 0;
        foreach (int r in partition)
            regionCount = Math.Max(regionCount, r + 1);
        ComputeArcFlags();
        arcFlagsValid = true;
    }

    /// <summary>
    /// Region of every vertex from a partition string, null if a label does not exist or is repeated.
    /// </summary>
    private int[]? ParsePartition(string regions)
    {
        int[] result = new int[labels.Count];
        Array.Fill(result, -1);
        int region = 0;
        foreach (string group in regions.Split(';'))
        {
            bool any = false;
            foreach (string label in group.Split(','))
            {
                string trimmed = label.Trim();
                if (trimmed.Length == 0)
                    continue;
                int v = GetVertexIndex(trimmed);
                if (v < 0 || result[v] >= 0)
                    return null;
                result[v] = region;
                any = true;
            }
            if (any)
                region++;
        }
        for (int v = 0; v < result.Length; v++)
        {
            if (result[v] < 0)
                result[v] = region;  // Vertices not listed: one additional region
        }
        return result;
    }

    /// <summary>
    /// Splits the vertices into connected chunks of similar size, using a breadth-first ordering over
    /// the underlying undirected graph.
    /// </summary>
    private int[] AutomaticPartition(int count)
    {
        int n = labels.Count;
        int[] result = new int[n];
        if (n == 0)
            return result;
        count = Math.Clamp(count, 1, n);
        int chunk = (n + count - 1) / count;

        bool[] visited = new bool[n];
        Queue<int> queue = new();
        int index = 0;
        for (int start = 0; start < n; start++)
        {
            if (visited[start])
                continue;
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                result[v] = index++ / chunk;
                for (int i = outStart[v]; i < outStart[v + 1]; i++)
                    Visit(edgeHead[outEdges[i]]);
                for (int i = inStart[v]; i < inStart[v + 1]; i++)
                    Visit(edgeTail[inEdges[i]]);
            }
        }
        return result;

        void Visit(int w)
        {
            if (!visited[w])
            {
                visited[w] = true;
                queue.Enqueue(w);
            }
        }
    }

    /// <summary>
    /// Computes the flags: edges inside a region get that region's flag. For every boundary vertex b of
    /// region r (a vertex with an incoming edge from another region), a backward Dijkstra to b runs,
    /// and every tight edge between reached vertices (d(u,b) = w(u,v) + d(v,b)) gets flag r. Only the
    /// edges of reached vertices are checked. Negative edges are ignored.
    /// </summary>
    private void ComputeArcFlags()
    {
        int n = labels.Count;
        arcFlags = new bool[edgeCount * regionCount];
        List<int> boundary = new();
        bool[] isBoundary = new bool[n];
        for (int e = 0; e < edgeCount; e++)
        {
            if (edgeWeight[e] < 0)
                continue;
            int tailRegion = vertexRegion[edgeTail[e]], headRegion = vertexRegion[edgeHead[e]];
            if (tailRegion == headRegion)
                arcFlags[e * regionCount + headRegion] = true;
            else if (!isBoundary[edgeHead[e]])
            {
                isBoundary[edgeHead[e]] = true;
                boundary.Add(edgeHead[e]);
            }
        }

        long[] dist = new long[n];
        int[] seen = new int[n];  // Search number that reached the vertex (0: none)
        List<int> reached = new();
        PriorityQueue<int, long> fringe = new();
        int search = 0;
        foreach (int b in boundary)
        {
            search++;
            reached.Clear();
            fringe.Clear();
            fringe.Enqueue(b, 0);
            while (fringe.TryDequeue(out int v, out long d))
            {
                if (seen[v] == search)
                    continue;
                seen[v] = search;
                dist[v] = d;
                reached.Add(v);
                for (int i = inStart[v]; i < inStart[v + 1]; i++)
                {
                    int e = inEdges[i];
                    if (edgeWeight[e] >= 0 && seen[edgeTail[e]] != search)
                        fringe.Enqueue(edgeTail[e], d + edgeWeight[e]);
                }
            }

            int region = vertexRegion[b];
            foreach (int v in reached)
            {
                for (int i = outStart[v]; i < outStart[v + 1]; i++)
                {
                    int e = outEdges[i];
                    int head = edgeHead[e];
                    if (edgeWeight[e] >= 0 && seen[head] == search && dist[v] == edgeWeight[e] + dist[head])
                        arcFlags[e * regionCount + region] = true;
                }
            }
        }
    }
}
