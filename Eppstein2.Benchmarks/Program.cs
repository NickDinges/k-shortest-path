using System;
using System.Diagnostics;
using System.Text;
using Eppstein;
using Path = Eppstein.Path;

/// <summary>
/// Simple timing benchmark on grid graphs (run in Release mode):
///   dotnet run -c Release --project Eppstein2.Benchmarks -- [size...] [--k N] [--local] [--ratio R]
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
        double ratio = double.PositiveInfinity;
        var sizes = new System.Collections.Generic.List<int>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--k" && i + 1 < args.Length)
                k = int.Parse(args[++i]);
            else if (args[i] == "--local")
                local = true;
            else if (args[i] == "--ratio" && i + 1 < args.Length)
                ratio = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            else
                sizes.Add(int.Parse(args[i]));
        }
        if (sizes.Count == 0)
            sizes.AddRange(new[] { 50, 100, 200 });

        Console.WriteLine((local ? "Local query (nearby vertices in the middle of the grid)" : "Corner to corner") +
            (double.IsPositiveInfinity(ratio) ? "" : ", MaxDetourRatio = " + ratio.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Console.WriteLine("| grid | vertices | edges | build graph | first path | first path (repeated query) | next paths | paths found | weight of last path |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
        foreach (int size in sizes)
            Run(size, k, local, ratio);
    }

    static void Run(int size, int k, bool local, double ratio)
    {
        Random rnd = new Random(1);
        Func<int, int, string> name = (x, y) => "V" + x + "X" + y;

        StringBuilder vertices = new StringBuilder();
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                vertices.Append(name(x, y)).Append(',');

        Stopwatch watch = Stopwatch.StartNew();
        Graph g = new Graph();
        g.CreateVertices(vertices.ToString());
        int edges = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (x + 1 < size) { g.CreateEdges(name(x, y), name(x + 1, y), rnd.Next(1, 101), "g"); edges++; }
                if (y + 1 < size) { g.CreateEdges(name(x, y), name(x, y + 1), rnd.Next(1, 101), "g"); edges++; }
                if (x > 0 && (x + y * size) % 10 == 0) { g.CreateEdges(name(x, y), name(x - 1, y), rnd.Next(1, 101), "g"); edges++; }
            }
        }
        double build = watch.Elapsed.TotalMilliseconds;

        watch.Restart();
        g.MaxDetourRatio = ratio;
        int from = local ? size / 2 : 0;
        int to = local ? size / 2 + Math.Max(size / 20, 1) : size - 1;
        Path p = g.FindShortestPath(name(from, from), name(to, to));
        double first = watch.Elapsed.TotalMilliseconds;

        // Same query again: without one-time work after a graph change (e.g. arc flags, edge arrays)
        watch.Restart();
        p = g.FindShortestPath(name(from, from), name(to, to));
        double repeated = watch.Elapsed.TotalMilliseconds;

        watch.Restart();
        Path last = p;
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

        Console.WriteLine("| " + size + "x" + size + " | " + size * size + " | " + edges + " | " + build.ToString("F0") + " ms | " +
            first.ToString("F0") + " ms | " + repeated.ToString("F0") + " ms | " + next.ToString("F0") + " ms | " + found + " | " + last.Weight + " |");
    }
}
