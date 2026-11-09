using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Eppstein;

/// <summary>
/// A directed edge of a <see cref="Graph"/>, from <see cref="Tail"/> to <see cref="Head"/>:
/// a lightweight view (graph and edge number), not a stored object.
/// </summary>
/// <remarks>
/// Edges are changed only through the graph (<see cref="Graph.SetEdgeWeight(Edge, long)"/>,
/// <see cref="Graph.EdgeGroupWeights"/>), so the graph always knows when its precomputed data
/// becomes outdated. Two <see cref="Edge"/> values are equal if they refer to the same edge of the
/// same graph.
/// </remarks>
public readonly struct Edge : IEquatable<Edge>
{
    private readonly Graph graph;

    internal Edge(Graph graph, int id)
    {
        this.graph = graph;
        Id = id;
    }

    /// <summary>
    /// Number of the edge in its graph (0 .. <see cref="Graph.EdgeCount"/> - 1), in order of creation.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Start vertex.
    /// </summary>
    public Vertex Tail => new(graph, graph.TailOf(Id));

    /// <summary>
    /// End vertex.
    /// </summary>
    public Vertex Head => new(graph, graph.HeadOf(Id));

    /// <summary>
    /// Weight; edges with a negative weight are ignored by the path search.
    /// </summary>
    public long Weight => graph.WeightOf(Id);

    /// <summary>
    /// Group name (upper case), empty if the edge has no group.
    /// </summary>
    public string Group => graph.GroupOf(Id);

    /// <summary>
    /// Extra weight of this edge compared to the shortest path, for the last query:
    /// weight + distance(head) - distance(tail). Only meaningful if both distances are known.
    /// </summary>
    public long Delta => Weight + Head.Distance - Tail.Distance;

    /// <summary>
    /// Tells if the edge may be part of a shortest path into an arc-flags region.
    /// </summary>
    /// <param name="region">Region index; negative values disable the check.</param>
    /// <returns>True if the flag is set, or if arc flags are not computed (conservative).</returns>
    public bool HasArcFlag(int region) => graph.HasArcFlag(Id, region);

    /// <inheritdoc/>
    public bool Equals(Edge other) => ReferenceEquals(graph, other.graph) && Id == other.Id;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Edge other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(graph), Id);

    /// <summary>
    /// Returns tail, weight and head, e.g. <c>A--5--&gt;B</c>.
    /// </summary>
    public override string ToString() =>
        graph == null ? "(none)" : Tail + "--" + Weight.ToString(CultureInfo.InvariantCulture) + "-->" + Head;

    /// <summary>
    /// True if both values refer to the same edge of the same graph.
    /// </summary>
    public static bool operator ==(Edge left, Edge right) => left.Equals(right);

    /// <summary>
    /// True if the values refer to different edges.
    /// </summary>
    public static bool operator !=(Edge left, Edge right) => !left.Equals(right);

    internal Graph Graph => graph;
}
