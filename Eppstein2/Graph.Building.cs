using System;
using System.Globalization;
using System.IO;

namespace Eppstein
{
    /// <summary>
    /// Fast graph building by vertex index, and bulk import from CSV and DIMACS files
    /// </summary>
    /// <remarks>
    /// CreateVertices and CreateEdges parse comma-separated label lists on every call. For large
    /// graphs it is much faster to add each vertex once (AddVertex returns its index) and then add
    /// edges by index (AddEdge). Both ways can be mixed: vertices created by CreateVertices have
    /// indices too (see GetVertexIndex), in the order they were created.
    /// </remarks>
    public partial class Graph
    {
        #region Building by index
        /// <summary>
        /// Number of vertices in the graph; valid indices are 0 .. VertexCount - 1
        /// </summary>
        public int VertexCount
        {
            get { return Vertices.Count; }
        }
        /// <summary>
        /// Adds one vertex, keeping all existing vertices and edges
        /// </summary>
        /// <param name="_label">Label: starts with a letter, then letters or digits (case-insensitive)</param>
        /// <returns>Index of the vertex (the existing one if the label is already used), -1 if the label is invalid</returns>
        public int AddVertex(string _label)
        {
            if (_label == null)
                return -1;
            string label = _label;
            foreach (char c in _label)
            {
                if (char.IsLower(c))  // Converts only if needed (saves a string per vertex)
                {
                    label = _label.ToUpper();
                    break;
                }
            }
            if (!IsValidLabel(label))
                return -1;

            Vertex v = GetVertex(label);
            if (v == null)
            {
                v = AppendVertex(label);
                Ready = false;
                AdjacencyValid = false;
                InvalidateArcFlags();  // Partition does not contain the new vertex
            }
            return v.Index;
        }
        /// <summary>
        /// Returns the index of a vertex
        /// </summary>
        /// <param name="_label">Vertex label (case-insensitive)</param>
        /// <returns>Index of the vertex, -1 if it does not exist</returns>
        public int GetVertexIndex(string _label)
        {
            Vertex v = _label == null ? null : GetVertex(_label);
            return v == null ? -1 : v.Index;
        }
        /// <summary>
        /// Adds one directional edge between vertices given by index, without a group
        /// </summary>
        /// <param name="_tail">Index of the start vertex</param>
        /// <param name="_head">Index of the end vertex</param>
        /// <param name="_weight">Weight of the edge (negative edges are ignored by the algorithm)</param>
        /// <returns>True if both indices are valid</returns>
        public bool AddEdge(int _tail, int _head, long _weight)
        {
            return AddEdge(_tail, _head, _weight, "");
        }
        /// <summary>
        /// Adds one directional edge between vertices given by index
        /// </summary>
        /// <param name="_tail">Index of the start vertex</param>
        /// <param name="_head">Index of the end vertex</param>
        /// <param name="_weight">Weight of the edge (negative edges are ignored by the algorithm)</param>
        /// <param name="_group">Group name (case-insensitive), see EdgeGroupWeights; null means no group</param>
        /// <returns>True if both indices are valid</returns>
        public bool AddEdge(int _tail, int _head, long _weight, string _group)
        {
            Vertex tail = VertexAt(_tail);
            Vertex head = VertexAt(_head);
            if (tail == null || head == null)
                return false;

            Edge e = new Edge(tail, head, _weight, _group == null ? "" : _group.ToUpper());
            tail.RelatedEdges.Add(e);
            if (tail != head)  // Avoids duplicated reference for self-pointing edges
                head.RelatedEdges.Add(e);

            Ready = false;
            AdjacencyValid = false;
            InvalidateArcFlags();  // New arcs need flags
            return true;
        }
        #endregion

        #region Import
        /// <summary>
        /// Adds edges from a CSV text: one edge per line, "tail,head,weight" or "tail,head,weight,group"
        /// </summary>
        /// <param name="_reader">Source, e.g. a StreamReader for a .csv file</param>
        /// <returns>Number of edges added</returns>
        /// <remarks>
        /// Vertices are created as needed (same label rules as CreateVertices). Empty lines and lines
        /// starting with '#' are skipped, spaces around values are ignored. Existing vertices and edges
        /// are kept. If a line is invalid, the edges of the lines before it stay in the graph.
        /// </remarks>
        /// <exception cref="System.ArgumentNullException">Thrown when reader is null</exception>
        /// <exception cref="System.FormatException">Thrown for an invalid line, the message names the line number</exception>
        public int ImportEdges(TextReader _reader)
        {
            if (_reader == null)
                throw new ArgumentNullException("_reader");

            int count = 0, lineNumber = 0;
            string line;
            while ((line = _reader.ReadLine()) != null)
            {
                lineNumber++;
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                    continue;

                string[] fields = trimmed.Split(',');
                if (fields.Length < 3 || fields.Length > 4)
                    throw ImportError(lineNumber, "expected tail,head,weight[,group] but found " + fields.Length + " values");

                int tail = AddVertex(fields[0].Trim());
                if (tail < 0)
                    throw ImportError(lineNumber, "invalid vertex label '" + fields[0].Trim() + "'");
                int head = AddVertex(fields[1].Trim());
                if (head < 0)
                    throw ImportError(lineNumber, "invalid vertex label '" + fields[1].Trim() + "'");
                long weight;
                if (!long.TryParse(fields[2].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out weight))
                    throw ImportError(lineNumber, "invalid weight '" + fields[2].Trim() + "'");

                AddEdge(tail, head, weight, fields.Length == 4 ? fields[3].Trim() : "");
                count++;
            }
            return count;
        }
        /// <summary>
        /// Adds a graph in DIMACS shortest path format (used by the DIMACS challenge road networks)
        /// </summary>
        /// <param name="_reader">Source, e.g. a StreamReader for a .gr file</param>
        /// <returns>Number of edges added</returns>
        /// <remarks>
        /// Lines: "c comment", "p sp VERTICES EDGES" (once, before the edges), "a TAIL HEAD WEIGHT" with
        /// vertex numbers 1 .. VERTICES. Vertex number n gets the label "V" + n (e.g. V1). Existing
        /// vertices and edges are kept. If a line is invalid, the edges of the lines before it stay in
        /// the graph.
        /// </remarks>
        /// <exception cref="System.ArgumentNullException">Thrown when reader is null</exception>
        /// <exception cref="System.FormatException">Thrown for an invalid line, the message names the line number</exception>
        public int ImportDimacs(TextReader _reader)
        {
            if (_reader == null)
                throw new ArgumentNullException("_reader");

            int[] index = null;  // vertex number -> vertex index
            int count = 0, lineNumber = 0;
            string line;
            char[] separators = { ' ', '\t' };
            while ((line = _reader.ReadLine()) != null)
            {
                lineNumber++;
                ReadOnlySpan<char> rest = line.AsSpan();
                ReadOnlySpan<char> type = NextToken(ref rest);
                if (type.Length == 0 || type.SequenceEqual("c"))
                    continue;

                if (type.SequenceEqual("a"))  // Most frequent line: parsed without creating strings
                {
                    if (index == null)
                        throw ImportError(lineNumber, "edge before the problem line 'p sp VERTICES EDGES'");
                    int tail, head;
                    long weight;
                    if (!int.TryParse(NextToken(ref rest), NumberStyles.None, CultureInfo.InvariantCulture, out tail) ||
                        !int.TryParse(NextToken(ref rest), NumberStyles.None, CultureInfo.InvariantCulture, out head) ||
                        !long.TryParse(NextToken(ref rest), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out weight) ||
                        NextToken(ref rest).Length != 0)
                        throw ImportError(lineNumber, "expected 'a TAIL HEAD WEIGHT'");
                    if (tail < 1 || tail >= index.Length || head < 1 || head >= index.Length)
                        throw ImportError(lineNumber, "vertex number out of range 1.." + (index.Length - 1));

                    AddEdge(index[tail], index[head], weight, "");
                    count++;
                    continue;
                }

                string[] fields = line.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (fields[0] == "p")
                {
                    int vertices;
                    long edges;
                    if (index != null)
                        throw ImportError(lineNumber, "more than one problem line");
                    if (fields.Length != 4 || fields[1] != "sp" ||
                        !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out vertices) ||
                        !long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out edges))
                        throw ImportError(lineNumber, "expected 'p sp VERTICES EDGES'");

                    index = new int[vertices + 1];
                    for (int i = 1; i <= vertices; i++)
                        index[i] = AddVertex("V" + i.ToString(CultureInfo.InvariantCulture));
                }
                else
                    throw ImportError(lineNumber, "unknown line type '" + fields[0] + "'");
            }
            return count;
        }
        /// <summary>
        /// Returns the next token separated by spaces or tabs and removes it from the text
        /// </summary>
        /// <returns>The token, empty if there are no more tokens</returns>
        private static ReadOnlySpan<char> NextToken(ref ReadOnlySpan<char> _text)
        {
            int start = 0;
            while (start < _text.Length && (_text[start] == ' ' || _text[start] == '\t'))
                start++;
            int end = start;
            while (end < _text.Length && _text[end] != ' ' && _text[end] != '\t')
                end++;
            ReadOnlySpan<char> token = _text.Slice(start, end - start);
            _text = _text.Slice(end);
            return token;
        }
        /// <summary>
        /// Creates an import error with line number
        /// </summary>
        private static FormatException ImportError(int _line, string _message)
        {
            return new FormatException("Line " + _line + ": " + _message);
        }
        #endregion
    }
}
