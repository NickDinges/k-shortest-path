using System;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// Original 2010 implementation (EppsteinV1) vs. the current library (Eppstein) on acyclic grids:
/// edges to the right and lower neighbor only (v1 does not terminate on graphs with cycles), weights
/// from SplitMix64 as in the other benchmarks. Query from corner to corner: first path, then the
/// next 999 paths. v1 enumerates all sidetrack combinations (= all paths) in the first query.
/// </summary>
static class Baseline
{
    static void Main(string[] args)
    {
        int maxSize = args.Length > 0 ? int.Parse(args[0]) : 12;
        double limitMs = 60_000;
        Console.WriteLine("size,paths,version,first_ms,next999_ms,memory_mb,weight1000");
        bool v1Done = false;
        for (int size = 3; size <= maxSize; size++)
        {
            var edges = Edges(size);
            string[] labels = Enumerable.Range(0, size * size).Select(v => "V" + (v % size) + "X" + (v / size)).ToArray();
            string from = labels[0], to = labels[size * size - 1];
            long paths = Binomial(2 * (size - 1), size - 1);

            // Current library
            {
                long before = GC.GetTotalMemory(true);
                var g = new Eppstein.Graph();
                foreach (string l in labels) g.AddVertex(l);
                foreach (var (t, h, w) in edges) g.AddEdge(t, h, w);
                var sw = Stopwatch.StartNew();
                var p = g.FindShortestPath(from, to);
                double first = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                long last = p.Weight;
                for (int i = 1; i < 1000 && p.IsValid; i++) { p = g.FindNextShortestPath(); if (p.IsValid) last = p.Weight; }
                double next = sw.Elapsed.TotalMilliseconds;
                double mb = (GC.GetTotalMemory(true) - before) / 1e6;
                GC.KeepAlive(g);
                Console.WriteLine($"{size},{paths},v2,{first:F3},{next:F3},{mb:F2},{last}");
            }
            if (v1Done)
                continue;
            // Original 2010 implementation
            {
                long before = GC.GetTotalMemory(true);
                var g = new EppsteinV1.Graph();
                g.CreateVertices(string.Join(",", labels));
                foreach (var (t, h, w) in edges) g.CreateEdges(labels[t], labels[h], w, "g");
                var sw = Stopwatch.StartNew();
                var p = g.FindShortestPath(from, to);
                double first = sw.Elapsed.TotalMilliseconds;
                double mb = (GC.GetTotalMemory(true) - before) / 1e6;
                sw.Restart();
                long last = p.Weight;
                for (int i = 1; i < 1000 && p.IsValid; i++) { p = g.FindNextShortestPath(); if (p.IsValid) last = p.Weight; }
                double next = sw.Elapsed.TotalMilliseconds;
                GC.KeepAlive(g);
                Console.WriteLine($"{size},{paths},v1,{first:F3},{next:F3},{mb:F2},{last}");
                v1Done = first > limitMs;  // larger grids would take too long
            }
        }
    }

    static (int, int, int)[] Edges(int size)
    {
        ulong state = 1;
        int Weight()
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return 1 + (int)((z ^ (z >> 31)) % 100);
        }
        var list = new System.Collections.Generic.List<(int, int, int)>();
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int v = y * size + x;
                if (x + 1 < size) list.Add((v, v + 1, Weight()));
                if (y + 1 < size) list.Add((v, v + size, Weight()));
            }
        return list.ToArray();
    }

    static long Binomial(int n, int k)
    {
        long r = 1;
        for (int i = 1; i <= k; i++) r = r * (n - k + i) / i;
        return r;
    }
}
