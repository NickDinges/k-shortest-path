# k-shortest-path (Go)

A Go port of the C# library in this repository. It finds the **shortest route** between two
points in a network, then the **second shortest**, the **third shortest**, and so on, using
[Eppstein's algorithm](http://www.ics.uci.edu/~eppstein/pubs/Epp-SJC-98.pdf).

The API, the behavior and the Graphviz output are the same as in the C# version. The
[main README](../README.md) explains the concepts (graph, edge, weight, path, arc flags) step by
step; everything there applies here too. This page shows the Go-specific parts.

No dependencies outside the Go standard library. Requires **Go 1.21 or newer**.

## Quick start

```sh
cd go
go run ./cmd/eppstein            # 20 shortest paths of the example graph from the paper
go run ./cmd/eppstein 5          # only 5 paths
go run ./cmd/eppstein 5 --dot graph.dot && dot -Tsvg graph.dot -o graph.svg
go test ./...                    # run the tests
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
| `g.AddVertex("A")` → `int` | `g.AddVertex("A")` → `int` |
| `g.AddEdge(tail, head, weight)` | `g.AddEdge(tail, head, weight)` |
| `g.AddEdge(tail, head, weight, group)` | `g.AddEdgeGroup(tail, head, weight, group)` |
| `g.GetVertexIndex("A")`, `g.VertexCount` | `g.VertexIndex("A")`, `g.VertexCount()` |
| `g.FindShortestPath(sIndex, tIndex)` | `g.FindShortestPathByIndex(sIndex, tIndex)` |
| `g.ImportEdges(reader)` → `int`, throws `FormatException` | `g.ImportEdges(r)` → `(int, error)` |
| `g.ImportDimacs(reader)` → `int`, throws `FormatException` | `g.ImportDimacs(r)` → `(int, error)` |
| `g.FindShortestPath(s, t)` → `Path` | `g.FindShortestPath(s, t)` → `Path` |
| `g.FindNextShortestPath()` → `Path` | `g.FindNextShortestPath()` → `Path` |
| `g.EnableArcFlags(3)` | `g.EnableArcFlags(3)` |
| `g.EnableArcFlags("S,A;B,C")` | `g.EnableArcFlagsRegions("S,A;B,C")` |
| `g.DisableArcFlags()` / `g.PrecomputeArcFlags()` | `g.DisableArcFlags()` / `g.PrecomputeArcFlags()` |
| `g.ArcFlagsEnabled` / `g.ArcFlagsRegionCount` (properties) | `g.ArcFlagsEnabled()` / `g.ArcFlagsRegionCount()` |
| `g.MaxDetour = 15` / `g.MaxDetour` | `g.SetMaxDetour(15)` → `bool` / `g.MaxDetour()` |
| `g.MaxDetourRatio = 0.2` / `g.MaxDetourRatio` | `g.SetMaxDetourRatio(0.2)` → `bool` / `g.MaxDetourRatio()` |
| `long.MaxValue` / `double.PositiveInfinity` (no limit) | `math.MaxInt64` / `math.Inf(1)` |
| invalid limit: `ArgumentOutOfRangeException` | invalid limit: returns `false` |
| `g.Serialize()` | `g.Serialize()` |
| `g.Serialize(path)` | `g.SerializePath(path)` |
| `g.Serialize(listOfPaths)` | `g.SerializePaths([]eppstein.Path{p1, p2})` |
| `g.Serialize(writer, paths)` (throws on null) | `g.SerializeTo(w, paths)` → `error` |
| `GraphPath : List<Edge>` | `type Path []*Edge` (in Go always written `eppstein.Path`, so no name clash) |
| `p.IsValid`, `p.Weight`, `p.VertexNames`, `p.DeltaWeight` | `p.IsValid()`, `p.Weight()`, `p.VertexNames()`, `p.DeltaWeight()` |
| `p.Count` | `len(p)` |
| `edge.Tail`, `edge.Head`, `edge.Weight`, `edge.Group` | `edge.Tail`, `edge.Head`, `edge.Weight`, `edge.Group` |
| `edge.Delta`, `edge.HasArcFlag(r)`, `edge.IsSidetrackOf(v)` | `edge.Delta()`, `edge.HasArcFlag(r)`, `edge.IsSidetrackOf(v)` |
| `vertex.Next`, `vertex.Distance`, `vertex.EdgeToPath`, `vertex.Region`, `vertex.Object` | `vertex.Next()`, `vertex.Distance`, `vertex.EdgeToPath`, `vertex.Region`, `vertex.Object` |
| `long` weights and distances | `int64` weights and distances |
| `Vertex.Unreached` (`long.MinValue`, "distance unknown") | `eppstein.Unreached` (`math.MinInt64`) |

## Things to know

The same rules as in the C# version apply (see
[section 7 of the main README](../README.md#7-things-to-know-common-pitfalls)):

- **Graphs with cycles have infinitely many paths.** Always limit the loop
  (`for i := 0; i < k && p.IsValid(); i++`), or set a corridor with `SetMaxDetour` or
  `SetMaxDetourRatio`: then the number of paths is finite.
- **An empty path means "no path":** unknown vertex name, goal not reachable, or start equals goal.
- **Vertex names:** letters and digits, starting with a letter; not case-sensitive, stored in
  upper case.
- **Negative weights switch edges off**, e.g. `g.EdgeGroupWeights("train", -1)`.
- **After changing the graph, call `FindShortestPath` again.**
- **Equal weights:** paths with the same weight come in insertion order. That order can differ
  from the C# version, but the set of paths and their weights are the same.

Go-specific:

- A `Graph` is **not safe for concurrent use**. Use one graph per goroutine or protect it with a
  `sync.Mutex`.
- Weights are `int64`. Untyped constants work directly (`g.CreateEdges("A", "B", 5, "x")`);
  an `int` variable needs a conversion: `g.CreateEdges("A", "B", int64(w), "x")`.
- The zero value `eppstein.Graph{}` is usable, but `eppstein.NewGraph()` is the recommended way.

## Performance

Same grid graphs as in [section 10 of the main README](../README.md#10-performance)
(4-core cloud VM):

| Vertices | Edges | Build graph | First path + next 10,000 paths |
|---|---|---|---|
| 10,000 | 20,700 | 20 ms | 62 ms |
| 40,000 | 83,400 | 72 ms | 119 ms |

Nearby vertices in the middle of a 1,000,000-vertex grid, first path + next 10,000 paths:
221 ms without a corridor, **33 ms** with `g.SetMaxDetourRatio(0.1)`.

Building the 1,000,000-vertex grid (2,097,000 edges): 1.7 s with `CreateVertices` +
`CreateEdges`, **0.73 s** with `AddVertex` + `AddEdgeGroup`, 1.1 s with `ImportDimacs`.

```sh
go test -run none -bench . -benchmem ./eppstein
```

## Project layout

| File | Content |
|---|---|
| `eppstein/graph.go` | Main algorithm: building the graph, Dijkstra, path enumeration |
| `eppstein/arcflags.go` | Optional arc-flags speed-up |
| `eppstein/serialize.go` | Graphviz (DOT) output |
| `eppstein/elements.go` | `Vertex`, `Edge` and `Path` types |
| `eppstein/building.go` | Building by index (`AddVertex`, `AddEdge`) and CSV/DIMACS import |
| `eppstein/pqueue.go` | Priority queue (binary heap) used by the algorithm |
| `eppstein/sidetrackheap.go` | Persistent heap of detours (Eppstein's path enumeration) |
| `eppstein/*_test.go` | Tests, benchmarks and runnable examples |
| `cmd/eppstein/main.go` | Demo program (`go run ./cmd/eppstein [k] [--dot file]`) |

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

3. Fix the links to `../README.md` in this file, then run `go test ./...`.
