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
    private int[]? filePartition;  // Region of every vertex, from EnableArcFlagsFromPartition
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
        filePartition = null;
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
        filePartition = null;
        arcFlagsEnabled = true;
        InvalidateArcFlags();
        ready = false;
        return true;
    }

    /// <summary>
    /// Enables arc flags with a partition computed by an external partitioner, e.g.
    /// <a href="https://github.com/kahypar/mt-kahypar">mt-KaHyPar</a>, KaHIP or METIS.
    /// </summary>
    /// <remarks>
    /// Write the graph with <see cref="WriteMetisGraph"/>, partition it, and pass the result: one line
    /// per vertex, in the order of the vertex indices, with its region number (0, 1, 2, …). This is the
    /// output format of the partitioners named above. Good partitions have few edges between regions,
    /// which makes the preprocessing faster and the flags more selective. If vertices are added later,
    /// arc flags are disabled on the next query (the partition no longer fits).
    /// </remarks>
    /// <param name="reader">The partition, e.g. a StreamReader for a file.</param>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    /// <exception cref="FormatException">Thrown for a line that is not a region number between 0 and
    /// VertexCount - 1 (the message names the line), or if the number of lines is not VertexCount.</exception>
    public void EnableArcFlagsFromPartition(System.IO.TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int n = labels.Count;
        int[] partition = new int[n];
        int count = 0, lineNumber = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '%' || trimmed[0] == '#')
                continue;  // Empty or comment
            if (!int.TryParse(trimmed, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int region) || region >= Math.Max(n, 1))
                throw new FormatException("Line " + lineNumber + ": expected a region number between 0 and " +
                    (n - 1) + ", found '" + trimmed + "'");
            if (count == n)
                throw new FormatException("Line " + lineNumber + ": more lines than the " + n + " vertices of the graph");
            partition[count++] = region;
        }
        if (count != n)
            throw new FormatException("The partition has " + count + " regions, the graph has " + n + " vertices");

        filePartition = partition;
        manualRegions = null;
        autoRegionCount = 0;
        arcFlagsEnabled = true;
        InvalidateArcFlags();
        ready = false;
    }

    /// <summary>
    /// Writes the graph in METIS format, the input format of graph partitioners such as mt-KaHyPar,
    /// KaHIP and METIS (see <see cref="EnableArcFlagsFromPartition"/>).
    /// </summary>
    /// <remarks>
    /// Partitioners expect an undirected graph: an edge in either direction connects two vertices
    /// once. Self-loops, parallel edges and edges with negative weight (switched off) are left out.
    /// Vertex i is written as METIS vertex i + 1.
    /// </remarks>
    /// <param name="writer">Destination, e.g. a StreamWriter for a .graph file.</param>
    public void WriteMetisGraph(System.IO.TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        int n = labels.Count;
        List<int>[] neighbors = new List<int>[n];
        for (int v = 0; v < n; v++)
            neighbors[v] = new List<int>();
        for (int e = 0; e < edgeCount; e++)
        {
            int a = edgeTail[e], b = edgeHead[e];
            if (a != b && edgeWeight[e] >= 0)
            {
                neighbors[a].Add(b);
                neighbors[b].Add(a);
            }
        }
        long undirected = 0;
        for (int v = 0; v < n; v++)
        {
            neighbors[v].Sort();
            int unique = 0;  // Remove duplicates in place
            for (int i = 0; i < neighbors[v].Count; i++)
            {
                if (i == 0 || neighbors[v][i] != neighbors[v][i - 1])
                    neighbors[v][unique++] = neighbors[v][i];
            }
            neighbors[v].RemoveRange(unique, neighbors[v].Count - unique);
            undirected += unique;
        }

        writer.WriteLine(n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " +
            (undirected / 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Text.StringBuilder line = new();
        for (int v = 0; v < n; v++)
        {
            line.Clear();
            foreach (int w in neighbors[v])
            {
                if (line.Length > 0)
                    line.Append(' ');
                line.Append(w + 1);
            }
            writer.WriteLine(line.ToString());
        }
    }

    /// <summary>
    /// Disables arc flags and releases their memory.
    /// </summary>
    public void DisableArcFlags()
    {
        arcFlagsEnabled = false;
        autoRegionCount = 0;
        manualRegions = null;
        filePartition = null;
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
        int[]? partition = filePartition != null ? (filePartition.Length == labels.Count ? (int[])filePartition.Clone() : null)
            : manualRegions != null ? ParsePartition(manualRegions)
            : AutomaticPartition(autoRegionCount);
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
