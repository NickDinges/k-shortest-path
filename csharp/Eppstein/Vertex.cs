using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Eppstein;

/// <summary>
/// A vertex of a <see cref="Graph"/>: a lightweight view (graph and index), not a stored object.
/// </summary>
/// <remarks>
/// Two <see cref="Vertex"/> values are equal if they refer to the same vertex of the same graph.
/// The values that depend on a query (<see cref="Distance"/>, <see cref="TreeEdge"/>,
/// <see cref="Next"/>) describe the shortest path tree of the last
/// <see cref="Graph.FindShortestPath(string, string)"/> call.
/// </remarks>
public readonly struct Vertex : IEquatable<Vertex>
{
    /// <summary>
    /// Value of <see cref="Distance"/> while the distance to the endpoint is unknown.
    /// </summary>
    public const long Unreached = long.MinValue;

    private readonly Graph graph;

    internal Vertex(Graph graph, int index)
    {
        this.graph = graph;
        Index = index;
    }

    /// <summary>
    /// Position of the vertex in its graph (0 .. <see cref="Graph.VertexCount"/> - 1).
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Label of the vertex (upper case).
    /// </summary>
    public string Label => graph.LabelOf(Index);

    /// <summary>
    /// Any object of the user attached to this vertex, null by default.
    /// </summary>
    public object? Tag
    {
        get => graph.GetTag(Index);
        set => graph.SetTag(Index, value);
    }

    /// <summary>
    /// Distance to the endpoint of the last query, <see cref="Unreached"/> if it is unknown
    /// (no query yet, endpoint not reachable, or outside of the corridor).
    /// </summary>
    public long Distance => graph.DistanceOf(Index);

    /// <summary>
    /// Edge of the shortest path tree leaving this vertex (the best first step towards the endpoint
    /// of the last query), null if there is none.
    /// </summary>
    public Edge? TreeEdge => graph.TreeEdgeOf(Index);

    /// <summary>
    /// Next vertex on the shortest path to the endpoint of the last query, null if there is none.
    /// </summary>
    public Vertex? Next => TreeEdge?.Head;

    /// <summary>
    /// Arc-flags region of the vertex, -1 if arc flags are not computed.
    /// </summary>
    public int Region => graph.RegionOf(Index);

    /// <summary>
    /// Edges leaving this vertex.
    /// </summary>
    public IEnumerable<Edge> OutEdges => graph.OutEdgesOf(Index);

    /// <summary>
    /// Edges arriving at this vertex.
    /// </summary>
    public IEnumerable<Edge> InEdges => graph.InEdgesOf(Index);

    /// <inheritdoc/>
    public bool Equals(Vertex other) => ReferenceEquals(graph, other.graph) && Index == other.Index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Vertex other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(graph), Index);

    /// <summary>
    /// Returns the label of the vertex.
    /// </summary>
    public override string ToString() => graph == null ? "(none)" : Label;

    /// <summary>
    /// True if both values refer to the same vertex of the same graph.
    /// </summary>
    public static bool operator ==(Vertex left, Vertex right) => left.Equals(right);

    /// <summary>
    /// True if the values refer to different vertices.
    /// </summary>
    public static bool operator !=(Vertex left, Vertex right) => !left.Equals(right);
}
