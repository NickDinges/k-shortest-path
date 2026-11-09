using System;
using System.Diagnostics;
using System.Text;
using Eppstein;

/// <summary>
/// Simple timing benchmark on grid graphs (run in Release mode):
///   dotnet run -c Release --project Eppstein2.Benchmarks -- [size...] [--k N] [--local] [--ratio R] [--build strings|index|dimacs] [--two-way] [--simple]
/// --build chooses how the graph is built: CreateVertices/CreateEdges with labels (strings, default),
/// AddVertex/AddEdge by index (index), or ImportDimacs from a DIMACS text in memory (dimacs).
/// --ratio R limits paths to at most R times longer than the shortest one (MaxDetourRatio, corridor).
/// --local searches between two nearby vertices in the middle of the grid (distance size/20
/// in both directions) instead of corner to corner.
/// Each grid vertex has edges to its right and lower neighbor (random weight 1..100)
/// and every 10th vertex an edge back to its left neighbor, so the graph has cycles.
/// </summary>
class Benchmark
{
    static void Main(string[] args)
    {
        int k = 10000;
        bool local = false;
        bool twoWay = false;
        bool simple = false;
        double ratio = double.PositiveInfinity;
        string build = "strings";
        var sizes = new System.Collections.Generic.List<int>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--k" && i + 1 < args.Length)
                k = int.Parse(args[++i]);
            else if (args[i] == "--local")
                local = true;
            else if (args[i] == "--two-way")
                twoWay = true;
            else if (args[i] == "--simple")
                simple = true;
            else if (args[i] == "--build" && i + 1 < args.Length)
                build = args[++i];
            else if (args[i] == "--ratio" && i + 1 < args.Length)
                ratio = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            else
                sizes.Add(int.Parse(args[i]));
        }
        if (sizes.Count == 0)
            sizes.AddRange(new[] { 50, 100, 200 });

        Console.WriteLine((local ? "Local query (nearby vertices in the middle of the grid)" : "Corner to corner") +
            (double.IsPositiveInfinity(ratio) ? "" : ", MaxDetourRatio = " + ratio.ToString(System.Globalization.CultureInfo.InvariantCulture)) +
            ", build: " + build + (twoWay ? ", two-way streets" : "") + (simple ? ", SimplePathsOnly" : ""));
        Console.WriteLine("| grid | vertices | edges | build graph | memory | first path | first path (repeated query) | next paths | paths found | weight of last path |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (int size in sizes)
            Run(size, k, local, ratio, build, twoWay, simple);
    }

    static void Run(int size, int k, bool local, double ratio, string build, bool twoWay, bool simple)
    {
        // Edge list (vertex index = y * size + x), prepared before timing
        Random rnd = new Random(1);
        var edgeList = new System.Collections.Generic.List<(int Tail, int Head, int Weight)>();
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int v = y * size + x;
                if (x + 1 < size) edgeList.Add((v, v + 1, rnd.Next(1, 101)));
                if (y + 1 < size) edgeList.Add((v, v + size, rnd.Next(1, 101)));
                if (x > 0 && v % 10 == 0) edgeList.Add((v, v - 1, rnd.Next(1, 101)));
            }
        }
        if (twoWay)  // Every street in both directions (the back edges of the one-way grid are dropped)
        {
            var oneWay = edgeList.FindAll(e => e.Head > e.Tail);
            edgeList = new System.Collections.Generic.List<(int Tail, int Head, int Weight)>(oneWay);
            foreach (var e in oneWay)
                edgeList.Add((e.Head, e.Tail, e.Weight));
        }
        int edges = edgeList.Count;
        string[] labels = new string[size * size];
        for (int v = 0; v < labels.Length; v++)
            labels[v] = "V" + (v % size) + "X" + (v / size);
        string vertexList = string.Join(",", labels);
        string dimacs = null;
        if (build == "dimacs")
        {
            StringBuilder text = new StringBuilder("p sp " + labels.Length + " " + edges + "\n");
            foreach (var e in edgeList)
                text.Append("a ").Append(e.Tail + 1).Append(' ').Append(e.Head + 1).Append(' ').Append(e.Weight).Append('\n');
            dimacs = text.ToString();
        }

        long memoryBefore = GC.GetTotalMemory(true);
        Stopwatch watch = Stopwatch.StartNew();
        Graph g = new Graph();
        switch (build)
        {
            case "strings":  // Labels, parsed on every call
                g.CreateVertices(vertexList);
                foreach (var e in edgeList)
                    g.CreateEdges(labels[e.Tail], labels[e.Head], e.Weight, "g");
                break;
            case "index":  // Vertex indices
                foreach (string label in labels)
                    g.AddVertex(label);
                foreach (var e in edgeList)
                    g.AddEdge(e.Tail, e.Head, e.Weight, "g");
                break;
            case "dimacs":  // Bulk import (vertex n gets index n - 1)
                g.ImportDimacs(new System.IO.StringReader(dimacs));
                break;
            default:
                throw new ArgumentException("unknown --build " + build);
        }
        double buildTime = watch.Elapsed.TotalMilliseconds;

        watch.Restart();
        g.MaxDetourRatio = ratio;
        g.SimplePathsOnly = simple;
        int from = local ? size / 2 : 0;
        int to = local ? size / 2 + Math.Max(size / 20, 1) : size - 1;
        GraphPath p = g.FindShortestPath(from * size + from, to * size + to);
        double first = watch.Elapsed.TotalMilliseconds;

        double memory = (GC.GetTotalMemory(true) - memoryBefore) / 1e6;  // Graph with one query, in MB

        // Same query again: without one-time work after a graph change (e.g. arc flags, edge arrays)
        watch.Restart();
        p = g.FindShortestPath(from * size + from, to * size + to);
        double repeated = watch.Elapsed.TotalMilliseconds;

        watch.Restart();
        GraphPath last = p;
        int found = p.IsValid ? 1 : 0;
        for (int i = 1; i < k && p.IsValid; i++)
        {
            p = g.FindNextShortestPath();
            if (p.IsValid)
            {
                last = p;
                found++;
            }
        }
        double next = watch.Elapsed.TotalMilliseconds;

        Console.WriteLine("| " + size + "x" + size + " | " + size * size + " | " + edges + " | " + buildTime.ToString("F0") + " ms | " + memory.ToString("F0") + " MB | " +
            first.ToString("F0") + " ms | " + repeated.ToString("F0") + " ms | " + next.ToString("F0") + " ms | " + found + " | " + last.Weight + " |");
    }
}
