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
        /// <param name="args">arguments list, not used</param>
        static void Main(string[] args)
        {
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

            Console.WriteLine("EPPSTEIN ALGORITHM TEST");

            System.Diagnostics.Stopwatch stp = new System.Diagnostics.Stopwatch();
            stp.Start();            
            Path p = g.FindShortestPath("S", "T");
            stp.Stop();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Calculation time: " + stp.ElapsedMilliseconds + " ms");
            Console.ResetColor();

            while (p.IsValid)  // This can be replaced by something like: while (p!=null)
            {
                Console.WriteLine(p.VertexNames + " (" + p.Weight + ")");
                p = g.FindNextShortestPath();
            }

            Console.WriteLine("End.");
            Console.ReadKey();
        }
    }
}
