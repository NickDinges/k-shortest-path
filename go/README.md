# k-shortest-path (Go)

A Go port of the C# library in this repository. It finds the **shortest route** between two
points in a network, then the **second shortest**, the **third shortest**, and so on, using
[Eppstein's algorithm](http://www.ics.uci.edu/~eppstein/pubs/Epp-SJC-98.pdf).

The API, the behavior and the Graphviz output are the same as in the C# version. The
[main README](../README.md) explains the concepts (graph, edge, weight, path, arc flags) step by
step; everything there applies here too. This page shows the Go-specific parts.

No dependencies outside the Go standard library. Requires **Go 1.23 or newer** (for `range` over the path iterators).

## Quick start

```sh
cd go
go run ./cmd/eppstein            # 20 shortest paths of the example graph from the paper
go run ./cmd/eppstein 5          # only 5 paths
go run ./cmd/eppstein 5 --dot graph.dot && dot -Tsvg graph.dot -o graph.svg
go test ./...                    # run the tests
go test -run '^$' -fuzz '^FuzzImportDimacs$' -fuzztime 1m ./eppstein   # fuzz one target
```

## Your first program

Create a new module and add the library:

```sh
mkdir myroutes && cd myroutes
go mod init myroutes
go get github.com/nickdinges/k-shortest-path/go/eppstein
```

> `go get` works once this code is on the repository's default branch and the repository is
> public. Until then, point Go to a local copy instead:
>
> ```sh
> go mod edit -replace github.com/nickdinges/k-shortest-path/go=../k-shortest-path/go
> go mod edit -require github.com/nickdinges/k-shortest-path/go@v0.0.0
> ```

`main.go`:

```go
package main

import (
	"fmt"

	"github.com/nickdinges/k-shortest-path/go/eppstein"
)

func main() {
	g := eppstein.NewGraph()

	// 1. Create the places (vertices)
	g.CreateVertices("HOME,PARK,BAKERY,STATION,OFFICE")

	// 2. Create one-way roads (edges): from, to, minutes, group name
	g.CreateEdges("HOME", "PARK", 2, "walk")
	g.CreateEdges("HOME", "BAKERY", 4, "walk")
	g.CreateEdges("PARK", "BAKERY", 1, "walk")
	g.CreateEdges("PARK", "STATION", 7, "walk")
	g.CreateEdges("BAKERY", "STATION", 2, "walk")
	g.CreateEdges("BAKERY", "OFFICE", 6, "walk")
	g.CreateEdges("STATION", "OFFICE", 1, "train")

	// 3. Ask for the shortest route, then for the next ones
	p := g.FindShortestPath("HOME", "OFFICE")
	for i := 1; i <= 5 && p.IsValid(); i++ {
		fmt.Printf("%d. %s  (%d minutes)\n", i, p.VertexNames(), p.Weight())
		p = g.FindNextShortestPath()
	}
}
```

```text
$ go run .
1. HOME,PARK,BAKERY,STATION,OFFICE  (6 minutes)
2. HOME,BAKERY,STATION,OFFICE  (7 minutes)
3. HOME,PARK,BAKERY,OFFICE  (9 minutes)
4. HOME,BAKERY,OFFICE  (10 minutes)
5. HOME,PARK,STATION,OFFICE  (10 minutes)
```

This example is also a test (`eppstein/example_test.go`), so it is guaranteed to stay correct.

## API: C# → Go

The methods keep their names and their behavior. Go has no method overloading and no
properties, so a few names differ:

| C# | Go |
|---|---|
| `new Graph()` | `eppstein.NewGraph()` |
| `g.CreateVertices("A,B")` → `bool` | `g.CreateVertices("A,B")` → `bool` |
| `g.CreateEdges(tails, heads, weight, group)` → `bool` | `g.CreateEdges(tails, heads, weight, group)` → `bool` |
| `g.EdgeGroupWeights(group, weight)` | `g.EdgeGroupWeights(group, weight)` |
| `g.SetEdgeWeight(edge, weight)` | `g.SetEdgeWeight(edge, weight)` |
| `g.AddVertex("A")` → index | `g.AddVertex("A")` → index |
| `g.AddEdge(tail, head, weight)` → edge number | `g.AddEdge(tail, head, weight)` → edge number |
| `g.AddEdge(tail, head, weight, group)` | `g.AddEdgeGroup(tail, head, weight, group)` |
| `g.GetVertexIndex("A")`, `g.VertexCount`, `g.EdgeCount` | `g.VertexIndex("A")`, `g.VertexCount()`, `g.EdgeCount()` |
| `g.FindVertex("A")` → `Vertex?` | `g.FindVertex("A")` → `(Vertex, bool)` |
| `g.GetVertex(i)`, `g.GetEdge(id)` (throw if invalid) | `g.Vertex(i)`, `g.Edge(id)` (panic if invalid) |
| `g.Vertices`, `g.Edges` | `g.Vertices()`, `g.Edges()` (iterators) |
| `g.ImportEdges(reader)` → `int`, throws `FormatException` | `g.ImportEdges(r)` → `(int, error)` |
| `g.ImportDimacs(reader)` → `int`, throws `FormatException` | `g.ImportDimacs(r)` → `(int, error)` |
| `g.ImportDimacs(reader, maxVertices)`, `Graph.DefaultMaxImportVertices` | `g.ImportDimacsLimit(r, maxVertices)`, `eppstein.DefaultMaxImportVertices` |
| `g.FindShortestPath(s, t)` → `GraphPath` | `g.FindShortestPath(s, t)` → `Path` |
| `g.FindShortestPath(sIndex, tIndex)` | `g.FindShortestPathByIndex(sIndex, tIndex)` |
| `g.FindNextShortestPath()` → `GraphPath` | `g.FindNextShortestPath()` → `Path` |
| `foreach (var p in g.EnumeratePaths(s, t))` | `for p := range g.Paths(s, t)` (`PathsByIndex` for indices) |
| `g.EnumeratePathWeights(s, t)` | `g.PathWeights(s, t)` (`PathWeightsByIndex`) |
| enumeration after a change: `InvalidOperationException` | iteration after a change: panic |
| `g.MaxDetour = 15` / `g.MaxDetour` | `g.SetMaxDetour(15)` → `bool` / `g.MaxDetour()` |
| `g.MaxDetourRatio = 0.2` / `g.MaxDetourRatio` | `g.SetMaxDetourRatio(0.2)` → `bool` / `g.MaxDetourRatio()` |
| `g.SimplePathsOnly = true` / `g.SimplePathsOnly` | `g.SetSimplePathsOnly(true)` / `g.SimplePathsOnly()` |
| `long.MaxValue` / `double.PositiveInfinity` (no limit) | `math.MaxInt64` / `math.Inf(1)` |
| invalid limit: `ArgumentOutOfRangeException` | invalid limit: returns `false` |
| `g.EnableArcFlags(3)` | `g.EnableArcFlags(3)` |
| `g.EnableArcFlags("S,A;B,C")` | `g.EnableArcFlagsRegions("S,A;B,C")` |
| `g.EnableArcFlagsFromPartition(reader)` (throws `FormatException`) | `g.EnableArcFlagsFromPartition(r)` → `error` |
| `g.WriteMetisGraph(writer)` | `g.WriteMetisGraph(w)` → `error` |
| `g.DisableArcFlags()` / `g.PrecomputeArcFlags()` | `g.DisableArcFlags()` / `g.PrecomputeArcFlags()` |
| `g.ArcFlagsEnabled` / `g.ArcFlagsRegionCount` | `g.ArcFlagsEnabled()` / `g.ArcFlagsRegionCount()` |
| `g.EnableContractionHierarchy()` / `g.DisableContractionHierarchy()` | `g.EnableContractionHierarchy()` / `g.DisableContractionHierarchy()` |
| `g.PrecomputeContractionHierarchy()` | `g.PrecomputeContractionHierarchy()` |
| `g.ContractionHierarchyEnabled` / `g.ContractionHierarchyShortcuts` | `g.ContractionHierarchyEnabled()` / `g.ContractionHierarchyShortcuts()` |
| `g.Serialize()` | `g.Serialize()` |
| `g.Serialize(path)` | `g.SerializePath(path)` |
| `g.Serialize(listOfPaths)` | `g.SerializePaths([]eppstein.Path{p1, p2})` |
| `g.Serialize(writer, paths)` | `g.SerializeTo(w, paths)` → `error` |
| `GraphPath` (read-only list of `Edge`) | `Path` (value; the zero value is the empty path) |
| `p.IsValid`, `p.Weight`, `p.VertexNames`, `p.Vertices` | `p.IsValid()`, `p.Weight()`, `p.VertexNames()`, `p.Vertices()` |
| `p.Count`, `p[i]`, `foreach (var e in p)` | `p.Len()`, `p.Edge(i)`, `for e := range p.Edges()` |
| `edge.Id`, `edge.Tail`, `edge.Head`, `edge.Weight`, `edge.Group`, `edge.Delta` | `edge.ID()`, `edge.Tail()`, `edge.Head()`, `edge.Weight()`, `edge.Group()`, `edge.Delta()` |
| `vertex.Index`, `vertex.Label`, `vertex.Distance`, `vertex.Region` | `vertex.Index()`, `vertex.Label()`, `vertex.Distance()`, `vertex.Region()` |
| `vertex.Next`, `vertex.TreeEdge` (nullable) | `vertex.Next()`, `vertex.TreeEdge()` → `(…, bool)` |
| `vertex.OutEdges`, `vertex.InEdges` | `vertex.OutEdges()`, `vertex.InEdges()` (iterators) |
| `vertex.Tag = x` | `vertex.SetTag(x)` / `vertex.Tag()` |
| `vertex1 == vertex2`, `edge1 == edge2` | `vertex1 == vertex2`, `edge1 == edge2` |
| `long` weights and distances | `int64` weights and distances |
| `Vertex.Unreached` (`long.MinValue`) | `eppstein.Unreached` (`math.MinInt64`) |

## Things to know

The same rules as in the C# version apply (see
[section 7 of the main README](../README.md#7-things-to-know-common-pitfalls)):

- **Routes without loops:** by default paths may repeat vertices; in networks with two-way
  connections most of them are loops. `g.SetSimplePathsOnly(true)` returns only simple paths
  (Yen with Lawler's improvement), see
  [section 8 of the main README](../README.md#8-routes-without-loops-simplepathsonly).
- **Graphs with cycles have infinitely many paths.** Stop the loop yourself
  (`for p := range g.Paths(s, t) { …; if done { break } }`), or set a corridor with
  `SetMaxDetour` or `SetMaxDetourRatio`: then the number of paths is finite.
- **An empty path means "no path":** unknown vertex name, goal not reachable, or start equals goal.
- **Vertex names:** letters and digits, starting with a letter; not case-sensitive, stored in
  upper case.
- **Negative weights switch edges off**, e.g. `g.EdgeGroupWeights("train", -1)`.
- **After changing the graph, call `FindShortestPath` again.** A running `range g.Paths(…)`
  loop panics if the graph changes or another query starts. The graph keeps one query at a time.
- **Changes go through the graph:** `SetEdgeWeight`, `EdgeGroupWeights`. Vertices and edges are
  read-only values.
- **Equal weights:** paths with the same weight come in no guaranteed order. Currently C# and Go
  return the same order; the shared golden tests (`testdata/golden`) compare them as sets.

Go-specific:

- A `Graph` is **not safe for concurrent use**. Use one graph per goroutine or protect it with a
  `sync.Mutex`.
- Weights are `int64`. Untyped constants work directly (`g.CreateEdges("A", "B", 5, "x")`);
  an `int` variable needs a conversion: `g.CreateEdges("A", "B", int64(w), "x")`.
- The zero value `eppstein.Graph{}` is usable, but `eppstein.NewGraph()` is the recommended way.

## Performance

Same grid graphs as in [section 11 of the main README](../README.md#11-performance)
(4-core cloud VM):

| Vertices | Edges | First path + next 10,000 paths | Memory allocated |
|---|---|---|---|
| 10,000 | 20,700 | 24 ms | 12 MB |
| 40,000 | 83,400 | 52 ms | 20 MB |

Nearby vertices in the middle of a 1,000,000-vertex grid, first path + next 10,000 paths:
102 ms without a corridor, **13 ms** with `g.SetMaxDetourRatio(0.1)`.

Contraction hierarchy (`g.EnableContractionHierarchy()`), 40,000-vertex grid from corner to corner:
the first path (all distances to the goal) takes 1.8 ms instead of 7.9 ms; the preparation takes
1.2 s (`go test -run none -bench ShortestPathTree ./eppstein`).

Building the 1,000,000-vertex grid (2,097,000 edges): 1.1 s with `CreateVertices` +
`CreateEdges`, **0.45 s** with `AddVertex` + `AddEdgeGroup`, 0.73 s with `ImportDimacs`.

```sh
go test -run none -bench . -benchmem ./eppstein
go run ./cmd/benchmark 200 1000 --k 10000 --build index   # same program as the C# and Rust benchmarks
```

## Project layout

| File | Content |
|---|---|
| `eppstein/graph.go` | Storage, building the graph, vertices and edges |
| `eppstein/paths.go` | Queries: Dijkstra, corridor, path enumeration, iterators |
| `eppstein/elements.go` | `Vertex`, `Edge` and `Path` value types |
| `eppstein/arcflags.go` | Optional arc-flags speed-up |
| `eppstein/ch.go` | Optional contraction hierarchy with PHAST |
| `eppstein/import.go` | CSV and DIMACS import |
| `eppstein/serialize.go` | Graphviz (DOT) output |
| `eppstein/pqueue.go` | Priority queue (binary heap) used by the algorithm |
| `eppstein/sidetrackheap.go` | Persistent heap of detours (Eppstein's path enumeration) |
| `eppstein/simplepaths.go` | Simple paths: Yen's algorithm with Lawler's improvement and A* |
| `eppstein/*_test.go` | Tests, benchmarks and runnable examples; `golden_test.go` checks the shared data in `../testdata/golden` |
| `cmd/eppstein/main.go` | Demo program (`go run ./cmd/eppstein [k] [--dot file]`) |
| `cmd/benchmark/main.go` | Benchmark, identical to the C# and Rust programs (see `../paper`) |

## Moving this module into its own repository

The module lives in the `go/` folder, so its import path is
`github.com/nickdinges/k-shortest-path/go`. To publish it as a separate repository (for example
`github.com/nickdinges/k-shortest-path-go`):

1. Copy the content of `go/` into the root of the new repository.
2. Replace the module path everywhere:

   ```sh
   grep -rl 'github.com/nickdinges/k-shortest-path/go' . \
     | xargs sed -i 's#github.com/nickdinges/k-shortest-path/go#github.com/nickdinges/k-shortest-path-go#g'
   ```

3. Copy `../testdata/golden` into the new repository as `testdata/golden` and change
   `goldenDir` in `eppstein/golden_test.go` to `"../testdata/golden"`.
4. Fix the links to `../README.md` in this file, then run `go test ./...`.
