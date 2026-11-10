# Performance

The library handles graphs with millions of edges. Measured on grid graphs (each vertex connected
to its right and lower neighbor, every 10th vertex also back to the left, random weights 1–100),
in Release mode on a 4-core cloud VM.

**From one corner to the opposite one** (paths cross the whole graph; built by index):

| Vertices | Edges | Build graph | Memory | First path | First path, repeated query | Next 10,000 paths |
|---|---|---|---|---|---|---|
| 2,500 | 5,100 | 7 ms | < 1 MB | 9 ms | 4 ms | 46 ms |
| 10,000 | 20,700 | 8 ms | 2 MB | 10 ms | 9 ms | 59 ms |
| 40,000 | 83,400 | 14 ms | 7 MB | 38 ms | 33 ms | 70 ms |
| 160,000 | 334,800 | 44 ms | 26 MB | 52 ms | 37 ms | 74 ms |
| 1,000,000 | 2,097,000 | 285 ms | 117 MB | 376 ms | 320 ms | 324 ms |

**Building the graph** with 1,000,000 vertices and 2,097,000 edges ([API reference](guide.md#api-reference)):

| Way of building | Time |
|---|---|
| `CreateVertices` + `CreateEdges` (by name) | 1.2 s |
| `AddVertex` + `AddEdge` (by index) | **0.29 s** |
| `ImportDimacs` (from text in memory) | 0.7 s |

**Between two nearby vertices** in the middle of the 1,000,000-vertex grid, with and without a
corridor ([Faster queries](faster-queries.md)):

| Corridor | First path | First path, repeated query | Next paths | Paths found |
|---|---|---|---|---|
| none | 273 ms | 78 ms | 32 ms | 10,000 (limit of the benchmark) |
| `MaxDetourRatio = 0.1` | 87 ms | **3 ms** | 35 ms | 10,000 (limit of the benchmark) |
| `MaxDetourRatio = 0.02` | 82 ms | **4 ms** | 4 ms | 1,169 (all within 2%) |

The first query after building or changing the graph includes one-time preparation (sorting the
edges by direction); the "repeated query" column shows a query without it.

**Simple paths** (`SimplePathsOnly`, [Routes without loops](simple-paths.md)) on the same grids with **two-way streets**, from
corner to corner, time for the routes after the first one:

| Vertices | Edges | 10 routes | 100 routes | 1,000 routes | Walks (default), 1,000 paths |
|---|---|---|---|---|---|
| 10,000 | 39,600 | 103 ms | 491 ms | 1.5 s | 5 ms |
| 40,000 | 159,200 | 124 ms | 431 ms | 3.5 s | 12 ms |

Between two nearby points of the two-way grid with 1,000,000 vertices:

| Routes | Without corridor: first route (repeated query) + further routes | With `MaxDetourRatio = 0.1` |
|---|---|---|
| 3 | 371 ms + 7 ms | 6 ms + 6 ms |
| 10 | 459 ms + 13 ms | 18 ms + 18 ms |
| 100 | 367 ms + 73 ms | 19 ms + 104 ms |

What makes it fast:

- **Compact storage:** vertices and edges live in plain arrays: 117 MB for the graph with
  1,000,000 vertices and 2,097,000 edges (version 1 needed 476 MB). That also makes the searches
  more cache-friendly. `Vertex`,
  `Edge` and the edges of a `GraphPath` are small values pointing into these arrays.
- **No cleanup between queries:** each query has a number, and a distance only counts if it was
  computed by the current query. So a query only touches the vertices it reaches.
- **Vertex lookup by name** uses a dictionary; building by index (`AddVertex`/`AddEdge`) skips the
  name lookup completely.
- **Priority queues** are binary heaps (O(log n) per operation).
- **At most three candidates per path**, thanks to the persistent detour heaps ([How the algorithm works](how-it-works.md)).
  Candidates store only their parent and one detour, not a copy of all detours.
- **The corridor** stops the shortest-path search where no allowed path can lie.
- **Optional contraction hierarchy:** for many queries without a corridor, the distances of all
  vertices come from one linear sweep (PHAST) instead of Dijkstra, 3 to 10 times faster ([Faster queries](faster-queries.md)).
- **Simple paths:** Lawler's rule saves most branch searches, and the remaining ones run as A*
  towards the goal, guided by the distances that are already known ([How the algorithm works](how-it-works.md)).
- **Edges are stored by direction**, so the searches never look at edges going the wrong way.

The benchmark now draws the weights from SplitMix64, identically in C#, Go (`go/cmd/benchmark`) and
Rust (`rust/src/bin/benchmark.rs`), so all three build the same graphs; the tables above were
measured earlier with `System.Random` (same weight distribution). The cross-language comparison is
in the [paper](../paper/README.md).

Run the benchmark yourself (sizes are grid widths; `--k` = number of paths, `--local` = nearby
vertices, `--ratio` = `MaxDetourRatio`, `--build strings|index|dimacs` = way of building,
`--two-way` = every street in both directions, `--simple` = `SimplePathsOnly`, `--ch` = contraction
hierarchy, its preparation time is written to standard error):

```sh
dotnet run -c Release --project csharp/Eppstein.Benchmarks -- 50 100 200 --k 10000
dotnet run -c Release --project csharp/Eppstein.Benchmarks -- 1000 --local --ratio 0.1
dotnet run -c Release --project csharp/Eppstein.Benchmarks -- 1000 --build index
dotnet run -c Release --project csharp/Eppstein.Benchmarks -- 100 200 --two-way --simple --k 100
dotnet run -c Release --project csharp/Eppstein.Benchmarks -- 200 1000 --build index --ch
```

---

[Back to the main README](../README.md)
