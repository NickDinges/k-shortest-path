using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Eppstein;

/// <summary>
/// Graphviz (DOT) output.
/// </summary>
/// <remarks>
/// Render the output with Graphviz, e.g. <c>dot -Tsvg graph.dot -o graph.svg</c>. Edges are labeled
/// with their weight; negative edges (ignored by the path search) are dashed. After a query, start and
/// goal are double circles, vertices show their distance to the goal and the edges of the shortest path
/// tree are bold. When arc flags are computed, every region is drawn as a cluster.
/// </remarks>
public sealed partial class Graph
{
    private static readonly string[] HighlightColors =
        { "red", "blue", "darkgreen", "orange", "purple", "brown", "magenta", "cyan4" };

    /// <summary>
    /// Returns the graph in Graphviz DOT format.
    /// </summary>
    public string Serialize() => Serialize((IEnumerable<GraphPath>?)null);

    /// <summary>
    /// Returns the graph in Graphviz DOT format with one path highlighted in red.
    /// </summary>
    public string Serialize(GraphPath highlight)
    {
        ArgumentNullException.ThrowIfNull(highlight);
        return Serialize(new[] { highlight });
    }

    /// <summary>
    /// Returns the graph in Graphviz DOT format with several paths highlighted, one color each.
    /// </summary>
    /// <param name="highlight">Paths to highlight; may be null.</param>
    public string Serialize(IEnumerable<GraphPath>? highlight)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        Serialize(writer, highlight);
        return writer.ToString();
    }

    /// <summary>
    /// Writes the graph in Graphviz DOT format, e.g. to a file.
    /// </summary>
    /// <param name="writer">Destination.</param>
    /// <param name="highlight">Paths to highlight, one color each; may be null.</param>
    public void Serialize(TextWriter writer, IEnumerable<GraphPath>? highlight)
    {
        ArgumentNullException.ThrowIfNull(writer);
        EnsureAdjacency();

        // Colors of every highlighted edge, one entry per path using it
        Dictionary<int, List<string>> edgeColors = new();
        HashSet<int> highlightedVertices = new();
        if (highlight != null)
        {
            int index = 0;
            foreach (GraphPath path in highlight)
            {
                if (path == null || !path.IsValid)
                    continue;
                string color = HighlightColors[index++ % HighlightColors.Length];
                foreach (Edge e in path)
                {
                    if (!edgeColors.TryGetValue(e.Id, out List<string>? colors))
                        edgeColors[e.Id] = colors = new List<string>();
                    if (!colors.Contains(color))  // A path may cross an edge several times
                        colors.Add(color);
                    highlightedVertices.Add(edgeTail[e.Id]);
                    highlightedVertices.Add(edgeHead[e.Id]);
                }
            }
        }

        writer.WriteLine("digraph Eppstein {");
        writer.WriteLine("    rankdir=LR;");
        writer.WriteLine("    node [shape=circle];");

        // Vertices, grouped by arc-flags region if available
        bool regions = arcFlagsEnabled && arcFlagsValid && regionCount > 0;
        if (regions)
        {
            for (int r = 0; r < regionCount; r++)
            {
                writer.WriteLine("    subgraph cluster_" + r.ToString(CultureInfo.InvariantCulture) + " {");
                writer.WriteLine("        label=\"Region " + r.ToString(CultureInfo.InvariantCulture) + "\";");
                writer.WriteLine("        style=dashed;");
                for (int v = 0; v < labels.Count; v++)
                {
                    if (vertexRegion[v] == r)
                        writer.WriteLine("        " + VertexStatement(v, highlightedVertices));
                }
                writer.WriteLine("    }");
            }
        }
        else
        {
            for (int v = 0; v < labels.Count; v++)
                writer.WriteLine("    " + VertexStatement(v, highlightedVertices));
        }

        // Edges, grouped by tail in vertex order
        for (int v = 0; v < labels.Count; v++)
        {
            for (int i = outStart[v]; i < outStart[v + 1]; i++)
            {
                int e = outEdges[i];
                List<string> attributes = new()
                {
                    "label=" + Quote(edgeWeight[e].ToString(CultureInfo.InvariantCulture)),
                    "tooltip=" + Quote("group " + groups[edgeGroup[e]]),
                };
                List<string> styles = new();
                if (edgeWeight[e] < 0)
                    styles.Add("dashed");
                if (ready && DistanceOf(v) != Vertex.Unreached && treeEdge[v] == e)  // Shortest path tree
                    styles.Add("bold");
                if (edgeColors.TryGetValue(e, out List<string>? colors))
                {
                    attributes.Add("color=" + Quote(string.Join(":", colors)));
                    attributes.Add("fontcolor=" + Quote(colors[0]));
                    attributes.Add("penwidth=2");
                }
                if (styles.Count > 0)
                    attributes.Add("style=" + Quote(string.Join(",", styles)));
                writer.WriteLine("    " + Quote(labels[v]) + " -> " + Quote(labels[edgeHead[e]]) +
                    " [" + string.Join(", ", attributes) + "];");
            }
        }
        writer.WriteLine("}");
    }

    private string VertexStatement(int v, HashSet<int> highlighted)
    {
        string label = labels[v];
        if (ready && DistanceOf(v) != Vertex.Unreached)  // Distance to the goal is known
            label += "\n" + distance[v].ToString(CultureInfo.InvariantCulture);
        List<string> attributes = new() { "label=" + Quote(label) };
        if (ready && (v == source || v == target))
            attributes.Add("shape=doublecircle");
        if (highlighted.Contains(v))
            attributes.Add("penwidth=2");
        return Quote(labels[v]) + " [" + string.Join(", ", attributes) + "];";
    }

    /// <summary>
    /// Quotes and escapes a DOT identifier or string.
    /// </summary>
    private static string Quote(string text)
    {
        StringBuilder builder = new("\"");
        foreach (char c in text)
        {
            if (c == '"' || c == '\\')
                builder.Append('\\').Append(c);
            else if (c == '\n')
                builder.Append("\\n");
            else
                builder.Append(c);
        }
        return builder.Append('"').ToString();
    }
}
