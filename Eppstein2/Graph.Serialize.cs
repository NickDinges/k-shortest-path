using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Eppstein
{
    /// <summary>
    /// Graphviz (DOT) serialization of the graph
    /// </summary>
    /// <remarks>
    /// Render the output with Graphviz, e.g. <c>dot -Tsvg graph.dot -o graph.svg</c>.
    /// Edges are labeled with their weight, negative edges (ignored by the algorithm) are dashed.
    /// After <see cref="FindShortestPath"/> the source and endpoint are drawn as double circles,
    /// vertices show their distance to the endpoint and shortest path tree edges are bold.
    /// When arc flags are computed, every region is drawn as a cluster.
    /// </remarks>
    public partial class Graph
    {
        /// <summary>
        /// Colors used for highlighted paths, in order (repeated if more paths are given)
        /// </summary>
        private static readonly string[] HighlightColors =
            { "red", "blue", "darkgreen", "orange", "purple", "brown", "magenta", "cyan4" };

        /// <summary>
        /// Serializes the graph into Graphviz DOT format
        /// </summary>
        /// <returns>DOT document describing the directed graph</returns>
        public string Serialize()
        {
            return Serialize((IEnumerable<Path>)null);
        }
        /// <summary>
        /// Serializes the graph into Graphviz DOT format, highlighting a path
        /// </summary>
        /// <param name="_highlight">Path to be drawn in color, ignored if null or empty</param>
        /// <returns>DOT document describing the directed graph</returns>
        public string Serialize(Path _highlight)
        {
            return Serialize(_highlight == null ? null : new Path[] { _highlight });
        }
        /// <summary>
        /// Serializes the graph into Graphviz DOT format, highlighting several paths
        /// </summary>
        /// <param name="_highlight">Paths to be drawn in different colors, may be null</param>
        /// <returns>DOT document describing the directed graph</returns>
        public string Serialize(IEnumerable<Path> _highlight)
        {
            StringWriter writer = new StringWriter(CultureInfo.InvariantCulture);
            Serialize(writer, _highlight);
            return writer.ToString();
        }
        /// <summary>
        /// Writes the graph in Graphviz DOT format
        /// </summary>
        /// <param name="_writer">Destination, e.g. a StreamWriter for a .dot file</param>
        /// <param name="_highlight">Paths to be drawn in different colors, may be null</param>
        /// <exception cref="System.ArgumentNullException">Thrown when writer is null</exception>
        public void Serialize(TextWriter _writer, IEnumerable<Path> _highlight)
        {
            if (_writer == null)
                throw new ArgumentNullException("_writer");

            // Colors of every highlighted edge, one entry per path using it
            Dictionary<Edge, List<string>> edgeColors = new Dictionary<Edge, List<string>>();
            HashSet<Vertex> highlightedVertices = new HashSet<Vertex>();
            if (_highlight != null)
            {
                int index = 0;
                foreach (Path _p in _highlight)
                {
                    if (_p == null || !_p.IsValid)
                        continue;
                    string color = HighlightColors[index++ % HighlightColors.Length];
                    foreach (Edge _e in _p)
                    {
                        List<string> colors;
                        if (!edgeColors.TryGetValue(_e, out colors))
                            edgeColors[_e] = colors = new List<string>();
                        if (!colors.Contains(color))  // Path crossing an edge several times
                            colors.Add(color);
                        highlightedVertices.Add(_e.Tail);
                        highlightedVertices.Add(_e.Head);
                    }
                }
            }

            _writer.WriteLine("digraph Eppstein {");
            _writer.WriteLine("    rankdir=LR;");
            _writer.WriteLine("    node [shape=circle];");

            // Vertices, grouped by arc-flags region if available
            bool regions = arcFlagsEnabled && arcFlagsValid && regionCount > 0;
            if (regions)
            {
                for (int r = 0; r < regionCount; r++)
                {
                    _writer.WriteLine("    subgraph cluster_" + r + " {");
                    _writer.WriteLine("        label=\"Region " + r + "\";");
                    _writer.WriteLine("        style=dashed;");
                    foreach (Vertex _v in Vertices)
                        if (_v.Region == r)
                            _writer.WriteLine("        " + VertexStatement(_v, highlightedVertices));
                    _writer.WriteLine("    }");
                }
            }
            foreach (Vertex _v in Vertices)
                if (!regions || _v.Region < 0 || _v.Region >= regionCount)
                    _writer.WriteLine("    " + VertexStatement(_v, highlightedVertices));

            // Edges, each one written once (from its tail)
            foreach (Vertex _v in Vertices)
            {
                foreach (Edge _e in _v.RelatedEdges)
                {
                    if (_e.Tail != _v)
                        continue;

                    List<string> attributes = new List<string>();
                    attributes.Add("label=" + Quote(_e.Weight.ToString(CultureInfo.InvariantCulture)));
                    attributes.Add("tooltip=" + Quote("group " + _e.Group));

                    List<string> styles = new List<string>();
                    if (_e.Weight < 0)
                        styles.Add("dashed");
                    if (Ready && _e.Tail.EdgeToPath == _e)  // Shortest path tree edge
                        styles.Add("bold");

                    List<string> colors;
                    if (edgeColors.TryGetValue(_e, out colors))
                    {
                        attributes.Add("color=" + Quote(string.Join(":", colors.ToArray())));
                        attributes.Add("fontcolor=" + Quote(colors[0]));
                        attributes.Add("penwidth=2");
                    }
                    if (styles.Count > 0)
                        attributes.Add("style=" + Quote(string.Join(",", styles.ToArray())));

                    _writer.WriteLine("    " + Quote(_e.Tail.ToString()) + " -> " + Quote(_e.Head.ToString()) +
                        " [" + string.Join(", ", attributes.ToArray()) + "];");
                }
            }

            _writer.WriteLine("}");
        }

        /// <summary>
        /// Builds the DOT statement of a vertex
        /// </summary>
        private string VertexStatement(Vertex _v, HashSet<Vertex> _highlighted)
        {
            List<string> attributes = new List<string>();
            string label = _v.ToString();
            if (Ready && _v.Distance != int.MinValue)  // Distance to endpoint is known
                label += "\n" + _v.Distance.ToString(CultureInfo.InvariantCulture);
            attributes.Add("label=" + Quote(label));
            if (Ready && (_v == SourceVertex || _v == EndVertex))
                attributes.Add("shape=doublecircle");
            if (_highlighted.Contains(_v))
                attributes.Add("penwidth=2");
            return Quote(_v.ToString()) + " [" + string.Join(", ", attributes.ToArray()) + "];";
        }
        /// <summary>
        /// Quotes and escapes a DOT identifier or string
        /// </summary>
        private static string Quote(string _text)
        {
            StringBuilder builder = new StringBuilder("\"");
            foreach (char c in _text)
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
}
