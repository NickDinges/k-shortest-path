using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FsCheck;
using FsCheck.Xunit;

namespace Eppstein.Tests
{
    /// <summary>
    /// Property-based tests (FsCheck): thousands of generated inputs per property; a failing input is
    /// shrunk to a minimal example. They complement the Go fuzz tests (go/eppstein/fuzz_test.go).
    /// </summary>
    public class PropertyTests
    {
        #region Import robustness
        /// <summary>Numbers that often break parsers: limits, signs, overflow.</summary>
        private static readonly string[] Numbers =
        {
            "0", "1", "2", "3", "-1", "-5", "999", "1000", "1001", "2147483647", "2147483648", "-2147483648",
            "9223372036854775807", "9223372036854775808", "-9223372036854775808", "18446744073709551616",
            "1e3", "0x10", "", "+3", "٣",  // Arabic-Indic digit three
        };

        private static readonly string[] Separators = { " ", "  ", "\t", "\v", "\f", " ", " ", "," };

        /// <summary>Lines that look like DIMACS, CSV or neither.</summary>
        private static Gen<string> LineGen()
        {
            Gen<string> number = Gen.Elements(Numbers);
            Gen<string> separator = Gen.Elements(Separators);
            Gen<string> word = Gen.Elements("p", "sp", "a", "c", "max", "A", "b1", "HOME", "1A", "x", "", "ü", "İ");
            Gen<string> token = Gen.OneOf(number, word);
            return Gen.OneOf(
                from s1 in separator from v in number from s2 in separator from e in number
                select "p" + s1 + "sp" + s2 + v + " " + e,
                from s1 in separator from x in number from s2 in separator from y in number from w in number
                select "a" + s1 + x + s2 + y + " " + w,
                from t1 in token from t2 in token from t3 in token select t1 + "," + t2 + "," + t3,
                from tokens in Gen.ListOf(token) from s in separator select string.Join(s, tokens),
                Arb.Default.String().Generator.Select(s => s ?? ""));
        }

        private static Arbitrary<string> ImportText() =>
            Arb.From(Gen.ListOf(LineGen()).Select(lines => string.Join(lines.Length % 2 == 0 ? "\n" : "\r\n", lines)));

        [Property(MaxTest = 3000)]
        public Property ImportDimacs_GivesConsistentGraphOrFormatException() =>
            Prop.ForAll(ImportText(), text => ImportsOrRejects(g => g.ImportDimacs(new StringReader(text), 1000)));

        [Property(MaxTest = 3000)]
        public Property ImportEdges_GivesConsistentGraphOrFormatException() =>
            Prop.ForAll(ImportText(), text => ImportsOrRejects(g => g.ImportEdges(new StringReader(text))));

        [Property(MaxTest = 1000)]
        public bool Imports_AcceptArbitraryStrings(string text) =>
            ImportsOrRejects(g => g.ImportDimacs(new StringReader(text ?? ""), 1000)) &&
            ImportsOrRejects(g => g.ImportEdges(new StringReader(text ?? "")));

        /// <summary>
        /// The import either succeeds with a consistent, queryable graph, or throws a FormatException
        /// naming the line. Any other exception fails the property.
        /// </summary>
        private static bool ImportsOrRejects(Func<Graph, int> import)
        {
            Graph g = new Graph();
            int count;
            try
            {
                count = import(g);
            }
            catch (FormatException e)
            {
                return e.Message.StartsWith("Line ", StringComparison.Ordinal);
            }

            if (count != g.EdgeCount)
                return false;
            foreach (Edge e in g.Edges)
            {
                if (e.Tail.Index >= g.VertexCount || e.Head.Index >= g.VertexCount)
                    return false;
            }
            if (g.VertexCount > 0)
            {
                // Weights may be huge (sums overflow), so only check that querying works
                foreach (GraphPath p in g.EnumeratePaths(0, g.VertexCount - 1).Take(5))
                {
                    if (!p.IsValid)
                        return false;
                }
            }
            return true;
        }
        #endregion

        #region Paths against brute force
        public sealed class SmallGraph
        {
            public int VertexCount;
            public List<(int Tail, int Head, int Weight)> Edges = new();
            public int Start, Goal;
            public int ArcFlagRegions;  // 0: no arc flags
            public long? MaxDetour;

            public override string ToString() =>
                $"n={VertexCount} s={Start} t={Goal} arcFlags={ArcFlagRegions} maxDetour={MaxDetour} edges=" +
                string.Join(" ", Edges.Select(e => $"{e.Tail}->{e.Head}:{e.Weight}"));
        }

        private static Arbitrary<SmallGraph> SmallGraphs()
        {
            Gen<SmallGraph> gen =
                from n in Gen.Choose(2, 7)
                from edgeCount in Gen.Choose(0, 16)
                from edges in Gen.ListOf(edgeCount,
                    from a in Gen.Choose(0, n - 1) from b in Gen.Choose(0, n - 1) from w in Gen.Choose(1, 9)
                    select (a, b, w))
                from s in Gen.Choose(0, n - 1)
                from t in Gen.Choose(0, n - 1)
                from regions in Gen.Choose(0, 3)
                from detour in Gen.OneOf(Gen.Constant<long?>(null), Gen.Choose(0, 12).Select(d => (long?)d))
                where s != t
                select new SmallGraph
                {
                    VertexCount = n, Edges = edges.ToList(), Start = s, Goal = t,
                    ArcFlagRegions = regions, MaxDetour = detour,
                };
            return Arb.From(gen);
        }

        [Property(MaxTest = 1500)]
        public Property KShortestPaths_MatchBruteForce() =>
            Prop.ForAll(SmallGraphs(), sg =>
            {
                const long maxWeight = 25;
                List<long> expected = BoundedWalkWeights(sg, maxWeight, maxStates: 200000);
                if (expected == null || expected.Count > 5000)
                    return true;  // Too many walks for the brute force: skip

                Graph g = new Graph();
                for (int v = 0; v < sg.VertexCount; v++)
                    g.AddVertex("N" + v);
                foreach (var e in sg.Edges)
                    g.AddEdge(e.Tail, e.Head, e.Weight);
                if (sg.ArcFlagRegions > 0)
                    g.EnableArcFlags(sg.ArcFlagRegions);
                if (sg.MaxDetour.HasValue)
                {
                    g.MaxDetour = sg.MaxDetour.Value;
                    if (expected.Count > 0)
                        expected = expected.Where(w => w <= expected[0] + sg.MaxDetour.Value).ToList();
                }

                List<long> actual = new List<long>();
                foreach (GraphPath p in g.EnumeratePaths(sg.Start, sg.Goal))
                {
                    if (p[0].Tail.Index != sg.Start || p[p.Count - 1].Head.Index != sg.Goal)
                        return false;
                    for (int i = 1; i < p.Count; i++)
                    {
                        if (p[i - 1].Head != p[i].Tail)
                            return false;
                    }
                    if (p.Weight > maxWeight || actual.Count > expected.Count)
                        break;
                    actual.Add(p.Weight);
                }
                return actual.SequenceEqual(expected);
            });

        /// <summary>
        /// Sorted weights of all start-goal walks up to maxWeight; null if the enumeration would visit
        /// more than maxStates states.
        /// </summary>
        private static List<long> BoundedWalkWeights(SmallGraph sg, long maxWeight, int maxStates)
        {
            List<long> weights = new List<long>();
            Stack<(int Vertex, long Weight)> stack = new Stack<(int, long)>();
            stack.Push((sg.Start, 0));
            for (int visited = 0; stack.Count > 0; visited++)
            {
                if (visited > maxStates)
                    return null;
                var (vertex, weight) = stack.Pop();
                if (vertex == sg.Goal && weight > 0)
                    weights.Add(weight);
                foreach (var e in sg.Edges)
                {
                    if (e.Tail == vertex && weight + e.Weight <= maxWeight)
                        stack.Push((e.Head, weight + e.Weight));
                }
            }
            weights.Sort();
            return weights;
        }
        #endregion
    }
}
