# Faster queries: corridor, arc flags and contraction hierarchy

All three are optional. The results never change, except that the corridor deliberately leaves out
paths that are too long.

## Corridor: only paths that are not much longer

In practice you rarely want *every* alternative: a route that is three times as long as the best
one is useless. Tell the library how much longer a path may be, and it only looks at the part of
the graph where such paths can lie:

```csharp
g.MaxDetourRatio = 0.2;   // paths at most 20% longer than the shortest one
// or an absolute limit, in the unit of your weights:
g.MaxDetour = 15;         // paths at most 15 minutes longer than the shortest one

foreach (GraphPath p in g.EnumeratePaths("HOME", "OFFICE"))   // ends by itself: finitely many paths
    Console.WriteLine(p);
```

Good to know:

- **Big speed-up on large graphs**, when start and goal are close to each other: the search only
  covers the area around the goal that is reachable within "shortest + detour". In a network
  with 1 million vertices, a repeated local query takes 3 ms instead of 78 ms (see [Performance](performance.md)).
- The number of paths within the limit is finite, so a loop over `EnumeratePaths` (or
  `while (p.IsValid)` with `FindNextShortestPath`) ends by itself, even with cycles.
- If you set both, the **smaller** limit applies.
- After changing a limit, call `FindShortestPath` again.
- Remove the limits with `g.MaxDetour = long.MaxValue;` and
  `g.MaxDetourRatio = double.PositiveInfinity;` (the defaults).

## Arc flags: preparation for many queries

*Rarely needed. Try the corridor first.*

> **Honest note:** Eppstein's algorithm needs the distance of *every* reachable vertex to the
> goal, so arc flags cannot skip vertices, only some edge checks. In our measurements queries
> became at most slightly faster, while the preparation takes about 1 s for 40,000 vertices.
> The corridor above is the much bigger lever. Arc flags stay available for graphs where many
> edges are never part of a shortest path.

**The idea in one sentence:** split the vertices into regions, and remember for every edge whether
it is ever useful for reaching each region. When you later search for routes into a region, edges
that are never useful for it are skipped.

Think of road signs: a small side street is never part of the fastest route to the airport, so a
search towards the airport can ignore it.

```csharp
g.EnableArcFlags(3);   // let the library split the graph into about 3 regions
// or choose regions yourself (separated by ';'), unlisted vertices form one extra region:
g.EnableArcFlags("S,A,B,C;D,E,F;G,H,I,J,K,T");

GraphPath p = g.FindShortestPath("S", "T");   // same results as before, possibly faster
```

Good to know:

- **Results are exactly the same** as without arc flags. Only the work behind the scenes changes.
- The preparation happens automatically on the next query, or right away with
  `g.PrecomputeArcFlags()`.
- Changing edges or weights is fine; the flags are recomputed automatically.
- `CreateVertices` turns arc flags off. Call `EnableArcFlags` again afterwards.
- The preparation costs time. It pays off only if you run **many** queries on the same graph.
- `g.DisableArcFlags()` switches the feature off again.

#### Better regions with an external partitioner

The built-in partition (`EnableArcFlags(3)`) simply cuts a breadth-first ordering into pieces. Good
regions have **few edges between them**; specialised graph partitioners find such regions. The
library can hand its graph to one and read the result back:

```csharp
using (var file = new StreamWriter("graph.metis"))
    g.WriteMetisGraph(file);                       // 1. graph in METIS format

// 2. run a partitioner, e.g. mt-KaHyPar (below), KaHIP or METIS

using (var file = new StreamReader("graph.part"))
    g.EnableArcFlagsFromPartition(file);           // 3. one region number per vertex
```

With [mt-KaHyPar](https://github.com/kahypar/mt-kahypar) from Python (`pip install mtkahypar`):

```python
import mtkahypar
init = mtkahypar.initialize(4)                                    # 4 threads
ctx = init.context_from_preset(mtkahypar.PresetType.DEFAULT)
ctx.set_partitioning_parameters(16, 0.03, mtkahypar.Objective.CUT)  # 16 regions, 3% imbalance
graph = init.graph_from_file("graph.metis", ctx, mtkahypar.FileFormat.METIS)
graph.partition(ctx).write_partition_to_file("graph.part")
```

`WriteMetisGraph` writes the graph undirected (METIS needs that), without self-loops, duplicate
and switched-off edges. The partition file has one line per vertex, in the order of the vertex
indices. If vertices are added later, arc flags switch off on the next query.

Measured on two-way grid streets with 40,000 vertices (results identical in all cases):

| Regions | Partition | Edges between regions | Preparation | Query (without arc flags) |
|---|---|---|---|---|
| 4 | built-in | 1,920 | 10.1 s | 7.3 ms (8.6 ms) |
| 4 | mt-KaHyPar | 918 | 9.0 s | 7.1 ms (8.6 ms) |
| 16 | built-in | 8,384 | 43.8 s | 8.8 ms (10.7 ms) |
| 16 | mt-KaHyPar | 2,736 | 26.1 s | 6.8 ms (8.5 ms) |

A good partition makes the preparation up to 1.7 times faster. The query gains about 20% either
way: as explained above, Eppstein's algorithm needs every distance, so arc flags can only skip
edge checks.

<details>
<summary>Technical details</summary>

Every arc `(u,v)` gets one flag per region `r`. The flag is set if the arc lies on a shortest path
from `u` to some vertex of `r`. To compute the flags, one backward Dijkstra runs from each boundary
vertex of each region. A query then only relaxes arcs flagged for the region of the goal `t`.
Every shortest path `u → t` uses only such arcs, so all distances stay exact.

</details>

## Contraction hierarchy: all distances much faster

*For many queries **without** a corridor on large, unchanging graphs.*

Every query starts with the distances of all vertices to the goal (step 1 in [How the algorithm works](how-it-works.md)). On a large
graph without a corridor that is most of the time of a query. A **contraction hierarchy** prepares
the graph once, so that these distances come out 3 to 10 times faster:

```csharp
g.EnableContractionHierarchy();
g.PrecomputeContractionHierarchy();   // optional: prepare now instead of on the first query

foreach (GraphPath p in g.EnumeratePaths("HOME", "OFFICE").Take(10))
    Console.WriteLine(p);
Console.WriteLine(g.ContractionHierarchyShortcuts + " shortcuts");
```

Measured on the benchmark grids ([Performance](performance.md), C#, results identical in all cases):

| Graph | Preparation | Shortcuts | Memory | Distances to the goal (repeated query): without → with |
|---|---|---|---|---|
| 40,000 vertices, one-way | 1.4 s | 110,482 | 7 → 10 MB | 29 → **7 ms** |
| 40,000 vertices, two-way | 3.1 s | 179,064 | 8 → 13 MB | 41 → **4 ms** |
| 1,000,000 vertices, one-way | 76 s | 3,053,922 | 117 → 203 MB | 258 → **95 ms** |
| 1,000,000 vertices, two-way | 101 s | 4,684,586 | 149 → 268 MB | 384 → **136 ms** |

Good to know:

- **It pays off only after many queries:** on the two-way grid with 1 million vertices, the
  preparation costs as much as about 400 queries save. Use it for a fixed graph that answers many
  questions (a server, a batch job).
- **Any change of the graph** (also `SetEdgeWeight`, `EdgeGroupWeights`) repeats the whole
  preparation on the next query. For graphs whose weights change often, leave it off.
- **With a corridor** (`MaxDetour`, `MaxDetourRatio`) the library keeps using Dijkstra: the corridor
  only visits the vertices near start and goal, while the hierarchy always computes all distances.
  A corridor is usually the better choice anyway ([Performance](performance.md): 3 ms instead of 78 ms).
- It speeds up the distances only. Finding the next paths (and the branch searches of
  `SimplePathsOnly`) is unchanged. Arc flags are not used while it is enabled.
- Results are exactly the same as without it; only paths of equal weight may come in another order.
- `g.DisableContractionHierarchy()` switches it off and frees the memory.

<details>
<summary>Technical details</summary>

The preparation removes ("contracts") the vertices one after the other, the least important first
(chosen by how many shortcuts removing them would need). When a vertex `v` is removed, a
**shortcut** `u → x` with weight `w(u,v) + w(v,x)` is added for each pair of neighbors whose
shortest connection ran through `v`; a small Dijkstra search (witness search) checks whether there
is another path that is as short. The order of removal is the vertex's *rank*. Every shortest path
then has an equally long path over edges and shortcuts that first only goes up in rank, then only
down ([Geisberger et al. 2008](how-it-works.md#further-reading)).

A query uses **PHAST** ([Delling et al.](how-it-works.md#further-reading)): a small search from the goal over
edges to higher-ranked vertices, then one linear sweep over all vertices from the highest to the
lowest rank, without any priority queue. The vertices are stored in sweep order, so the sweep reads
memory almost sequentially. Finally, the shortest-path tree is built from the original edges whose
weight matches the distances (breadth-first from the goal, so edges of weight 0 cannot form a
cycle). Negative (switched-off) edges and self-loops are ignored.

</details>

---

[Back to the main README](../README.md)
