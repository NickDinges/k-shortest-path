using System;
using System.Collections.Generic;
using System.Linq;
using Eppstein;
using Xunit;

namespace Eppstein2.Tests
{
    public class GraphTests
    {
        private static Graph EppsteinExample()
        {
            Graph g = new Graph();
            g.CreateVertices("S,A,B,C,D,E,F,G,H,I,J,K,T");
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
            g.CreateEdges("E", "J", 30, "beta");
            g.CreateEdges("F", "T", 35, "beta");
            g.CreateEdges("J", "K", 5, "beta");
            g.CreateEdges("C", "C", 16, "beta");
            return g;
        }

        private static List<Path> Take(Graph g, string s, string t, int k)
        {
            List<Path> result = new List<Path>();
            Path p = g.FindShortestPath(s, t);
            while (p.IsValid && result.Count < k)
            {
                result.Add(p);
                p = g.FindNextShortestPath();
            }
            return result;
        }

        /// <summary>Weights of all s-t walks (repeated vertices allowed) up to a maximum weight</summary>
        private static List<int> BruteForceWalkWeights(int n, List<Tuple<int, int, int>> edges, int s, int t, int maxWeight)
        {
            List<int> weights = new List<int>();
            Stack<Tuple<int, int>> stack = new Stack<Tuple<int, int>>();
            stack.Push(Tuple.Create(s, 0));
            while (stack.Count > 0)
            {
                Tuple<int, int> cur = stack.Pop();
                if (cur.Item1 == t && cur.Item2 > 0)
                    weights.Add(cur.Item2);
                foreach (Tuple<int, int, int> e in edges)
                    if (e.Item1 == cur.Item1 && cur.Item2 + e.Item3 <= maxWeight)
                        stack.Push(Tuple.Create(e.Item2, cur.Item2 + e.Item3));
            }
            weights.Sort();
            return weights;
        }

        private static void AssertValidPath(Path p, string s, string t)
        {
            Assert.True(p[0].Tail.Equals(s));
            Assert.True(p[p.Count - 1].Head.Equals(t));
            for (int i = 1; i < p.Count; i++)
                Assert.Same(p[i - 1].Head, p[i].Tail);
        }

        [Fact]
        public void EppsteinExample_FirstPathsMatchReference()
        {
            string[] expected =
            {
                "S,D,E,F,J,T:55", "S,A,B,C,G,T:58", "S,A,B,F,J,T:59", "S,D,E,I,J,T:61",
                "S,A,E,F,J,T:62", "S,D,E,J,T:63", "S,D,E,F,G,T:64", "S,D,H,I,J,T:65",
                "S,D,E,F,T:67", "S,A,E,I,J,T:68", "S,A,B,F,G,T:68", "S,A,E,J,T:70",
                "S,A,E,F,G,T:71", "S,A,B,F,T:71", "S,A,E,F,T:74", "S,A,B,C,C,G,T:74",
                "S,A,B,C,C,C,G,T:90",
            };
            List<Path> paths = Take(EppsteinExample(), "S", "T", expected.Length);
            // Order among equal weights is not specified: compare weights in order, paths as sets
            Assert.Equal(expected.Select(x => int.Parse(x.Split(':')[1])), paths.Select(p => p.Weight));
            Assert.Equal(new HashSet<string>(expected), new HashSet<string>(paths.Select(p => p.VertexNames + ":" + p.Weight)));
        }

        [Fact]
        public void CyclicGraph_Terminates_AndRepeatsCycle()
        {
            Graph g = new Graph();
            g.CreateVertices("S,A,B,T");
            g.CreateEdges("S,A,B,A", "A,B,A,T", 1, "g");  // A <-> B cycle used to recurse forever
            List<Path> paths = Take(g, "S", "T", 5);
            Assert.Equal(new[] { 2, 4, 6, 8, 10 }, paths.Select(p => p.Weight));
            Assert.Equal("S,A,B,A,T", paths[1].VertexNames);
        }

        [Fact]
        public void UnreachableTarget_ReturnsEmptyPath()
        {
            Graph g = new Graph();
            g.CreateVertices("S,A,T");
            g.CreateEdges("S,A", "A,S", 1, "g");
            Assert.False(g.FindShortestPath("S", "T").IsValid);
            Assert.False(g.FindNextShortestPath().IsValid);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RandomCyclicGraphs_MatchBruteForce(bool arcFlags)
        {
            Random rnd = new Random(42);
            const int maxWeight = 25;
            for (int it = 0; it < 200; it++)
            {
                int n = rnd.Next(2, 8);
                string[] labels = Enumerable.Range(0, n).Select(i => "V" + i).ToArray();
                List<Tuple<int, int, int>> edges = new List<Tuple<int, int, int>>();
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        if (rnd.NextDouble() < 0.3)
                            edges.Add(Tuple.Create(i, j, rnd.Next(1, 10)));

                Graph g = new Graph();
                g.CreateVertices(string.Join(",", labels));
                foreach (Tuple<int, int, int> e in edges)
                    g.CreateEdges(labels[e.Item1], labels[e.Item2], e.Item3, "g");
                if (arcFlags)
                    Assert.True(g.EnableArcFlags(rnd.Next(1, 4)));

                int s = rnd.Next(n), t = rnd.Next(n);
                if (s == t)
                    continue;  // Empty path is reported as invalid

                List<int> expected = BruteForceWalkWeights(n, edges, s, t, maxWeight);
                List<Path> paths = Take(g, labels[s], labels[t], expected.Count + 1);
                foreach (Path p in paths)
                    AssertValidPath(p, labels[s], labels[t]);

                List<int> actual = paths.Select(p => p.Weight).TakeWhile(w => w <= maxWeight).ToList();
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void ArcFlags_GiveSameResults_AndFollowWeightChanges()
        {
            List<Path> plain = Take(EppsteinExample(), "S", "T", 30);
            Graph g = EppsteinExample();
            Assert.True(g.EnableArcFlags("S,A,B,C;D,E,F;G,H,I,J,K,T"));
            Assert.True(g.PrecomputeArcFlags());
            Assert.Equal(3, g.ArcFlagsRegionCount);
            Assert.Equal(plain.Select(p => p.Weight), Take(g, "S", "T", 30).Select(p => p.Weight));

            Graph h = EppsteinExample();
            h.EdgeGroupWeights("beta", 1);
            g.EdgeGroupWeights("beta", 1);  // Flags must be recomputed
            Assert.Equal(Take(h, "S", "T", 30).Select(p => p.Weight), Take(g, "S", "T", 30).Select(p => p.Weight));
        }

        [Fact]
        public void ArcFlags_InvalidPartitionRejected_AndDisable()
        {
            Graph g = EppsteinExample();
            Assert.False(g.EnableArcFlags("S,X"));
            Assert.False(g.EnableArcFlags("S,A;A"));
            Assert.False(g.ArcFlagsEnabled);
            Assert.True(g.EnableArcFlags(2));
            g.DisableArcFlags();
            Assert.Equal(0, g.ArcFlagsRegionCount);
        }
    }
}
