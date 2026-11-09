using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Eppstein;

// Demo: the k shortest paths of the example graph of Eppstein's paper (1998).
// Usage: Eppstein.Demo [k] [--dot file]
//   k     number of paths to print (default 20)
//   file  Graphviz output with the first (up to 3) paths highlighted

int k = 20;
string? dotFile = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--dot" && i + 1 < args.Length)
        dotFile = args[++i];
    else if (!int.TryParse(args[i], out k) || k < 1)
    {
        Console.WriteLine("Usage: Eppstein.Demo [k] [--dot file]");
        Console.WriteLine("  k     number of paths to print (default 20)");
        Console.WriteLine("  file  Graphviz DOT output, render with: dot -Tsvg file -o graph.svg");
        return 2;
    }
}

Graph g = new();
g.CreateVertices("S,A,B,C,D,E,F,G,H,I,J,K,T");

// Edges of the original example
g.CreateEdges("S", "A", 2, "alpha");
g.CreateEdges("A,E", "B,I", 20, "alpha");
g.CreateEdges("B,B", "C,F", 14, "alpha");
g.CreateEdges("D", "E", 9, "alpha");
g.CreateEdges("E", "F", 10, "alpha");
g.CreateEdges("F", "G", 25, "alpha");
g.CreateEdges("H", "I", 18, "alpha");
g.CreateEdges("I", "J", 8, "alpha");
g.CreateEdges("J", "T", 11, "alpha");
g.CreateEdges("S", "D", 13, "alpha");
g.CreateEdges("A", "E", 27, "alpha");
g.CreateEdges("C,D", "G,H", 15, "alpha");
g.CreateEdges("F", "J", 12, "alpha");
g.CreateEdges("G", "T", 7, "alpha");

// Additional edges for special cases
g.CreateEdges("E", "J", 30, "beta");  // Diagonal edge
g.CreateEdges("F", "T", 35, "beta");  // Diagonal edge
g.CreateEdges("J", "K", 5, "beta");   // Edge not leading to the goal
g.CreateEdges("C", "C", 16, "beta");  // Loop: paths may repeat it, so there are infinitely many paths

// g.EdgeGroupWeights("beta", -1);                       // switch the special edges off
// g.MaxDetourRatio = 0.2;                               // only paths at most 20% longer
// g.EnableArcFlags("S,A,B,C;D,E,F;G,H,I,J,K,T");        // speed-up for many queries

Console.WriteLine("EPPSTEIN ALGORITHM TEST");
Stopwatch watch = Stopwatch.StartNew();
List<GraphPath> found = g.EnumeratePaths("S", "T").Take(k).ToList();
watch.Stop();
Console.WriteLine("Calculation time: " + watch.ElapsedMilliseconds + " ms");

foreach (GraphPath p in found)
    Console.WriteLine(p.VertexNames + " (" + p.Weight + ")");

if (dotFile != null)
{
    File.WriteAllText(dotFile, g.Serialize(found.Take(3)));
    Console.WriteLine("Graphviz output written to " + dotFile);
}
Console.WriteLine("End.");
return 0;
