using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Eppstein;

/// <summary>
/// A path: the sequence of edges from the start to the goal of a query. Immutable.
/// </summary>
/// <remarks>
/// Named <c>GraphPath</c> so it does not clash with <c>System.IO.Path</c>.
/// <see cref="Weight"/> is the total weight at the time the path was found.
/// </remarks>
public sealed class GraphPath : IReadOnlyList<Edge>
{
    private readonly Graph? graph;
    private readonly int[] edges;

    /// <summary>
    /// The empty path ("no path").
    /// </summary>
    public static GraphPath Empty { get; } = new(null, Array.Empty<int>(), 0);

    internal GraphPath(Graph? graph, int[] edges, long weight)
    {
        this.graph = graph;
        this.edges = edges;
        Weight = weight;
    }

    /// <summary>
    /// Number of edges.
    /// </summary>
    public int Count => edges.Length;

    /// <summary>
    /// Returns the edge at a position (0 .. <see cref="Count"/> - 1).
    /// </summary>
    public Edge this[int index] => new(graph!, edges[index]);

    /// <summary>
    /// False if the path is empty ("no path"), true otherwise.
    /// </summary>
    public bool IsValid => edges.Length > 0;

    /// <summary>
    /// Sum of the edge weights.
    /// </summary>
    public long Weight { get; }

    /// <summary>
    /// The vertices of the path in order, from start to goal (none for an empty path).
    /// </summary>
    public IEnumerable<Vertex> Vertices
    {
        get
        {
            if (!IsValid)
                yield break;
            yield return this[0].Tail;
            foreach (Edge e in this)
                yield return e.Head;
        }
    }

    /// <summary>
    /// Comma-separated vertex labels, e.g. <c>HOME,PARK,OFFICE</c>; <c>(empty)</c> for an empty path.
    /// </summary>
    public string VertexNames
    {
        get
        {
            if (!IsValid)
                return "(empty)";
            StringBuilder builder = new();
            foreach (Vertex v in Vertices)
            {
                if (builder.Length > 0)
                    builder.Append(',');
                builder.Append(v.Label);
            }
            return builder.ToString();
        }
    }

    /// <inheritdoc/>
    public IEnumerator<Edge> GetEnumerator()
    {
        for (int i = 0; i < edges.Length; i++)
            yield return new Edge(graph!, edges[i]);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns the vertex labels and the weight, e.g. <c>HOME,PARK,OFFICE (9)</c>.
    /// </summary>
    public override string ToString() =>
        IsValid ? VertexNames + " (" + Weight.ToString(CultureInfo.InvariantCulture) + ")" : "(empty)";
}
