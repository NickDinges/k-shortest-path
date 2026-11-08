using System;
using System.Collections.Generic;
using System.Text;

namespace Eppstein
{
    /// <summary>
    /// Main application class
    /// </summary>
    class Program
    {
        /// <summary>
        /// Entry point
        /// </summary>
        /// <param name="args">optional: [k] [--dot file]
        /// k = maximum number of paths to print (default 20),
        /// file = Graphviz output with the first (up to 3) paths highlighted</param>
        static void Main(string[] args)
        {
            // Paths may repeat vertices (cycle C->C below), so there are infinitely many: limit them
            int k = 20;
            string dotFile = null;
            for (int a = 0; a < args.Length; a++)
            {
                if (args[a] == "--dot" && a + 1 < args.Length)
                    dotFile = args[++a];
                else if (!int.TryParse(args[a], out k) || k < 1)
                {
                    Console.WriteLine("Usage: Eppstein2 [k] [--dot file]");
                    Console.WriteLine("  k     number of paths to print (default 20)");
                    Console.WriteLine("  file  Graphviz DOT output, render with: dot -Tsvg file -o graph.svg");
                    return;
                }
            }

            Graph g = new Graph();

            g.CreateVertices("S,A,B,C,D,E,F,G,H,I,J,K,T");

            // Edges in Original Eppstein example (1997)
            g.CreateEdges("S",   "A",   2,  "alpha");
            g.CreateEdges("A,E", "B,I", 20, "alpha");
            g.CreateEdges("B,B", "C,F", 14, "alpha");
            g.CreateEdges("D",   "E",   9,  "alpha");
            g.CreateEdges("E",   "F",   10, "alpha");
            g.CreateEdges("F",   "G",   25, "alpha");
            g.CreateEdges("H",   "I",   18, "alpha");
            g.CreateEdges("I",   "J",   8,  "alpha");
            g.CreateEdges("J",   "T",   11, "alpha");
            g.CreateEdges("S",   "D",   13, "alpha");
            g.CreateEdges("A",   "E",   27, "alpha");
            g.CreateEdges("C,D", "G,H", 15, "alpha");
            g.CreateEdges("F",   "J",   12, "alpha");
            g.CreateEdges("G",   "T",   7,  "alpha");

            // Aditional edges for special cases testing
            g.CreateEdges("E", "J", 30, "beta");  // Diagonal edge
            g.CreateEdges("F", "T", 35, "beta");  // Diagonal edge
            g.CreateEdges("J", "K", 5,  "beta");  // Edge not pointing to shortest path
            g.CreateEdges("C", "C", 16, "beta");  // Cycling edge

//            g.EdgeGroupWeights("beta", -1);

            // Optional arc-flags speed-up (results are identical, preprocessing is done lazily)
//            g.EnableArcFlags("S,A,B,C;D,E,F;G,H,I,J,K,T");  // or: g.EnableArcFlags(3);

            Console.WriteLine("EPPSTEIN ALGORITHM TEST");

            System.Diagnostics.Stopwatch stp = new System.Diagnostics.Stopwatch();
            stp.Start();            
            Path p = g.FindShortestPath("S", "T");
            stp.Stop();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Calculation time: " + stp.ElapsedMilliseconds + " ms");
            Console.ResetColor();

            List<Path> found = new List<Path>();
            for (int i = 0; i < k && p.IsValid; i++)
            {
                Console.WriteLine(p.VertexNames + " (" + p.Weight + ")");
                found.Add(p);
                p = g.FindNextShortestPath();
            }

            if (dotFile != null)
            {
                System.IO.File.WriteAllText(dotFile, g.Serialize(found.GetRange(0, Math.Min(3, found.Count))));
                Console.WriteLine("Graphviz output written to " + dotFile);
            }

            Console.WriteLine("End.");
            if (!Console.IsInputRedirected)
                Console.ReadKey();
        }
    }
}
