using System;
using System.Diagnostics;
using System.Text;
using Eppstein;

/// <summary>
/// Simple timing benchmark on grid graphs (run in Release mode):
///   dotnet run -c Release --project Eppstein2.Benchmarks -- [size...] [--k N] [--local] [--ratio R] [--build strings|index|dimacs] [--two-way] [--simple] [--ch]
///       [--arc-flags N] [--reps N] [--weights]
/// --build chooses how the graph is built: CreateVertices/CreateEdges with labels (strings, default),
/// AddVertex/AddEdge by index (index), or ImportDimacs from a DIMACS text in memory (dimacs).
/// --ratio R limits paths to at most R times longer than the shortest one (MaxDetourRatio, corridor).
/// --ch enables the contraction hierarchy (PHAST for the shortest-path tree); its preprocessing time and
/// shortcut count are written to standard error, the first-path column then excludes it.
/// --arc-flags N enables arc flags with N regions (built-in partition); the preparation time goes to standard error.
/// --reps N repeats the first-path query N more times in the same process (steady state: optimized code,
/// warm caches) and --weights enumerates only the weights of the next paths (no path objects); see the
/// "stats:" line on standard error, which also reports the garbage collections, their pause time and the
/// bytes allocated while the next paths were enumerated.
/// --local searches between two nearby vertices in the middle of the grid (distance size/20
/// in both directions) instead of corner to corner.
/// Each grid vertex has edges to its right and lower neighbor (random weight 1..100)
/// and every 10th vertex an edge back to its left neighbor, so the graph has cycles.
/// The weights come from SplitMix64 (seed 1), implemented identically in the Go
/// (go/cmd/benchmark) and Rust (rust/src/bin/benchmark.rs) benchmarks: all three build the same
/// graphs, print the same table, and must report the same weight of the last path.
/// </summary>
class Benchmark
{
    static void Main(string[] args)
    {
        int k = 10000;
        bool local = false;
        bool twoWay = false;
        bool simple = false;
        bool ch = false;
        bool weightsOnly = false;
        int arcFlags = 0;
        int reps = 0;
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
            else if (args[i] == "--ch")
                ch = true;
            else if (args[i] == "--weights")
                weightsOnly = true;
            else if (args[i] == "--arc-flags" && i + 1 < args.Length)
                arcFlags = int.Parse(args[++i]);
            else if (args[i] == "--reps" && i + 1 < args.Length)
                reps = int.Parse(args[++i]);
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
            ", build: " + build + (twoWay ? ", two-way streets" : "") + (simple ? ", SimplePathsOnly" : "") + (ch ? ", contraction hierarchy" : "") +
            (arcFlags > 0 ? ", arc flags (" + arcFlags + " regions)" : "") + (weightsOnly ? ", weights only" : ""));
        Console.WriteLine("| grid | vertices | edges | build graph | memory | first path | first path (repeated query) | next paths | paths found | weight of last path |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (int size in sizes)
            Run(size, k, local, ratio, build, twoWay, simple, ch, arcFlags, reps, weightsOnly);
    }

    static void Run(int size, int k, bool local, double ratio, string build, bool twoWay, bool simple, bool ch, int arcFlags,
        int reps, bool weightsOnly)
    {
        // Edge list (vertex index = y * size + x), prepared before timing
        SplitMix rnd = new SplitMix(1);
        var edgeList = new System.Collections.Generic.List<(int Tail, int Head, int Weight)>();
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int v = y * size + x;
                if (x + 1 < size) edgeList.Add((v, v + 1, rnd.Weight()));
                if (y + 1 < size) edgeList.Add((v, v + size, rnd.Weight()));
                if (x > 0 && v % 10 == 0) edgeList.Add((v, v - 1, rnd.Weight()));
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

        if (ch)
        {
            g.EnableContractionHierarchy();
            watch.Restart();
            g.PrecomputeContractionHierarchy();
            Console.Error.WriteLine($"{size}x{size}: contraction {watch.Elapsed.TotalMilliseconds:F0} ms, {g.ContractionHierarchyShortcuts} shortcuts");
        }

        double arcFlagsTime = 0;
        if (arcFlags > 0)
        {
            g.EnableArcFlags(arcFlags);
            watch.Restart();
            g.PrecomputeArcFlags();
            arcFlagsTime = watch.Elapsed.TotalMilliseconds;
        }

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

        // Steady state: the same query again, reps times (optimized code after tiered compilation, warm caches)
        double[] warm = new double[Math.Max(reps, 1)];
        warm[0] = repeated;
        for (int i = 0; i < reps; i++)
        {
            watch.Restart();
            p = g.FindShortestPath(from * size + from, to * size + to);
            warm[i] = watch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(warm);

        int collections = GC.CollectionCount(0);
        TimeSpan pause = GC.GetTotalPauseDuration();
        long allocated = GC.GetTotalAllocatedBytes(false);
        long lastWeight = p.Weight;
        int found = p.IsValid ? 1 : 0;
        double next;
        if (weightsOnly)
        {
            // Weights without path objects: a new enumeration; the clock starts at its first result
            found = 0;
            next = 0;
            foreach (long weight in g.EnumeratePathWeights(from * size + from, to * size + to))
            {
                if (found == 0)
                    watch.Restart();
                lastWeight = weight;
                if (++found >= k)
                    break;
            }
            if (found > 0)
                next = watch.Elapsed.TotalMilliseconds;
        }
        else
        {
            watch.Restart();
            for (int i = 1; i < k && p.IsValid; i++)
            {
                p = g.FindNextShortestPath();
                if (p.IsValid)
                {
                    lastWeight = p.Weight;
                    found++;
                }
            }
            next = watch.Elapsed.TotalMilliseconds;
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Console.Error.WriteLine(string.Format(inv,
            "stats: warm_median_ms={0:F3} warm_min_ms={1:F3} gc_count={2} gc_pause_ms={3:F2} alloc_mb={4:F1} arcflags_ms={5:F0}",
            warm[warm.Length / 2], warm[0], GC.CollectionCount(0) - collections,
            (GC.GetTotalPauseDuration() - pause).TotalMilliseconds, (GC.GetTotalAllocatedBytes(false) - allocated) / 1e6,
            arcFlagsTime));

        Console.WriteLine("| " + size + "x" + size + " | " + size * size + " | " + edges + " | " + buildTime.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms | " + memory.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " MB | " +
            first.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms | " + repeated.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms | " + next.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms | " + found + " | " + lastWeight + " |");
    }
}

/// <summary>SplitMix64: tiny deterministic generator, identical in C#, Go and Rust.</summary>
sealed class SplitMix
{
    private ulong state;

    public SplitMix(ulong seed) => state = seed;

    public ulong Next()
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public int Weight() => 1 + (int)(Next() % 100);
}
