using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eppstein;
using Xunit;

namespace Eppstein.Tests
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

        private static List<GraphPath> Take(Graph g, string s, string t, int k)
        {
            List<GraphPath> result = new List<GraphPath>();
            GraphPath p = g.FindShortestPath(s, t);
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

        /// <summary>Tells if t can be reached from s</summary>
        private static bool Reachable(List<Tuple<int, int, int>> edges, int s, int t)
        {
            HashSet<int> seen = new HashSet<int> { s };
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                foreach (Tuple<int, int, int> e in edges)
                    if (e.Item1 == v && seen.Add(e.Item2))
                        queue.Enqueue(e.Item2);
            }
            return seen.Contains(t);
        }

        private static void AssertValidPath(GraphPath p, string s, string t)
        {
            Assert.Equal(s, p[0].Tail.Label);
            Assert.Equal(t, p[p.Count - 1].Head.Label);
            for (int i = 1; i < p.Count; i++)
                Assert.Equal(p[i - 1].Head, p[i].Tail);
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
            List<GraphPath> paths = Take(EppsteinExample(), "S", "T", expected.Length);
            // Order among equal weights is not specified: compare weights in order, paths as sets
            Assert.Equal(expected.Select(x => long.Parse(x.Split(':')[1])), paths.Select(p => p.Weight));
            Assert.Equal(new HashSet<string>(expected), new HashSet<string>(paths.Select(p => p.VertexNames + ":" + p.Weight)));
        }

        [Fact]
        public void CyclicGraph_Terminates_AndRepeatsCycle()
        {
            Graph g = new Graph();
            g.CreateVertices("S,A,B,T");
            g.CreateEdges("S,A,B,A", "A,B,A,T", 1, "g");  // A <-> B cycle used to recurse forever
            List<GraphPath> paths = Take(g, "S", "T", 5);
            Assert.Equal(new long[] { 2, 4, 6, 8, 10 }, paths.Select(p => p.Weight));
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
                List<GraphPath> paths = Take(g, labels[s], labels[t], expected.Count + 1);
                foreach (GraphPath p in paths)
                    AssertValidPath(p, labels[s], labels[t]);

                List<long> actual = paths.Select(p => p.Weight).TakeWhile(w => w <= maxWeight).ToList();
                Assert.Equal(expected.Select(w => (long)w), actual);
            }
        }

        [Fact]
        public void ArcFlags_GiveSameResults_AndFollowWeightChanges()
        {
            List<GraphPath> plain = Take(EppsteinExample(), "S", "T", 30);
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

        [Fact]
        public void LargeWeights_BeyondInt32_AreSupported()
        {
            const long big = 3000000000L;  // larger than int.MaxValue
            Graph g = new Graph();
            g.CreateVertices("S,A,B,T");
            g.CreateEdges("S,A", "A,T", big, "far");
            g.CreateEdges("S,B", "B,T", big + 1, "far2");
            List<GraphPath> paths = Take(g, "S", "T", 3);
            Assert.Equal(new[] { 2 * big, 2 * big + 2 }, paths.Select(p => p.Weight));
            Assert.Contains("label=\"3000000000\"", g.Serialize());

            g.EdgeGroupWeights("far", 5000000000L);
            Assert.Equal(2 * big + 2, g.FindShortestPath("S", "T").Weight);
        }

        [Fact]
        public void VertexLookup_IsCaseInsensitive_AndRecreatedWithVertices()
        {
            Graph g = new Graph();
            g.CreateVertices("Home,Work");
            Assert.True(g.CreateEdges("home", "WORK", 1, "x"));
            Assert.Equal("HOME,WORK", g.FindShortestPath("hOmE", "work").VertexNames);

            g.CreateVertices("Other");  // Old vertices must no longer be found
            Assert.False(g.CreateEdges("HOME", "OTHER", 1, "x"));
            Assert.False(g.FindShortestPath("HOME", "OTHER").IsValid);
        }

        [Fact]
        public void SidetrackHeap_MergeIsPersistentAndHeapOrdered()
        {
            Random rnd = new Random(7);
            SidetrackHeapNode heap = null;
            List<SidetrackHeapNode> versions = new List<SidetrackHeapNode>();
            List<List<long>> contents = new List<List<long>>();
            List<long> keys = new List<long>();
            for (int i = 0; i < 300; i++)
            {
                long key = rnd.Next(0, 50);
                heap = SidetrackHeapNode.Merge(heap, new SidetrackHeapNode(i, key));
                keys.Add(key);
                versions.Add(heap);
                contents.Add(keys.OrderBy(k => k).ToList());
            }
            // Every version still contains exactly its keys, in heap order (persistence)
            for (int i = 0; i < versions.Count; i += 37)
                Assert.Equal(contents[i], HeapKeys(versions[i]).OrderBy(k => k));
        }

        private static List<long> HeapKeys(SidetrackHeapNode node)
        {
            List<long> result = new List<long>();
            Stack<SidetrackHeapNode> stack = new Stack<SidetrackHeapNode>();
            if (node != null)
                stack.Push(node);
            while (stack.Count > 0)
            {
                SidetrackHeapNode n = stack.Pop();
                result.Add(n.Key);
                foreach (SidetrackHeapNode child in new[] { n.Left, n.Right })
                {
                    if (child != null)
                    {
                        Assert.True(child.Key >= n.Key, "heap order");
                        stack.Push(child);
                    }
                }
            }
            return result;
        }

        [Fact]
        public void LabelLookup_DoesNotDependOnCulture()
        {
            System.Globalization.CultureInfo before = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                // Turkish: "i".ToUpper() is the dotted capital I, which broke the lookup before
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
                Graph g = new Graph();
                Assert.True(g.CreateVertices("Izmir,Ankara"));
                Assert.True(g.CreateEdges("izmir", "ankara", 1, "x"));
                Assert.Equal(0, g.GetVertexIndex("IZMIR"));
                Assert.Equal("IZMIR,ANKARA", g.FindShortestPath("izmir", "Ankara").VertexNames);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = before;
            }
        }

        [Fact]
        public void SetEdgeWeight_InvalidatesArcFlagsAndQuery()
        {
            // Changing a weight directly on a public field used to leave arc flags outdated (wrong result)
            Graph g = new Graph();
            g.CreateVertices("S,A,B,T");
            g.CreateEdges("S,A", "A,T", 1, "x");
            g.CreateEdges("S,B", "B,T", 5, "y");
            Assert.True(g.EnableArcFlags("S,A,B;T"));
            GraphPath before = g.FindShortestPath("S", "T");
            Assert.Equal("S,A,T", before.VertexNames);
            foreach (Edge e in before)
                g.SetEdgeWeight(e, 100);
            Assert.False(g.FindNextShortestPath().IsValid);  // Query invalidated by the change
            GraphPath after = g.FindShortestPath("S", "T");
            Assert.Equal("S,B,T", after.VertexNames);
            Assert.Equal(10, after.Weight);
            Assert.Equal(2, before.Weight);  // A path keeps the weight it had when it was found

            Assert.Throws<ArgumentOutOfRangeException>(() => g.SetEdgeWeight(99, 1));
            Graph other = new Graph();
            other.CreateVertices("A,B");
            other.AddEdge(0, 1, 1);
            Assert.Throws<ArgumentException>(() => g.SetEdgeWeight(other.GetEdge(0), 1));  // Edge of another graph
        }

        [Fact]
        public void VertexAndEdge_AreViewsWithValueEquality()
        {
            Graph g = EppsteinExample();
            GraphPath p = g.FindShortestPath("S", "T");
            Vertex s = g.FindVertex("s").Value;
            Assert.Equal(s, p[0].Tail);
            Assert.True(s == g.GetVertex(s.Index));
            Assert.Equal(p[0], g.GetEdge(p[0].Id));
            Assert.NotEqual(EppsteinExample().GetVertex(s.Index), s);  // Same index, other graph
            Assert.Null(g.FindVertex("X"));
            Assert.Equal("S", s.ToString());
            Assert.Equal("S--13-->D", p[0].ToString());

            Assert.Equal(55, s.Distance);
            Assert.Equal(p[0], s.TreeEdge);
            Assert.Equal("D", s.Next.Value.Label);
            Assert.Null(g.FindVertex("T").Value.Next);
            Assert.Equal(new[] { "A", "D" }, s.OutEdges.Select(e => e.Head.Label).OrderBy(l => l));
            Assert.Equal(new[] { "F", "G", "J" }, g.FindVertex("T").Value.InEdges.Select(e => e.Tail.Label).OrderBy(l => l));
            Assert.Equal(p.Vertices.Select(v => v.Label), p.VertexNames.Split(','));

            s.Tag = "start";
            Assert.Equal("start", g.GetVertex(s.Index).Tag);
            Assert.Null(g.FindVertex("A").Value.Tag);

            Assert.Equal(21, g.EdgeCount);
            Assert.Equal(13, g.Vertices.Count());
            Assert.Equal(21, g.Edges.Count());
            Assert.Throws<ArgumentOutOfRangeException>(() => g.GetVertex(13));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.GetEdge(-1));
        }

        [Fact]
        public void VertexAndEdge_EqualityHashingAndQueryValues()
        {
            Graph g = EppsteinExample();
            GraphPath p = g.FindShortestPath("S", "T");
            Edge first = p[0];
            Vertex s = first.Tail;

            // Equality works in hash sets and with object.Equals
            Assert.Single(new HashSet<Vertex> { s, g.GetVertex(s.Index), first.Tail });
            Assert.Single(new HashSet<Edge> { first, g.GetEdge(first.Id) });
            Assert.True(s.Equals((object)g.GetVertex(s.Index)));
            Assert.False(s.Equals("S"));  // Not equal to its label
            Assert.True(first != p[1]);
            Assert.True(s != first.Head);
            Assert.False(first.Equals((object)null));

            Assert.Equal("ALPHA", first.Group);
            Assert.Equal(0, first.Delta);  // Tree edge of the shortest path
            Assert.Equal(-1, s.Region);    // No arc flags
            Assert.True(first.HasArcFlag(0));
            g.EnableArcFlags("S,A,B,C;D,E,F;G,H,I,J,K,T");
            g.FindShortestPath("S", "T");
            Assert.Equal(0, s.Region);
            Assert.True(g.FindVertex("D").Value.InEdges.First().HasArcFlag(1));

            Assert.Equal("(none)", default(Vertex).ToString());
            Assert.Equal("(none)", default(Edge).ToString());
        }

        [Fact]
        public void NewQuery_DoesNotShowDistancesOfThePreviousQuery()
        {
            // Distances are only valid for the current query (no reset of all vertices any more)
            Graph g = new Graph();
            g.CreateVertices("S,T,X1,X2");
            g.CreateEdges("S,X1,X2", "T,S,X1", 10, "g");
            g.FindShortestPath("X2", "T");
            Assert.Equal(30, g.FindVertex("X2").Value.Distance);
            g.MaxDetour = 5;
            g.FindShortestPath("S", "T");  // Corridor: X1 and X2 are not reached
            Assert.Equal(10, g.FindVertex("S").Value.Distance);
            Assert.Equal(Vertex.Unreached, g.FindVertex("X2").Value.Distance);
            Assert.Null(g.FindVertex("X2").Value.TreeEdge);
        }

        [Fact]
        public void EnumeratePaths_WorksWithLinq_AndDetectsChanges()
        {
            Graph g = EppsteinExample();
            List<long> expected = Take(EppsteinExample(), "S", "T", 20).Select(p => p.Weight).ToList();
            Assert.Equal(expected, g.EnumeratePaths("S", "T").Take(20).Select(p => p.Weight));
            Assert.Equal(expected, g.EnumeratePathWeights("S", "T").Take(20));
            Assert.Equal(expected.Take(5), g.EnumeratePathWeights(g.GetVertexIndex("S"), g.GetVertexIndex("T")).Take(5));
            Assert.Empty(g.EnumeratePaths("S", "X"));
            Assert.Empty(g.EnumeratePaths("T", "S"));

            g.MaxDetour = 4;
            Assert.Equal(new long[] { 55, 58, 59 }, g.EnumeratePathWeights("S", "T"));  // Finite with a corridor

            using IEnumerator<GraphPath> paths = g.EnumeratePaths("S", "T").GetEnumerator();
            Assert.True(paths.MoveNext());
            g.SetEdgeWeight(0, 3);
            Assert.Throws<InvalidOperationException>(() => paths.MoveNext());

            using IEnumerator<long> weights = g.EnumeratePathWeights("S", "T").GetEnumerator();
            Assert.True(weights.MoveNext());
            g.FindShortestPath("A", "T");  // Another query
            Assert.Throws<InvalidOperationException>(() => weights.MoveNext());
        }

        [Fact]
        public void GraphPath_IsImmutableWithStoredWeight()
        {
            Graph g = EppsteinExample();
            GraphPath p = g.FindShortestPath("S", "T");
            Assert.IsAssignableFrom<IReadOnlyList<Edge>>(p);
            Assert.Equal(p.Sum(e => e.Weight), p.Weight);
            Assert.Equal("S,D,E,F,J,T (55)", p.ToString());
            Assert.False(GraphPath.Empty.IsValid);
            Assert.Equal("(empty)", GraphPath.Empty.VertexNames);
            Assert.Empty(GraphPath.Empty.Vertices);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MaxDetour_ReturnsExactlyThePathsWithinTheCorridor(bool ratio)
        {
            Random rnd = new Random(11);
            int checkedGraphs = 0;
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
                if (rnd.Next(2) == 0)
                    g.EnableArcFlags(rnd.Next(1, 4));

                int s = rnd.Next(n), t = rnd.Next(n);
                if (s == t)
                    continue;
                if (!Reachable(edges, s, t))
                {
                    Assert.False(g.FindShortestPath(labels[s], labels[t]).IsValid);
                    continue;
                }
                const int bruteForceLimit = 45;
                List<int> all = BruteForceWalkWeights(n, edges, s, t, bruteForceLimit);
                if (all.Count == 0)
                    continue;  // Shortest path longer than the brute force limit

                long shortest = all[0];
                long limit;
                if (ratio)
                {
                    g.MaxDetourRatio = rnd.Next(0, 6) / 10.0;
                    limit = shortest + (long)Math.Floor(g.MaxDetourRatio * shortest);
                }
                else
                {
                    g.MaxDetour = rnd.Next(0, 15);
                    limit = shortest + g.MaxDetour;
                }
                if (limit > bruteForceLimit)
                    continue;  // Brute force would miss paths

                List<long> expected = all.Where(w => w <= limit).Select(w => (long)w).ToList();
                List<GraphPath> paths = Take(g, labels[s], labels[t], expected.Count + 5);
                Assert.Equal(expected, paths.Select(p => p.Weight));  // Then an empty path
                foreach (GraphPath p in paths)
                    AssertValidPath(p, labels[s], labels[t]);
                checkedGraphs++;
            }
            Assert.True(checkedGraphs >= 50, "only " + checkedGraphs + " graphs checked");
        }

        [Fact]
        public void MaxDetour_ValidatesValues_AndRequiresNewQuery()
        {
            Graph g = EppsteinExample();
            Assert.Equal(long.MaxValue, g.MaxDetour);
            Assert.True(double.IsPositiveInfinity(g.MaxDetourRatio));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.MaxDetour = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => g.MaxDetourRatio = -0.1);
            Assert.Throws<ArgumentOutOfRangeException>(() => g.MaxDetourRatio = double.NaN);

            Assert.Equal(55, g.FindShortestPath("S", "T").Weight);
            g.MaxDetour = 4;  // Paths 55, 58, 59
            Assert.False(g.FindNextShortestPath().IsValid);  // Changing the limit requires a new query
            Assert.Equal(new long[] { 55, 58, 59 }, Take(g, "S", "T", 10).Select(p => p.Weight));

            g.MaxDetour = long.MaxValue;
            g.MaxDetourRatio = 0.1;  // floor(5.5) = 5: paths up to 60
            Assert.Equal(new long[] { 55, 58, 59 }, Take(g, "S", "T", 10).Select(p => p.Weight));
            g.MaxDetour = 3;  // The smaller limit applies
            Assert.Equal(new long[] { 55, 58 }, Take(g, "S", "T", 10).Select(p => p.Weight));
        }

        [Fact]
        public void MaxDetour_StopsShortestPathTreeAtTheCorridor()
        {
            // Long chain behind the start: S <- X1 <- X2 <- ... is far away from T
            Graph g = new Graph();
            g.CreateVertices("S,T,X1,X2,X3");
            g.CreateEdges("S,X1,X2,X3", "T,S,X1,X2", 10, "g");
            g.MaxDetour = 5;
            Assert.Equal("S,T", g.FindShortestPath("S", "T").VertexNames);
            string dot = g.Serialize();
            Assert.Contains("\"S\" [label=\"S\\n10\"", dot);    // Distance computed
            Assert.Contains("\"X1\" [label=\"X1\"]", dot);         // Beyond the corridor: not computed
        }

        [Fact]
        public void PathEnumeration_CreatesAtMostThreeCandidatesPerPath()
        {
            Graph g = new Graph();
            int size = 30;
            Random rnd = new Random(3);
            List<string> names = new List<string>();
            for (int i = 0; i < size * size; i++)
                names.Add("V" + i);
            g.CreateVertices(string.Join(",", names));
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (x + 1 < size) g.CreateEdges(names[y * size + x], names[y * size + x + 1], rnd.Next(1, 100), "g");
                    if (y + 1 < size) g.CreateEdges(names[y * size + x], names[(y + 1) * size + x], rnd.Next(1, 100), "g");
                }
            List<GraphPath> paths = Take(g, names[0], names[size * size - 1], 2000);
            Assert.Equal(2000, paths.Count);
            Assert.True(g.CandidatesCreated <= 3 * paths.Count + 1, "candidates: " + g.CandidatesCreated);
            for (int i = 1; i < paths.Count; i++)
                Assert.True(paths[i - 1].Weight <= paths[i].Weight);
        }

        [Fact]
        public void AddVertex_ReturnsIndices_AndMixesWithCreateVertices()
        {
            Graph g = new Graph();
            g.CreateVertices("A,B");
            Assert.Equal(2, g.VertexCount);
            Assert.Equal(0, g.GetVertexIndex("a"));
            Assert.Equal(1, g.GetVertexIndex("B"));
            Assert.Equal(2, g.AddVertex("Home"));
            Assert.Equal(2, g.AddVertex("HOME"));   // Existing label (case-insensitive)
            Assert.Equal(-1, g.AddVertex("1A"));    // Invalid labels
            Assert.Equal(-1, g.AddVertex("A-B"));
            Assert.Equal(-1, g.AddVertex(""));
            Assert.Throws<ArgumentNullException>(() => g.AddVertex(null));
            Assert.Equal(-1, g.GetVertexIndex("X"));
            Assert.Equal(-1, g.GetVertexIndex(null));
            Assert.Equal(3, g.VertexCount);

            Assert.Equal(0, g.AddEdge(0, 2, 4));  // Returns the edge number
            Assert.Equal(1, g.AddEdge(0, 1, 1, "fast"));
            Assert.Equal(2, g.AddEdge(1, 2, 1, null));
            Assert.Equal(-1, g.AddEdge(0, 3, 1));
            Assert.Equal(-1, g.AddEdge(-1, 0, 1));
            Assert.True(g.CreateEdges("HOME", "a", 2, "x"));  // String and index methods mix

            Assert.Equal("A,B,HOME", g.FindShortestPath(0, 2).VertexNames);
            Assert.Equal(new long[] { 2, 4 }, Take(g, "A", "HOME", 5).Take(2).Select(p => p.Weight));
            Assert.False(g.FindShortestPath(0, 3).IsValid);
            Assert.False(g.FindShortestPath(-1, 0).IsValid);

            g.EdgeGroupWeights("FAST", 10);  // Group given to AddEdge, upper case
            Assert.Equal("A,HOME", g.FindShortestPath("A", "HOME").VertexNames);
        }

        [Fact]
        public void BuildingByIndexAndImports_GiveSameResultsAsCreateEdges()
        {
            Random rnd = new Random(5);
            for (int it = 0; it < 100; it++)
            {
                int n = rnd.Next(2, 9);
                List<Tuple<int, int, int>> edges = new List<Tuple<int, int, int>>();
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        if (rnd.NextDouble() < 0.3)
                            edges.Add(Tuple.Create(i, j, rnd.Next(1, 10)));
                // DIMACS numbers vertices from 1 and labels them V1..Vn
                string[] labels = Enumerable.Range(1, n).Select(i => "V" + i).ToArray();

                Graph byLabels = new Graph();
                byLabels.CreateVertices(string.Join(",", labels));
                foreach (var e in edges)
                    byLabels.CreateEdges(labels[e.Item1], labels[e.Item2], e.Item3, "g");

                Graph byIndex = new Graph();
                foreach (string label in labels)
                    byIndex.AddVertex(label);
                foreach (var e in edges)
                    byIndex.AddEdge(e.Item1, e.Item2, e.Item3, "g");

                Graph csv = new Graph();
                csv.CreateVertices(string.Join(",", labels));  // Keeps isolated vertices
                Assert.Equal(edges.Count, csv.ImportEdges(new StringReader(
                    string.Join("\n", edges.Select(e => labels[e.Item1] + "," + labels[e.Item2] + "," + e.Item3 + ",g")))));

                Graph dimacs = new Graph();
                Assert.Equal(edges.Count, dimacs.ImportDimacs(new StringReader(
                    "c random graph\np sp " + n + " " + edges.Count + "\n" +
                    string.Join("\n", edges.Select(e => "a " + (e.Item1 + 1) + " " + (e.Item2 + 1) + " " + e.Item3)))));

                int s = rnd.Next(n), t = rnd.Next(n);
                List<long> expected = Take(byLabels, labels[s], labels[t], 30).Select(p => p.Weight).ToList();
                foreach (Graph g in new[] { byIndex, csv, dimacs })
                {
                    Assert.Equal(n, g.VertexCount);
                    Assert.Equal(expected, Take(g, labels[s], labels[t], 30).Select(p => p.Weight));
                }
            }
        }

        [Fact]
        public void ImportEdges_ReadsCsv_AndReportsErrorsWithLineNumber()
        {
            Graph g = new Graph();
            int count = g.ImportEdges(new StringReader(
                "# tail,head,weight,group\n" +
                "home, park, 2\n" +
                "\n" +
                "  PARK ,office,7,Train  \n" +
                "HOME,OFFICE,10\n"));
            Assert.Equal(3, count);
            Assert.Equal(3, g.VertexCount);
            Assert.Equal("HOME,PARK,OFFICE", g.FindShortestPath("HOME", "OFFICE").VertexNames);
            g.EdgeGroupWeights("train", 100);
            Assert.Equal("HOME,OFFICE", g.FindShortestPath("HOME", "OFFICE").VertexNames);

            FormatException error = Assert.Throws<FormatException>(() => g.ImportEdges(new StringReader("A,B,1\nA,B\n")));
            Assert.StartsWith("Line 2:", error.Message);
            Assert.Equal(1, g.FindShortestPath("A", "B").Weight);  // Line 1 was imported
            Assert.StartsWith("Line 1:", Assert.Throws<FormatException>(() => g.ImportEdges(new StringReader("A,1B,1"))).Message);
            Assert.StartsWith("Line 1:", Assert.Throws<FormatException>(() => g.ImportEdges(new StringReader("A,B,x"))).Message);
            Assert.StartsWith("Line 1:", Assert.Throws<FormatException>(() => g.ImportEdges(new StringReader("A,B,1,g,h"))).Message);
            Assert.Throws<ArgumentNullException>(() => g.ImportEdges(null));
        }

        [Fact]
        public void ImportDimacs_ReadsGraph_AndReportsErrorsWithLineNumber()
        {
            Graph g = new Graph();
            int count = g.ImportDimacs(new StringReader(
                "c 9th DIMACS challenge format\n" +
                "p sp 3 3\n" +
                "a 1 2 5\n" +
                "a\t2  3 5\n" +
                "a 1 3 20\n"));
            Assert.Equal(3, count);
            Assert.Equal(new long[] { 10, 20 }, Take(g, "V1", "V3", 5).Select(p => p.Weight));
            // Any white space separates values (found by fuzzing: "\r " crashed the Go import)
            Assert.Equal(1, new Graph().ImportDimacs(new StringReader("\r \n\u2000\np\vsp 2 1\r\na\f1\u20002\t3\r\n")));
            Assert.Equal(0, g.GetVertexIndex("V1"));

            Func<string, string> error = text => Assert.Throws<FormatException>(() => new Graph().ImportDimacs(new StringReader(text))).Message;
            Assert.StartsWith("Line 1:", error("a 1 2 3"));                  // Edge before problem line
            Assert.StartsWith("Line 2:", error("p sp 2 1\na 1 3 1"));        // Vertex out of range
            Assert.StartsWith("Line 2:", error("p sp 2 1\na 1 2"));          // Missing weight
            Assert.StartsWith("Line 2:", error("p sp 2 1\na 1 2 3 4"));      // Too many values
            Assert.StartsWith("Line 2:", error("p sp 2 1\np sp 2 1"));       // Two problem lines
            Assert.StartsWith("Line 1:", error("p max 2 1"));                // Other problem type
            Assert.StartsWith("Line 1:", error("x 1 2"));                    // Unknown line type
            // Absurd vertex counts: a few bytes must not overflow or allocate all memory
            Assert.StartsWith("Line 1:", error("p sp 2147483647 1\na 1 2 3"));
            Assert.StartsWith("Line 1:", error("p sp 400000000 1\na 1 2 3"));
            Assert.StartsWith("Line 1:", error("p sp 18446744073709551616 1"));  // Beyond ulong
            Assert.StartsWith("Line 1:", error("p sp -5 1"));
            Assert.Contains("limit of 2", Assert.Throws<FormatException>(() => new Graph().ImportDimacs(new StringReader("p sp 3 0"), 2)).Message);
            Assert.Equal(0, new Graph().ImportDimacs(new StringReader("p sp 3 0"), 3));  // Raised limit
            Assert.Throws<ArgumentOutOfRangeException>(() => new Graph().ImportDimacs(new StringReader(""), -1));
            Assert.Throws<ArgumentNullException>(() => g.ImportDimacs(null));
        }

        /// <summary>All simple start-goal paths (no repeated vertex) as "weight:labels", sorted by weight.</summary>
        private static List<(long Weight, string Vertices)> BruteForceSimplePaths(int n, List<Tuple<int, int, int>> edges, int s, int t)
        {
            List<(long, string)> result = new List<(long, string)>();
            bool[] visited = new bool[n];
            List<int> path = new List<int> { s };
            void Visit(int v, long weight)
            {
                if (v == t)
                {
                    result.Add((weight, string.Join(",", path.Select(x => "V" + x))));
                    return;
                }
                visited[v] = true;
                foreach (var e in edges)
                {
                    if (e.Item1 == v && e.Item3 >= 0 && !visited[e.Item2])
                    {
                        path.Add(e.Item2);
                        Visit(e.Item2, weight + e.Item3);
                        path.RemoveAt(path.Count - 1);
                    }
                }
                visited[v] = false;
            }
            if (s != t)
                Visit(s, 0);
            return result.OrderBy(r => r.Item1).ToList();
        }

        [Theory]
        [InlineData(0)]  // plain
        [InlineData(1)]  // arc flags
        [InlineData(2)]  // corridor
        public void SimplePaths_MatchBruteForce(int variant)
        {
            Random rnd = new Random(23 + variant);
            int checkedGraphs = 0;
            for (int it = 0; it < 300; it++)
            {
                int n = rnd.Next(2, 9);
                string[] labels = Enumerable.Range(0, n).Select(i => "V" + i).ToArray();
                List<Tuple<int, int, int>> edges = new List<Tuple<int, int, int>>();
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        if (rnd.NextDouble() < 0.35)
                            edges.Add(Tuple.Create(i, j, rnd.Next(0, 10) - (rnd.NextDouble() < 0.05 ? 20 : 0)));

                Graph g = new Graph();
                g.CreateVertices(string.Join(",", labels));
                foreach (var e in edges)
                    g.CreateEdges(labels[e.Item1], labels[e.Item2], e.Item3, "g");
                g.SimplePathsOnly = true;
                if (variant == 1)
                    g.EnableArcFlags(rnd.Next(1, 4));

                int s = rnd.Next(n), t = rnd.Next(n);
                List<(long Weight, string Vertices)> expected = BruteForceSimplePaths(n, edges, s, t);
                if (variant == 2 && expected.Count > 0)
                {
                    g.MaxDetour = rnd.Next(0, 12);
                    expected = expected.Where(p => p.Weight <= expected[0].Weight + g.MaxDetour).ToList();
                }

                List<GraphPath> actual = g.EnumeratePaths(labels[s], labels[t]).Take(expected.Count + 5).ToList();
                Assert.Equal(expected.Select(p => p.Weight), actual.Select(p => p.Weight));
                foreach (var group in expected.GroupBy(p => p.Weight))
                    Assert.Equal(group.Select(p => p.Vertices).OrderBy(x => x, StringComparer.Ordinal),
                        actual.Where(p => p.Weight == group.Key).Select(p => p.VertexNames).OrderBy(x => x, StringComparer.Ordinal));
                foreach (GraphPath p in actual)
                    Assert.Equal(p.Count + 1, p.Vertices.Distinct().Count());  // Simple
                if (expected.Count > 0)
                    checkedGraphs++;
            }
            Assert.True(checkedGraphs >= 100, "only " + checkedGraphs + " graphs with paths");
        }

        [Fact]
        public void SimplePaths_TwoWayRoads_GiveRealAlternatives()
        {
            Graph g = new Graph();
            g.CreateVertices("HOME,PARK,BAKERY,STATION,OFFICE");
            foreach (var (a, b, w) in new[] { ("HOME", "PARK", 2), ("HOME", "BAKERY", 4), ("PARK", "BAKERY", 1),
                ("PARK", "STATION", 7), ("BAKERY", "STATION", 2), ("BAKERY", "OFFICE", 6), ("STATION", "OFFICE", 1) })
                g.CreateEdges(a + "," + b, b + "," + a, w, "road");  // Both directions

            // Default (Eppstein): the third path already drives back and forth
            Assert.Equal("HOME,PARK,BAKERY,STATION,OFFICE,STATION,OFFICE",
                g.EnumeratePaths("HOME", "OFFICE").ElementAt(2).VertexNames);

            g.SimplePathsOnly = true;
            List<GraphPath> paths = g.EnumeratePaths("HOME", "OFFICE").ToList();  // Finite: ends by itself
            // Exactly the 7 simple routes (checked by hand with a depth-first search), then the end
            Assert.Equal(new long[] { 6, 7, 9, 10, 10, 13, 17 }, paths.Select(p => p.Weight));
            Assert.Equal("HOME,PARK,BAKERY,STATION,OFFICE", paths[0].VertexNames);
            Assert.Equal("HOME,BAKERY,STATION,OFFICE", paths[1].VertexNames);
            Assert.Equal("HOME,PARK,BAKERY,OFFICE", paths[2].VertexNames);
            Assert.All(paths, p => Assert.Equal(p.Count + 1, p.Vertices.Distinct().Count()));
            Assert.Equal(paths.Select(p => p.Weight), g.EnumeratePathWeights("HOME", "OFFICE"));

            g.MaxDetourRatio = 0.5;  // Corridor: at most 9 minutes
            Assert.Equal(new long[] { 6, 7, 9 }, g.EnumeratePathWeights("HOME", "OFFICE"));
        }

        [Fact]
        public void SimplePaths_EdgeCasesAndLaziness()
        {
            Graph g = EppsteinExample();
            g.SimplePathsOnly = true;
            Assert.True(g.SimplePathsOnly);
            Assert.False(g.FindShortestPath("T", "S").IsValid);  // Unreachable
            Assert.False(g.FindShortestPath("C", "C").IsValid);  // Start = goal
            Assert.False(g.FindShortestPath("S", "X").IsValid);  // Unknown

            GraphPath first = g.FindShortestPath("S", "T");
            Assert.Equal("S,D,E,F,J,T (55)", first.ToString());
            Assert.Equal(0, g.SpurSearches);  // Spurs are only searched when the next path is requested
            Assert.True(g.FindNextShortestPath().IsValid);
            Assert.True(g.SpurSearches > 0);

            g.SimplePathsOnly = false;  // Changing the mode requires a new query
            Assert.False(g.FindNextShortestPath().IsValid);
            // Eppstein's example: 16 simple paths before the loop at C repeats; simple mode stops there
            g.SimplePathsOnly = true;
            Assert.DoesNotContain(g.EnumeratePaths("S", "T"), p => p.VertexNames.Contains("C,C"));
        }

        [Fact]
        public void WriteMetisGraph_WritesUndirectedSimpleGraph()
        {
            Graph g = new Graph();
            g.CreateVertices("A,B,C,D");
            g.CreateEdges("A,B,A,B,C,C", "B,A,B,C,C,D", 1, "x");  // A-B three times, self-loop C, C-D
            g.CreateEdges("D", "A", -1, "off");                    // Switched off: left out
            StringWriter writer = new StringWriter();
            g.WriteMetisGraph(writer);
            Assert.Equal(new[] { "4 3", "2", "1 3", "2 4", "3" },
                writer.ToString().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 || false).ToArray());

            // Every edge of the example appears in both directions of the adjacency lists
            StringWriter example = new StringWriter();
            EppsteinExample().WriteMetisGraph(example);
            string[] lines = example.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            int[] header = lines[0].Split(' ').Select(int.Parse).ToArray();
            List<int[]> adjacency = lines.Skip(1).Take(header[0])
                .Select(l => l.Length == 0 ? new int[0] : l.Split(' ').Select(int.Parse).ToArray()).ToList();
            Assert.Equal(13, header[0]);
            Assert.Equal(header[1] * 2, adjacency.Sum(a => a.Length));
            for (int v = 0; v < adjacency.Count; v++)
                foreach (int w in adjacency[v])
                    Assert.Contains(v + 1, adjacency[w - 1]);
        }

        [Fact]
        public void EnableArcFlagsFromPartition_UsesExternalPartition()
        {
            Graph g = EppsteinExample();
            List<long> plain = Take(EppsteinExample(), "S", "T", 30).Select(p => p.Weight).ToList();

            // Same regions as "S,A,B,C;D,E,F;G,H,I,J,K,T", one region number per vertex (in index order)
            g.EnableArcFlagsFromPartition(new StringReader("% from a partitioner\n0\n0\n0\n0\n1\n1\n1\n2\n2\n2\n2\n2\n2\n"));
            Assert.True(g.ArcFlagsEnabled);
            Assert.Equal(3, g.ArcFlagsRegionCount);
            Assert.Equal(1, g.FindVertex("E").Value.Region);
            Assert.Equal(plain, Take(g, "S", "T", 30).Select(p => p.Weight));

            Func<string, string> error = text => Assert.Throws<FormatException>(
                () => EppsteinExample().EnableArcFlagsFromPartition(new StringReader(text))).Message;
            Assert.Contains("13 vertices", error("0\n1\n"));                          // Too few lines
            Assert.StartsWith("Line 14:", error(string.Concat(Enumerable.Repeat("0\n", 14))));  // Too many
            Assert.StartsWith("Line 2:", error("0\n-1\n"));
            Assert.StartsWith("Line 1:", error("x\n"));
            Assert.StartsWith("Line 1:", error("13\n"));                               // Region >= VertexCount
            Assert.Throws<ArgumentNullException>(() => g.EnableArcFlagsFromPartition(null));

            g.AddVertex("NEW");  // Partition no longer fits: arc flags are disabled on the next query
            Assert.Equal(plain.Take(5), Take(g, "S", "T", 5).Select(p => p.Weight));
            Assert.False(g.ArcFlagsEnabled);
        }

        [Fact]
        public void Serialize_WritesAllVerticesAndEdges()
        {
            Graph g = EppsteinExample();
            string dot = g.Serialize();
            Assert.StartsWith("digraph Eppstein {", dot);
            Assert.EndsWith("}", dot.TrimEnd());
            foreach (string v in "S,A,B,C,D,E,F,G,H,I,J,K,T".Split(','))
                Assert.Contains("\"" + v + "\" [", dot);
            Assert.Equal(21, dot.Split('\n').Count(l => l.Contains(" -> ")));
            Assert.Contains("\"C\" -> \"C\" [label=\"16\", tooltip=\"group BETA\"]", dot);
            Assert.DoesNotContain("doublecircle", dot);  // No query yet
        }

        [Fact]
        public void Serialize_HighlightsPathsAndQueryState()
        {
            Graph g = EppsteinExample();
            List<GraphPath> paths = Take(g, "S", "T", 2);
            string dot = g.Serialize(paths);
            Assert.Contains("\"S\" [label=\"S\\n55\", shape=doublecircle", dot);
            Assert.Contains("\"S\" -> \"D\" [label=\"13\", tooltip=\"group ALPHA\", color=\"red\"", dot);
            Assert.Contains("\"S\" -> \"A\" [label=\"2\", tooltip=\"group ALPHA\", color=\"blue\"", dot);
            Assert.Contains("style=\"bold\"", dot);  // Shortest path tree
            Assert.Equal(g.Serialize(paths[0]), g.Serialize(new[] { paths[0] }));
        }

        [Fact]
        public void Serialize_NegativeEdgesDashed_AndRegionsAsClusters()
        {
            Graph g = EppsteinExample();
            g.EdgeGroupWeights("beta", -1);
            Assert.True(g.EnableArcFlags("S,A,B,C;D,E,F"));
            g.PrecomputeArcFlags();
            string dot = g.Serialize();
            Assert.Contains("\"J\" -> \"K\" [label=\"-1\", tooltip=\"group BETA\", style=\"dashed\"]", dot);
            Assert.Contains("subgraph cluster_0 {", dot);
            Assert.Contains("subgraph cluster_2 {", dot);  // Unlisted vertices
            Assert.Throws<ArgumentNullException>(() => g.Serialize(null, null));
        }
    }
}
