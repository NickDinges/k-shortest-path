# User guide (C#)

Concepts, the complete API, drawing graphs, and common pitfalls. The examples are C#; the
[Go](../go/README.md) and [Rust](../rust/README.md) ports have the same functions under the names
usual in those languages. Start with the [quick start in the main README](../README.md#quick-start).

## Key ideas in plain words

| Term | Meaning | In this library |
|---|---|---|
| **Graph** | A network of points and connections. | `Graph` |
| **Vertex** (node) | A point in the network: a city, a router, a state. | `Vertex`, created by name with `CreateVertices` or `AddVertex` |
| **Edge** (arc) | A **one-way** connection from one vertex to another. For a two-way road, create two edges. | `Edge`, created with `CreateEdges` or `AddEdge` |
| **Weight** | The cost of using an edge: distance, time, price… Whole numbers of type `long` (up to about 9.2 × 10¹⁸). | `Edge.Weight` |
| **Group** | A name you give to edges, so you can change all their weights at once. | `EdgeGroupWeights` |
| **Path** (route) | A chain of edges from the start to the goal. | `GraphPath` |
| **Path weight** | The sum of all edge weights in the path. "Shortest" means "smallest total weight". | `GraphPath.Weight` |
| **k shortest paths** | The best path, the 2nd best, …, the k-th best. | `FindShortestPath` + `FindNextShortestPath` |
| **Simple path** | A path that visits no vertex twice (no loops). By default paths *may* repeat vertices. | `SimplePathsOnly` ([Routes without loops](simple-paths.md)) |

## API reference

All methods are on the `Graph` class (namespace `Eppstein`).

### Building the graph

| Method | What it does | Returns |
|---|---|---|
| `CreateVertices("A,B,C")` | Creates the vertices. **Replaces** all existing vertices *and edges*. | `true`, or `false` if a name is invalid |
| `CreateEdges(from, to, weight, group)` | Adds one-way edges. `from` and `to` are comma-separated lists of the same length. | `true`, or `false` if a vertex doesn't exist or the lists differ in length |
| `EdgeGroupWeights(group, weight)` | Sets a new weight for every edge of a group. | nothing |

For **large graphs**, add vertices and edges by number instead of by name. This is several times
faster, because no name lists have to be split and looked up for every edge:

| Method | What it does | Returns |
|---|---|---|
| `AddVertex("A")` | Adds one vertex and keeps everything else. | its **index** (0, 1, 2, …); the existing index if the name is taken; `-1` if the name is invalid |
| `AddEdge(fromIndex, toIndex, weight)` | Adds one one-way edge between vertices given by index. An overload takes a `group` as 4th value. | the **edge number** (0, 1, 2, …), or `-1` if an index is invalid |
| `GetVertexIndex("A")` | Index of a vertex. | the index, or `-1` |
| `VertexCount`, `EdgeCount` | Number of vertices and edges (valid indices are `0` to `VertexCount - 1`). | |
| `SetEdgeWeight(edge, weight)` | Changes the weight of one edge (an `Edge` or its number). | nothing |
| `ImportEdges(reader)` | Reads edges from a CSV text (see below). | number of edges read |
| `ImportDimacs(reader)` | Reads a graph in the DIMACS format (see below). | number of edges read |

```csharp
Graph g = new Graph();
int home = g.AddVertex("HOME");
int office = g.AddVertex("OFFICE");
g.AddEdge(home, office, 10);
GraphPath p = g.FindShortestPath(home, office);   // also works with indices
```

All ways of building can be mixed. Vertices created by `CreateVertices` get indices too, in the
order of the list.

**CSV files** (`ImportEdges`): one edge per line, `from,to,weight` or `from,to,weight,group`.
Missing vertices are created. Empty lines and lines starting with `#` are skipped:

```text
# from,to,weight,group
HOME,PARK,2
PARK,OFFICE,7,train
```

```csharp
using (var file = new System.IO.StreamReader("roads.csv"))
    g.ImportEdges(file);
```

**DIMACS files** (`ImportDimacs`): the format of the
[DIMACS road networks](https://www.diag.uniroma1.it/challenge9/download.shtml) with millions of
vertices. Vertex number `n` gets the name `V` + `n` (`V1`, `V2`, …):

```text
c comment
p sp 3 2        <- 3 vertices, 2 edges
a 1 2 5         <- edge from vertex 1 to vertex 2, weight 5
a 2 3 5
```

Both imports keep what is already in the graph. A broken line throws a `FormatException` whose
message starts with the line number (`Line 7: invalid weight 'x'`); the lines before it stay
imported.

The DIMACS problem line creates all announced vertices at once. To protect against broken files,
at most 100 million vertices are accepted (`Graph.DefaultMaxImportVertices`; the largest DIMACS
road network, USA, has about 24 million). For bigger graphs pass your own limit:
`g.ImportDimacs(file, 300_000_000)`.

### Asking for paths

| Method | What it does | Returns |
|---|---|---|
| `FindShortestPath(start, goal)` | Prepares everything and returns the **shortest** path. Start and goal are names, or indices (see above). | `GraphPath` (empty if none exists) |
| `FindNextShortestPath()` | Returns the **next** shortest path. Call it again and again. | `GraphPath` (empty when there are no more) |
| `EnumeratePaths(start, goal)` | All paths in order, for `foreach` and LINQ (`.Take(10)`). | `IEnumerable<GraphPath>` |
| `EnumeratePathWeights(start, goal)` | Only the weights of the paths, in order. Faster, because the paths are not built. | `IEnumerable<long>` |

### Working with a `GraphPath`

A `GraphPath` is a read-only list of `Edge` values, so you can loop over it with `foreach`. It
never changes after it was returned.

| Member | Meaning |
|---|---|
| `IsValid` | `true` if the path contains at least one edge. Use it to check for "no path". |
| `Weight` | Total weight (sum of all edges, at the time the path was found). |
| `VertexNames` | The route as text, e.g. `"HOME,PARK,OFFICE"`. |
| `Vertices` | The vertices of the route, in order. |
| `Count`, `path[i]` | Number of edges, and the edge at a position. |

### Vertices and edges

`Vertex` and `Edge` are small read-only values that refer to the graph. Two values are equal
(`==`) if they refer to the same vertex or edge.

| Member | Meaning |
|---|---|
| `g.FindVertex("A")` | The vertex with a name, or `null`. |
| `g.GetVertex(index)`, `g.GetEdge(number)` | The vertex or edge with an index or number. |
| `g.Vertices`, `g.Edges` | All vertices or edges. |
| `vertex.Label`, `vertex.Index` | Name and index. |
| `vertex.OutEdges`, `vertex.InEdges` | The edges leaving or arriving at the vertex. |
| `vertex.Tag` | Any object of your own, attached to the vertex (e.g. coordinates). |
| `vertex.Distance`, `vertex.Next` | After a query: distance to the goal, and the next vertex on the shortest way there. |
| `edge.Tail`, `edge.Head`, `edge.Weight`, `edge.Group` | Start vertex, end vertex, weight and group. |

Changes always go through the graph (`SetEdgeWeight`, `EdgeGroupWeights`), never through a vertex
or edge. That way the graph always knows when its prepared data is outdated.

### Extras

| Method | What it does | Section |
|---|---|---|
| `Serialize(...)` | Produces a Graphviz drawing of the graph. | [6](#drawing-your-graph-graphviz) |
| `SimplePathsOnly` | Only routes that visit no vertex twice (no loops); Yen's algorithm. | [8](simple-paths.md) |
| `MaxDetour`, `MaxDetourRatio` | Only paths at most this much longer than the shortest one (much faster on large graphs). | [9](faster-queries.md) |
| `EnableArcFlags(...)`, `EnableArcFlagsFromPartition(reader)`, `WriteMetisGraph(writer)`, `DisableArcFlags()`, `PrecomputeArcFlags()`, `ArcFlagsEnabled`, `ArcFlagsRegionCount` | Optional speed-up for many queries, also with regions from an external partitioner. | [9](faster-queries.md) |
| `EnableContractionHierarchy()`, `DisableContractionHierarchy()`, `PrecomputeContractionHierarchy()`, `ContractionHierarchyEnabled`, `ContractionHierarchyShortcuts` | Optional speed-up for many queries without a corridor on large graphs (PHAST). | [9](faster-queries.md) |

## Drawing your graph (Graphviz)

`Serialize` turns your graph into text in the
[DOT language](https://graphviz.org/doc/info/lang.html). The free tool
[Graphviz](https://graphviz.org/download/) turns that text into a picture. All pictures in this
README were made this way.

```csharp
string dot = g.Serialize();                    // just the graph
dot = g.Serialize(path);                       // with one path highlighted in red
dot = g.Serialize(new List<GraphPath> { p1, p2 });  // several paths, one color each
System.IO.File.WriteAllText("graph.dot", dot); // save it to a file
```

Then create an image from the command line:

```sh
dot -Tsvg graph.dot -o graph.svg    # or -Tpng graph.png
```

No Graphviz installed? Paste the text into an online viewer such as
[GraphvizOnline](https://dreampuf.github.io/GraphvizOnline/).

The demo program can do this for you. This draws the example graph with its 3 shortest paths:

```sh
dotnet run --project csharp/Eppstein.Demo -- 5 --dot graph.dot
```

**How to read the picture:**

- The numbers on the arrows are the edge weights.
- Highlighted paths are colored (red, blue, green, …). If several paths share an edge, it is drawn
  in all their colors.
- After `FindShortestPath`:
  - the start and goal are drawn as **double circles**,
  - the number under each name is the **distance to the goal**,
  - **bold** arrows are the shortest-path tree (from every vertex, the best next step towards the
    goal).
- **Dashed** arrows have a negative weight, so they are switched off (see [Things to know](#things-to-know-common-pitfalls)).
- If arc flags are on, each region is drawn as a dashed box (see [Faster queries](faster-queries.md)).
- With a corridor ([Faster queries](faster-queries.md)), vertices outside of it show no distance: they were not needed.

## Things to know (common pitfalls)

**1. Graphs with loops produce endless paths.** Paths may visit the same vertex more than once.
For example, the demo graph has a loop `C→C`, so `S,A,B,C,C,G,T` and `S,A,B,C,C,C,G,T` are also
valid paths. If your graph has any cycle, there are infinitely many paths. **Always limit the
number of paths you ask for:**

```csharp
for (int i = 0; i < 10 && p.IsValid; i++) { ... }   // good: at most 10 paths
while (p.IsValid) { ... }                            // may never end on a graph with cycles
```

Alternatively, set a corridor (`MaxDetour` or `MaxDetourRatio`, [Faster queries](faster-queries.md)): then the number of
paths is finite and the `while` loop ends by itself. If you want routes **without** such loops
at all, use `SimplePathsOnly` ([Routes without loops](simple-paths.md)).

**2. An empty path means "no path".** `FindShortestPath` returns an empty path
(`IsValid == false`) when:

- a vertex name doesn't exist, or
- the goal can't be reached from the start, or
- start and goal are the same vertex.

**3. Vertex names:** letters and digits only, starting with a letter (`A1` is fine; `1A`, `A-B`,
`A B` are not). Names are **not case-sensitive** and are stored in upper case (`home` becomes
`HOME`).

**4. Negative weights switch edges off.** Edges with a weight below 0 are ignored completely.
This is handy for "closing" a group of roads temporarily:

```csharp
g.EdgeGroupWeights("train", -1);   // ignore all train connections
g.EdgeGroupWeights("train", 1);    // turn them back on
```

**5. Changing the graph means asking again.** After `CreateEdges`, `SetEdgeWeight` or
`EdgeGroupWeights`, call `FindShortestPath` again. Until then, `FindNextShortestPath` returns empty
paths. A running `EnumeratePaths` loop stops with an `InvalidOperationException` if the graph
changes or another query starts, because its paths would no longer be correct. The graph keeps
**one query at a time**.

**6. Equal weights come in no guaranteed order.** If two paths have the same total weight, either
one may come first. The order is the same every time you run the same program, but it may differ
between versions of the library (and between the C# and the Go version).

**7. Paths are `GraphPath`, not `Path`.** In older versions the class was called `Path`. That
clashed with .NET's own `System.IO.Path` (for file paths), which new projects import
automatically, and caused the error *"'Path' is an ambiguous reference"*. If you upgrade, see
[Upgrading from version 1](upgrading-from-v1.md).

**8. Weights are `long`, not `int`.** Weights and distances are 64-bit numbers, so very large
weights (for example distances in millimeters) work. Passing an `int` is fine
(`CreateEdges("A", "B", 5, "x")`), but reading a weight into an `int` variable does not compile:

```csharp
long w = path.Weight;   // or: var w = path.Weight;
int  x = path.Weight;   // error CS0266: cannot convert 'long' to 'int'
```

The total weight of a path must still fit into a `long`; there is no overflow check.

**9. How errors are reported.** Methods that create or look up something from your data report a
failure in their result: `false`, `-1` or `null` (for example `AddVertex("1A")` returns `-1`,
because names must start with a letter). Mistakes in the program itself throw an exception:
`null` arguments, an index out of range, a negative limit. Broken import files throw a
`FormatException` with the line number.

**10. One graph, one thread.** A graph is not thread-safe. Use a separate graph per thread.

---

[Back to the main README](../README.md)
