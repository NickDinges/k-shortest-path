using System;
using System.Collections.Generic;

namespace Eppstein;

/// <summary>
/// A directed graph with weighted edges, and Eppstein's algorithm to find the k shortest paths
/// between two of its vertices.
/// </summary>
/// <remarks>
/// <para>
/// Vertices and edges are stored in arrays; <see cref="Vertex"/> and <see cref="Edge"/> are
/// lightweight views on them. A graph is changed only through its methods, so precomputed data
/// (edge lists by direction, arc flags, the current query) is invalidated automatically.
/// </para>
/// <para>
/// Error handling: methods that create or look up something from user data report a failure in
/// their return value (<c>false</c> or <c>-1</c>). Invalid arguments that are programming errors
/// (null, an index out of range, a negative limit) throw an <see cref="ArgumentException"/>;
/// malformed import files throw a <see cref="FormatException"/>.
/// </para>
/// <para>A graph is not thread-safe; use one graph per thread.</para>
/// </remarks>
public sealed partial class Graph
{
    // Vertices
    private readonly List<string> labels = new();
    private readonly Dictionary<string, int> labelIndex = new(StringComparer.Ordinal);
    private object?[]? tags;

    // Edges, as parallel arrays of which the first edgeCount entries are used
    private int edgeCount;
    private int[] edgeTail = Array.Empty<int>();
    private int[] edgeHead = Array.Empty<int>();
    private long[] edgeWeight = Array.Empty<long>();
    private int[] edgeGroup = Array.Empty<int>();
    private readonly List<string> groups = new() { "" };  // group 0: no group
    private readonly Dictionary<string, int> groupIndex = new(StringComparer.Ordinal) { [""] = 0 };

    // Edges by direction (compressed sparse rows), rebuilt on demand after a change:
    // the edges leaving vertex v are outEdges[outStart[v] .. outStart[v + 1] - 1]
    private bool adjacencyValid;
    private int[] outStart = new int[1];
    private int[] outEdges = Array.Empty<int>();
    private int[] inStart = new int[1];
    private int[] inEdges = Array.Empty<int>();

    /// <summary>
    /// Incremented on every change of the graph and of the query settings; lets running
    /// enumerations detect that their results became invalid.
    /// </summary>
    private int changeCount;

    /// <summary>
    /// Creates an empty graph.
    /// </summary>
    public Graph()
    {
    }

    #region Vertices and edges
    /// <summary>
    /// Number of vertices; valid indices are 0 .. VertexCount - 1.
    /// </summary>
    public int VertexCount => labels.Count;

    /// <summary>
    /// Number of edges; valid edge numbers are 0 .. EdgeCount - 1.
    /// </summary>
    public int EdgeCount => edgeCount;

    /// <summary>
    /// All vertices, in order of their index.
    /// </summary>
    public IEnumerable<Vertex> Vertices
    {
        get
        {
            for (int v = 0; v < labels.Count; v++)
                yield return new Vertex(this, v);
        }
    }

    /// <summary>
    /// All edges, in order of creation.
    /// </summary>
    public IEnumerable<Edge> Edges
    {
        get
        {
            for (int e = 0; e < edgeCount; e++)
                yield return new Edge(this, e);
        }
    }

    /// <summary>
    /// Returns the vertex with an index.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is not valid.</exception>
    public Vertex GetVertex(int index)
    {
        if ((uint)index >= (uint)labels.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, "No vertex with this index");
        return new Vertex(this, index);
    }

    /// <summary>
    /// Returns the vertex with a label (case-insensitive), null if there is none.
    /// </summary>
    public Vertex? FindVertex(string label)
    {
        int index = GetVertexIndex(label);
        return index < 0 ? null : new Vertex(this, index);
    }

    /// <summary>
    /// Returns the index of the vertex with a label (case-insensitive), -1 if there is none.
    /// </summary>
    public int GetVertexIndex(string label)
    {
        if (string.IsNullOrEmpty(label))
            return -1;
        if (labelIndex.TryGetValue(label, out int index))
            return index;
        string upper = ToUpper(label);
        return !ReferenceEquals(upper, label) && labelIndex.TryGetValue(upper, out index) ? index : -1;
    }

    /// <summary>
    /// Returns the edge with a number.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The number is not valid.</exception>
    public Edge GetEdge(int id)
    {
        CheckEdge(id);
        return new Edge(this, id);
    }

    /// <summary>
    /// Creates all vertices from a comma-separated list of labels, removing all previous vertices
    /// and edges. Repeated and empty labels are ignored.
    /// </summary>
    /// <param name="vertices">Labels, e.g. <c>"S,A,B,T"</c>; each starts with a letter and contains
    /// only letters and digits (case-insensitive, stored in upper case).</param>
    /// <returns>True if all labels are valid; false at the first invalid label (the vertices before
    /// it are created).</returns>
    public bool CreateVertices(string vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        Clear();
        foreach (string label in vertices.Split(','))
        {
            if (label.Length == 0)
                continue;
            if (AddVertex(label) < 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Adds one vertex, keeping all existing vertices and edges.
    /// </summary>
    /// <param name="label">Label: starts with a letter, then only letters or digits (case-insensitive).</param>
    /// <returns>Index of the vertex (the existing one if the label is already used), -1 if the label
    /// is invalid.</returns>
    public int AddVertex(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        string upper = ToUpper(label);
        if (!IsValidLabel(upper))
            return -1;
        if (labelIndex.TryGetValue(upper, out int index))
            return index;

        index = labels.Count;
        labels.Add(upper);
        labelIndex.Add(upper, index);
        OnChanged(structure: true);
        return index;
    }

    /// <summary>
    /// Creates directed edges between vertices given by label, all with the same weight and group.
    /// </summary>
    /// <param name="tails">Comma-separated labels of the start vertices.</param>
    /// <param name="heads">Comma-separated labels of the end vertices, paired by position with
    /// <paramref name="tails"/>: <c>CreateEdges("A,B", "C,D", ...)</c> creates A→C and B→D.</param>
    /// <param name="weight">Weight of all edges (negative edges are ignored by the path search).</param>
    /// <param name="group">Group name (case-insensitive) for <see cref="EdgeGroupWeights"/>; null or
    /// empty for no group.</param>
    /// <returns>True if all vertices exist and both lists have the same length; otherwise false and
    /// no edge is created.</returns>
    public bool CreateEdges(string tails, string heads, long weight, string? group)
    {
        ArgumentNullException.ThrowIfNull(tails);
        ArgumentNullException.ThrowIfNull(heads);
        string[] tailLabels = tails.Split(',');
        string[] headLabels = heads.Split(',');
        if (tailLabels.Length != headLabels.Length)
            return false;

        int[] tailIndices = new int[tailLabels.Length];
        int[] headIndices = new int[headLabels.Length];
        for (int i = 0; i < tailLabels.Length; i++)
        {
            tailIndices[i] = GetVertexIndex(tailLabels[i]);
            headIndices[i] = GetVertexIndex(headLabels[i]);
            if (tailIndices[i] < 0 || headIndices[i] < 0)
                return false;
        }
        for (int i = 0; i < tailIndices.Length; i++)
            AddEdge(tailIndices[i], headIndices[i], weight, group);
        return true;
    }

    /// <summary>
    /// Adds one directed edge without a group between vertices given by index.
    /// </summary>
    /// <returns>Number of the new edge (see <see cref="GetEdge"/>), -1 if an index is not valid.</returns>
    public int AddEdge(int tail, int head, long weight) => AddEdge(tail, head, weight, null);

    /// <summary>
    /// Adds one directed edge between vertices given by index.
    /// </summary>
    /// <param name="tail">Index of the start vertex.</param>
    /// <param name="head">Index of the end vertex.</param>
    /// <param name="weight">Weight (negative edges are ignored by the path search).</param>
    /// <param name="group">Group name (case-insensitive) for <see cref="EdgeGroupWeights"/>; null or
    /// empty for no group.</param>
    /// <returns>Number of the new edge (see <see cref="GetEdge"/>), -1 if an index is not valid.</returns>
    public int AddEdge(int tail, int head, long weight, string? group)
    {
        if ((uint)tail >= (uint)labels.Count || (uint)head >= (uint)labels.Count)
            return -1;

        if (edgeCount == edgeTail.Length)
        {
            int capacity = Math.Max(16, edgeCount * 2);
            Array.Resize(ref edgeTail, capacity);
            Array.Resize(ref edgeHead, capacity);
            Array.Resize(ref edgeWeight, capacity);
            Array.Resize(ref edgeGroup, capacity);
        }
        int id = edgeCount++;
        edgeTail[id] = tail;
        edgeHead[id] = head;
        edgeWeight[id] = weight;
        edgeGroup[id] = GroupId(group);
        OnChanged(structure: true);
        return id;
    }

    /// <summary>
    /// Changes the weight of one edge.
    /// </summary>
    /// <exception cref="ArgumentException">The edge belongs to another graph.</exception>
    public void SetEdgeWeight(Edge edge, long weight)
    {
        if (!ReferenceEquals(edge.Graph, this))
            throw new ArgumentException("The edge belongs to another graph", nameof(edge));
        SetEdgeWeight(edge.Id, weight);
    }

    /// <summary>
    /// Changes the weight of one edge given by its number.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The number is not valid.</exception>
    public void SetEdgeWeight(int id, long weight)
    {
        CheckEdge(id);
        edgeWeight[id] = weight;
        OnChanged(structure: false);
    }

    /// <summary>
    /// Sets the weight of all edges of a group; for example a negative weight switches them off.
    /// </summary>
    /// <param name="group">Group name (case-insensitive).</param>
    /// <param name="weight">New weight.</param>
    public void EdgeGroupWeights(string group, long weight)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (!groupIndex.TryGetValue(ToUpper(group), out int id))
            return;
        for (int e = 0; e < edgeCount; e++)
        {
            if (edgeGroup[e] == id)
                edgeWeight[e] = weight;
        }
        OnChanged(structure: false);
    }
    #endregion

    #region Internal accessors (used by Vertex and Edge)
    internal string LabelOf(int vertex) => labels[vertex];

    internal object? GetTag(int vertex) => tags == null || vertex >= tags.Length ? null : tags[vertex];

    internal void SetTag(int vertex, object? tag)
    {
        if (tags == null || vertex >= tags.Length)
        {
            if (tag == null)
                return;
            Array.Resize(ref tags, Math.Max(labels.Count, 16));
        }
        tags[vertex] = tag;
    }

    internal int TailOf(int edge) => edgeTail[edge];

    internal int HeadOf(int edge) => edgeHead[edge];

    internal long WeightOf(int edge) => edgeWeight[edge];

    internal string GroupOf(int edge) => groups[edgeGroup[edge]];

    internal IEnumerable<Edge> OutEdgesOf(int vertex)
    {
        EnsureAdjacency();
        for (int i = outStart[vertex]; i < outStart[vertex + 1]; i++)
            yield return new Edge(this, outEdges[i]);
    }

    internal IEnumerable<Edge> InEdgesOf(int vertex)
    {
        EnsureAdjacency();
        for (int i = inStart[vertex]; i < inStart[vertex + 1]; i++)
            yield return new Edge(this, inEdges[i]);
    }
    #endregion

    #region Helpers
    /// <summary>
    /// Removes all vertices and edges and disables arc flags.
    /// </summary>
    private void Clear()
    {
        labels.Clear();
        labelIndex.Clear();
        tags = null;
        edgeCount = 0;
        groups.RemoveRange(1, groups.Count - 1);
        groupIndex.Clear();
        groupIndex[""] = 0;
        DisableArcFlags();
        OnChanged(structure: true);
    }

    /// <summary>
    /// Invalidates the current query and precomputed data after a change.
    /// </summary>
    /// <param name="structure">True if vertices or edges were added or removed (not only weights).</param>
    private void OnChanged(bool structure)
    {
        changeCount++;
        ready = false;
        if (structure)
            adjacencyValid = false;
        InvalidateArcFlags();
    }

    /// <summary>
    /// Returns the id of a group, creating it if needed.
    /// </summary>
    private int GroupId(string? group)
    {
        if (string.IsNullOrEmpty(group))
            return 0;
        string upper = ToUpper(group);
        if (!groupIndex.TryGetValue(upper, out int id))
        {
            id = groups.Count;
            groups.Add(upper);
            groupIndex.Add(upper, id);
        }
        return id;
    }

    private void CheckEdge(int id)
    {
        if ((uint)id >= (uint)edgeCount)
            throw new ArgumentOutOfRangeException(nameof(id), id, "No edge with this number");
    }

    /// <summary>
    /// Upper case independent of the current culture (no Turkish dotted I); returns the same string
    /// if nothing changes.
    /// </summary>
    internal static string ToUpper(string text)
    {
        foreach (char c in text)
        {
            if (char.ToUpperInvariant(c) != c)
                return text.ToUpperInvariant();
        }
        return text;
    }

    /// <summary>
    /// Tells if a label starts with a letter and contains only letters and digits.
    /// </summary>
    private static bool IsValidLabel(string label)
    {
        if (label.Length == 0 || !char.IsLetter(label[0]))
            return false;
        foreach (char c in label)
        {
            if (!char.IsLetterOrDigit(c))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Builds the edge lists by direction (counting sort by vertex) if the graph changed.
    /// </summary>
    private void EnsureAdjacency()
    {
        if (adjacencyValid)
            return;
        int n = labels.Count;
        outStart = BuildRows(edgeTail, n, out outEdges);
        inStart = BuildRows(edgeHead, n, out inEdges);
        adjacencyValid = true;
    }

    /// <summary>
    /// Groups the edges by a vertex (tail or head): returns the start of each vertex's row in
    /// <paramref name="rows"/>, which lists the edge numbers in order of creation.
    /// </summary>
    private int[] BuildRows(int[] vertexOfEdge, int vertexCount, out int[] rows)
    {
        int[] start = new int[vertexCount + 1];
        for (int e = 0; e < edgeCount; e++)
            start[vertexOfEdge[e] + 1]++;
        for (int v = 0; v < vertexCount; v++)
            start[v + 1] += start[v];
        rows = new int[edgeCount];
        int[] next = (int[])start.Clone();
        for (int e = 0; e < edgeCount; e++)
            rows[next[vertexOfEdge[e]]++] = e;
        return start;
    }
    #endregion
}
