# k-shortest-path
http://www.ics.uci.edu/~eppstein/pubs/Epp-SJC-98.pdf

This paper presents an algorithm that finds multiple short paths connecting two terminals in a graph (allowing repeated vertices and edges in the paths) in constant time per path after a preprocessing stage dominated by a single-source shortest path computation. The paths it finds are the k shortest in the graph, where k is a parameter given as input to the algorithm.

The k shortest paths problem has many important applications for finding alternative solutions to geographic path planning problems, network routing, hypothesis generation in computational linguistics, and sequence alignment and metabolic pathway finding in bioinformatics. Although there have been many papers on the k shortest paths problem before and after this one, it has become frequently cited in those application areas. Additionally, it marks a boundary in the theoretical study of the problem: prior theoretical work largely concerned how quickly the problem could be solved, a line of research that was closed off by the optimal time bounds of this paper. Subsequent work has focused instead on devising efficient algorithms for more complex alternative formulations of the problem that avoid the repeated vertices and other shortcomings of the alternative paths produced by this formulation.

## Build and run (.NET 8, Linux / macOS / Windows)

```sh
dotnet build Eppstein2.sln
dotnet run --project Eppstein2 -- 20   # print the 20 shortest paths of the example graph
dotnet test Eppstein2.sln
```

## Graphviz output

`Graph.Serialize()` returns the graph in Graphviz DOT format (edge labels = weights, negative
edges dashed). After a query, `s` and `t` are double circles, vertices show their distance to `t`
and shortest path tree edges are bold; computed arc-flag regions are drawn as clusters.

```csharp
string dot = g.Serialize();                    // plain graph
dot = g.Serialize(path);                       // one path highlighted
dot = g.Serialize(new List<Path> { p1, p2 });  // several paths, one color each
g.Serialize(writer, paths);                    // write to any TextWriter
```

```sh
dotnet run --project Eppstein2 -- 5 --dot graph.dot   # first 3 paths highlighted
dot -Tsvg graph.dot -o graph.svg
```

## Paths with cycles

As in Eppstein's formulation, paths may repeat vertices and edges. The heap of candidate paths is
expanded lazily: each call of `FindNextShortestPath` extracts the next path and only then enqueues
its successors (the same sidetracks plus one more). This works on graphs with cycles, but such a
graph can have infinitely many s-t paths, so callers must limit the number of paths they request
instead of looping until an empty path is returned.

## Arc-flags optimization (optional)

The shortest path tree computation (backward Dijkstra from `t`) can be accelerated with
[arc flags](https://doi.org/10.1145/1210586.1210591). The vertices are split into regions; in a
preprocessing step every arc `(u,v)` gets one flag per region `r`, set if the arc lies on a shortest
path from `u` to some vertex of `r`. A query then only relaxes arcs flagged for the region of `t`.

Every shortest path `u -> t` uses only arcs flagged for `region(t)`, so all distances and sidetrack
deltas stay exact: the k shortest paths are the same as without arc flags. The feature is opt-in,
the existing API (`CreateVertices`, `CreateEdges`, `EdgeGroupWeights`, `FindShortestPath`,
`FindNextShortestPath`) is unchanged.

```csharp
Graph g = new Graph();
// ... CreateVertices / CreateEdges ...
g.EnableArcFlags("S,A,B,C;D,E,F;G,H,I,J,K,T"); // manual partition (unlisted vertices form one extra region)
// or: g.EnableArcFlags(3);                     // automatic partition into ~3 BFS-connected regions
g.PrecomputeArcFlags();                         // optional, otherwise done lazily on the next query
Path p = g.FindShortestPath("S", "T");
// g.DisableArcFlags();
```

Flags are recomputed automatically on the next query after `CreateEdges` or `EdgeGroupWeights`;
`CreateVertices` disables arc flags (the partition refers to the removed vertices).
Arc flags pay off when many queries are run on the same graph, since preprocessing runs one
Dijkstra per region boundary vertex.
