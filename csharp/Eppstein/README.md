# Eppstein.KShortestPaths

Find the shortest route between two points in a network, then the second shortest, the third
shortest, and so on (the *k shortest paths* problem).

- **Eppstein's algorithm:** after one computation, each further path comes out almost immediately.
- **Routes without loops:** `SimplePathsOnly` (Yen's algorithm with Lawler's improvement and A*).
- **Corridor:** `MaxDetour` and `MaxDetourRatio` limit how much longer than the best path a path
  may be, which makes queries between nearby vertices fast on large graphs.
- **Optional preparation:** arc flags and a contraction hierarchy (PHAST), with identical results.
- CSV and DIMACS import, Graphviz (DOT) output. No dependencies. .NET 8 and .NET 10.

```csharp
using System;
using Eppstein;

Graph g = new Graph();
g.CreateVertices("HOME,PARK,BAKERY,STATION,OFFICE");
g.CreateEdges("HOME",   "PARK",    2, "walk");   // from, to, weight, group name
g.CreateEdges("HOME",   "BAKERY",  4, "walk");
g.CreateEdges("PARK",   "BAKERY",  1, "walk");
g.CreateEdges("BAKERY", "STATION", 2, "walk");
g.CreateEdges("BAKERY", "OFFICE",  6, "walk");
g.CreateEdges("STATION","OFFICE",  1, "train");

GraphPath p = g.FindShortestPath("HOME", "OFFICE");
for (int i = 1; i <= 3 && p.IsValid; i++)
{
    Console.WriteLine(p.VertexNames + " (" + p.Weight + ")");
    p = g.FindNextShortestPath();
}
```

Documentation: [user guide and API reference](https://github.com/NickDinges/k-shortest-path/blob/master/docs/guide.md),
[project page](https://github.com/NickDinges/k-shortest-path). The same library exists for Go and
Rust in the same repository.
