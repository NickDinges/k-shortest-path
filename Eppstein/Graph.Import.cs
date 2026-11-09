using System;
using System.Globalization;
using System.IO;

namespace Eppstein;

public sealed partial class Graph
{
    #region Import
    /// <summary>
    /// Adds edges from a CSV text: one edge per line, "tail,head,weight" or "tail,head,weight,group"
    /// </summary>
    /// <param name="reader">Source, e.g. a StreamReader for a .csv file</param>
    /// <returns>Number of edges added</returns>
    /// <remarks>
    /// Vertices are created as needed (same label rules as CreateVertices). Empty lines and lines
    /// starting with '#' are skipped, spaces around values are ignored. Existing vertices and edges
    /// are kept. If a line is invalid, the edges of the lines before it stay in the graph.
    /// </remarks>
    /// <exception cref="System.ArgumentNullException">Thrown when reader is null</exception>
    /// <exception cref="System.FormatException">Thrown for an invalid line, the message names the line number</exception>
    public int ImportEdges(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int count = 0, lineNumber = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
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
    /// <param name="reader">Source, e.g. a StreamReader for a .gr file</param>
    /// <returns>Number of edges added</returns>
    /// <remarks>
    /// Lines: "c comment", "p sp VERTICES EDGES" (once, before the edges), "a TAIL HEAD WEIGHT" with
    /// vertex numbers 1 .. VERTICES. Vertex number n gets the label "V" + n (e.g. V1). Existing
    /// vertices and edges are kept. If a line is invalid, the edges of the lines before it stay in
    /// the graph.
    /// </remarks>
    /// <exception cref="System.ArgumentNullException">Thrown when reader is null</exception>
    /// <exception cref="System.FormatException">Thrown for an invalid line, the message names the line number,
    /// also if the file declares more than <see cref="DefaultMaxImportVertices"/> vertices</exception>
    public int ImportDimacs(TextReader reader) => ImportDimacs(reader, DefaultMaxImportVertices);

    /// <summary>
    /// Default upper limit for the number of vertices a DIMACS file may declare (100 million; the
    /// largest DIMACS road network, USA, has about 24 million).
    /// </summary>
    /// <remarks>
    /// The problem line creates all declared vertices at once, so without a limit a few bytes of a
    /// broken or hostile file could make the import allocate all memory.
    /// </remarks>
    public const int DefaultMaxImportVertices = 100_000_000;

    /// <summary>
    /// Like <see cref="ImportDimacs(TextReader)"/>, with an own limit for the number of vertices the
    /// file may declare.
    /// </summary>
    /// <param name="reader">Source, e.g. a StreamReader for a .gr file</param>
    /// <param name="maxVertices">Largest number of vertices accepted in the problem line.</param>
    /// <returns>Number of edges added</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when reader is null</exception>
    /// <exception cref="System.ArgumentOutOfRangeException">Thrown when maxVertices is negative</exception>
    /// <exception cref="System.FormatException">Thrown for an invalid line or too many vertices, the
    /// message names the line number</exception>
    public int ImportDimacs(TextReader reader, int maxVertices)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxVertices);
        ArgumentNullException.ThrowIfNull(reader);

        int[]? index = null;  // vertex number -> vertex index
        int count = 0, lineNumber = 0;
        string? line;
        char[] separators = { ' ', '\t' };
        while ((line = reader.ReadLine()) != null)
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
                if (index != null)
                    throw ImportError(lineNumber, "more than one problem line");
                if (fields.Length != 4 || fields[1] != "sp" ||
                    !ulong.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out ulong declared) ||
                    !ulong.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    throw ImportError(lineNumber, "expected 'p sp VERTICES EDGES'");
                long limit = Math.Min(maxVertices, Array.MaxLength - 1L);
                if (declared > (ulong)limit)
                    throw ImportError(lineNumber, declared.ToString(CultureInfo.InvariantCulture) +
                        " vertices exceed the limit of " + limit.ToString(CultureInfo.InvariantCulture) +
                        " (see the maxVertices parameter)");
                int vertices = (int)declared;

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
    private static ReadOnlySpan<char> NextToken(ref ReadOnlySpan<char> text)
    {
        int start = 0;
        while (start < text.Length && (text[start] == ' ' || text[start] == '\t'))
            start++;
        int end = start;
        while (end < text.Length && text[end] != ' ' && text[end] != '\t')
            end++;
        ReadOnlySpan<char> token = text.Slice(start, end - start);
        text = text.Slice(end);
        return token;
    }
    /// <summary>
    /// Creates an import error with line number
    /// </summary>
    private static FormatException ImportError(int line, string message)
    {
        return new FormatException("Line " + line + ": " + message);
    }
    #endregion
}
