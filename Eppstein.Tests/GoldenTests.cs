using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Eppstein;
using Xunit;

namespace Eppstein.Tests
{
    /// <summary>
    /// Shared golden test data in testdata/golden, checked by the C# and the Go test suites, so both
    /// implementations return the same paths. The order among paths of equal weight is not compared.
    /// </summary>
    public class GoldenTests
    {
        private sealed class GoldenPath
        {
            public long Weight { get; set; }
            public string Vertices { get; set; }
        }

        private sealed class GoldenCase
        {
            public string Name { get; set; }
            public string Graph { get; set; }
            public string From { get; set; }
            public string To { get; set; }
            public long? MaxDetour { get; set; }
            public double? MaxDetourRatio { get; set; }
            public bool SimplePathsOnly { get; set; }
            public bool Complete { get; set; }
            public List<GoldenPath> Paths { get; set; }
        }

        private static string GoldenDirectory()
        {
            for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "testdata", "golden");
                if (Directory.Exists(candidate))
                    return candidate;
            }
            throw new DirectoryNotFoundException("testdata/golden not found");
        }

        public static IEnumerable<object[]> CaseNames()
        {
            string json = File.ReadAllText(System.IO.Path.Combine(GoldenDirectory(), "cases.json"));
            foreach (GoldenCase c in JsonSerializer.Deserialize<List<GoldenCase>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }))
                yield return new object[] { c.Name };
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void GoldenCase_MatchesExpectedPaths(string name)
        {
            string dir = GoldenDirectory();
            GoldenCase c = JsonSerializer.Deserialize<List<GoldenCase>>(File.ReadAllText(System.IO.Path.Combine(dir, "cases.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).Single(x => x.Name == name);

            Graph g = new Graph();
            using (StreamReader reader = new StreamReader(System.IO.Path.Combine(dir, c.Graph)))
            {
                if (c.Graph.EndsWith(".gr"))
                    g.ImportDimacs(reader);
                else
                    g.ImportEdges(reader);
            }
            if (c.MaxDetour.HasValue)
                g.MaxDetour = c.MaxDetour.Value;
            if (c.MaxDetourRatio.HasValue)
                g.MaxDetourRatio = c.MaxDetourRatio.Value;
            g.SimplePathsOnly = c.SimplePathsOnly;

            // Complete: all paths must come and then an empty path. Otherwise: all paths up to the
            // weight of the last expected one.
            long lastWeight = c.Paths.Count == 0 ? long.MinValue : c.Paths[c.Paths.Count - 1].Weight;
            List<GoldenPath> actual = new List<GoldenPath>();
            for (GraphPath p = g.FindShortestPath(c.From, c.To); p.IsValid; p = g.FindNextShortestPath())
            {
                if (!c.Complete && p.Weight > lastWeight)
                    break;
                actual.Add(new GoldenPath { Weight = p.Weight, Vertices = p.VertexNames });
                Assert.True(actual.Count <= c.Paths.Count, "more paths than expected");
            }

            Assert.Equal(c.Paths.Select(p => p.Weight), actual.Select(p => p.Weight));
            foreach (var group in c.Paths.GroupBy(p => p.Weight))
                Assert.Equal(group.Select(p => p.Vertices).OrderBy(v => v, StringComparer.Ordinal),
                    actual.Where(p => p.Weight == group.Key).Select(p => p.Vertices).OrderBy(v => v, StringComparer.Ordinal));
        }
    }
}
