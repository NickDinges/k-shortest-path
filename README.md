# k-shortest-path

Find the **shortest route** between two points in a network, then the **second shortest**, the
**third shortest**, and so on. This is a C# implementation of
[Eppstein's algorithm](http://www.ics.uci.edu/~eppstein/pubs/Epp-SJC-98.pdf) for the
*k shortest paths* problem.

![Example graph with the three shortest paths from S to T highlighted](docs/eppstein-example.svg)

*The example graph from Eppstein's paper. The three shortest routes from S to T are drawn in red,
blue and green. The number under each letter is the distance from that point to T.*

> **Go and Rust versions:** ports with the same API and results live in [`go/`](go/README.md) and
> [`rust/`](rust/README.md). A paper comparing the three and describing the algorithms and the
> AI-assisted development is in [`paper/`](paper/README.md) ([PDF](paper/main.pdf)).

**Contents**

1. [What problem does this solve?](#1-what-problem-does-this-solve)
2. [Quick start](#2-quick-start)
3. [Your first program](#3-your-first-program)
4. [Key ideas in plain words](#4-key-ideas-in-plain-words)
5. [API reference](#5-api-reference)
6. [Drawing your graph (Graphviz)](#6-drawing-your-graph-graphviz)
7. [Things to know (common pitfalls)](#7-things-to-know-common-pitfalls)
8. [Routes without loops: `SimplePathsOnly`](#8-routes-without-loops-simplepathsonly)
9. [Faster queries: corridor, arc flags and contraction hierarchy](#9-faster-queries-corridor-arc-flags-and-contraction-hierarchy)
10. [How the algorithm works](#10-how-the-algorithm-works)
11. [Performance](#11-performance)
12. [Project layout and tests](#12-project-layout-and-tests)
13. [Upgrading from version 1](#13-upgrading-from-version-1)
14. [Further reading](#14-further-reading)
15. [The paper](#15-the-paper)

---

## 1. What problem does this solve?

A route planner usually shows you *one* best route. Often you also want **alternatives**: the
second-best route in case of a traffic jam, the third-best, and so on. Asking for the *k* best
routes is called the **k shortest paths problem**.

Typical uses:

- **Navigation:** show alternative routes.
- **Network routing:** have backup routes ready when a link fails.
- **Planning and scheduling:** list the cheapest few options, not only the cheapest one.
- **Bioinformatics and language processing:** rank the most likely candidates.

Eppstein's algorithm does this very efficiently. It does one main computation up front, and after
that each additional route comes out almost immediately.

## 2. Quick start

**You need:** the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). It runs on
Linux, macOS and Windows. Check that it is installed:

```sh
dotnet --version     # should print 10.x.x or newer
```

The library itself runs on .NET 8 and .NET 10. The tests run on both, so `dotnet test` also needs
the [.NET 8 runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

**Get the code and run the demo:**

```sh
git clone https://github.com/nickdinges/k-shortest-path.git
cd k-shortest-path
dotnet run --project Eppstein.Demo          # prints the 20 shortest paths of the example graph
dotnet run --project Eppstein.Demo -- 5     # prints only the 5 shortest paths
```

The output looks like this (each line is a route, with its total length in brackets):

```text
EPPSTEIN ALGORITHM TEST
Calculation time: 7 ms
S,D,E,F,J,T (55)
S,A,B,C,G,T (58)
S,A,B,F,J,T (59)
...
```

**Run the tests** (optional, checks that everything works):

```sh
dotnet test Eppstein.sln
```

## 3. Your first program

Imagine five places connected by one-way roads. Each road is labeled with the number of minutes it
takes. We want the fastest ways from `HOME` to `OFFICE`.

![Small road network, the fastest route is red](docs/quickstart-routes.svg)

**Step 1:** create your own console app and connect it to this library (run these commands next to
the `k-shortest-path` folder):

```sh
dotnet new console -o MyRoutes
dotnet add MyRoutes reference k-shortest-path/Eppstein/Eppstein.csproj
```

**Step 2:** replace the content of `MyRoutes/Program.cs` with:

```csharp
using System;
using Eppstein;

class Example
{
    static void Main()
    {
        Graph g = new Graph();

        // 1. Create the places (vertices)
        g.CreateVertices("HOME,PARK,BAKERY,STATION,OFFICE");

        // 2. Create one-way roads (edges): from, to, minutes, group name
        g.CreateEdges("HOME",   "PARK",    2, "walk");
        g.CreateEdges("HOME",   "BAKERY",  4, "walk");
        g.CreateEdges("PARK",   "BAKERY",  1, "walk");
        g.CreateEdges("PARK",   "STATION", 7, "walk");
        g.CreateEdges("BAKERY", "STATION", 2, "walk");
        g.CreateEdges("BAKERY", "OFFICE",  6, "walk");
        g.CreateEdges("STATION","OFFICE",  1, "train");

        // 3. Ask for the shortest route, then for the next ones
        GraphPath p = g.FindShortestPath("HOME", "OFFICE");
        for (int i = 1; i <= 5 && p.IsValid; i++)
        {
            Console.WriteLine(i + ". " + p.VertexNames + "  (" + p.Weight + " minutes)");
            p = g.FindNextShortestPath();
        }
    }
}
```

**Step 3:** run it:

```sh
dotnet run --project MyRoutes
```

```text
1. HOME,PARK,BAKERY,STATION,OFFICE  (6 minutes)
2. HOME,BAKERY,STATION,OFFICE  (7 minutes)
3. HOME,PARK,BAKERY,OFFICE  (9 minutes)
4. HOME,BAKERY,OFFICE  (10 minutes)
5. HOME,PARK,STATION,OFFICE  (10 minutes)
```

That's it. The pattern is always the same:

1. `CreateVertices` once,
2. `CreateEdges` for every connection,
3. `FindShortestPath` for the best route,
4. `FindNextShortestPath` repeatedly for the next ones.

> **Shorter with `foreach`:** `EnumeratePaths` gives you the paths one after the other, so steps 3
> and 4 become one loop. `Take(5)` (from `System.Linq`) stops after 5 paths:
>
> ```csharp
> foreach (GraphPath path in g.EnumeratePaths("HOME", "OFFICE").Take(5))
>     Console.WriteLine(path);   // e.g. "HOME,PARK,BAKERY,STATION,OFFICE (6)"
> ```

> **Tip:** `CreateEdges` can create several edges in one call. The lists are paired by position, so
> `g.CreateEdges("A,B", "C,D", 5, "x")` creates `A→C` and `B→D`, both with weight 5.

## 4. Key ideas in plain words

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
| **Simple path** | A path that visits no vertex twice (no loops). By default paths *may* repeat vertices. | `SimplePathsOnly` (section 8) |

## 5. API reference

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
| `Serialize(...)` | Produces a Graphviz drawing of the graph. | [6](#6-drawing-your-graph-graphviz) |
| `SimplePathsOnly` | Only routes that visit no vertex twice (no loops); Yen's algorithm. | [8](#8-routes-without-loops-simplepathsonly) |
| `MaxDetour`, `MaxDetourRatio` | Only paths at most this much longer than the shortest one (much faster on large graphs). | [9](#9-faster-queries-corridor-arc-flags-and-contraction-hierarchy) |
| `EnableArcFlags(...)`, `EnableArcFlagsFromPartition(reader)`, `WriteMetisGraph(writer)`, `DisableArcFlags()`, `PrecomputeArcFlags()`, `ArcFlagsEnabled`, `ArcFlagsRegionCount` | Optional speed-up for many queries, also with regions from an external partitioner. | [9](#9-faster-queries-corridor-arc-flags-and-contraction-hierarchy) |
| `EnableContractionHierarchy()`, `DisableContractionHierarchy()`, `PrecomputeContractionHierarchy()`, `ContractionHierarchyEnabled`, `ContractionHierarchyShortcuts` | Optional speed-up for many queries without a corridor on large graphs (PHAST). | [9](#9-faster-queries-corridor-arc-flags-and-contraction-hierarchy) |

## 6. Drawing your graph (Graphviz)

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
dotnet run --project Eppstein.Demo -- 5 --dot graph.dot
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
- **Dashed** arrows have a negative weight, so they are switched off (see section 7).
- If arc flags are on, each region is drawn as a dashed box (see section 9).
- With a corridor (section 9), vertices outside of it show no distance: they were not needed.

## 7. Things to know (common pitfalls)

**1. Graphs with loops produce endless paths.** Paths may visit the same vertex more than once.
For example, the demo graph has a loop `C→C`, so `S,A,B,C,C,G,T` and `S,A,B,C,C,C,G,T` are also
valid paths. If your graph has any cycle, there are infinitely many paths. **Always limit the
number of paths you ask for:**

```csharp
for (int i = 0; i < 10 && p.IsValid; i++) { ... }   // good: at most 10 paths
while (p.IsValid) { ... }                            // may never end on a graph with cycles
```

Alternatively, set a corridor (`MaxDetour` or `MaxDetourRatio`, section 9): then the number of
paths is finite and the `while` loop ends by itself. If you want routes **without** such loops
at all, use `SimplePathsOnly` (section 8).

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
[Upgrading from version 1](#13-upgrading-from-version-1).

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

## 8. Routes without loops: `SimplePathsOnly`

### The problem: paths with loops

By default the library returns the k shortest **walks**: a path may visit the same vertex more than
once. That is what Eppstein's algorithm computes, and it is very fast. But in networks where
connections go **both ways** (roads, computer networks, pipes, game maps) most of these walks are
useless as alternatives. Here is the road network from section 3, with every road in both
directions:

| # | Default (walks, Eppstein) | Minutes | | `SimplePathsOnly = true` (Yen) | Minutes |
|---|---|---|---|---|---|
| 1 | HOME → PARK → BAKERY → STATION → OFFICE | 6 | | HOME → PARK → BAKERY → STATION → OFFICE | 6 |
| 2 | HOME → BAKERY → STATION → OFFICE | 7 | | HOME → BAKERY → STATION → OFFICE | 7 |
| 3 | HOME → PARK → BAKERY → STATION → OFFICE → **STATION → OFFICE** | 8 | | HOME → PARK → BAKERY → OFFICE | 9 |
| 4 | HOME → PARK → BAKERY → **PARK → BAKERY** → STATION → OFFICE | 8 | | HOME → BAKERY → OFFICE | 10 |
| 5 | HOME → BAKERY → STATION → OFFICE → **STATION → OFFICE** | 9 | | HOME → PARK → STATION → OFFICE | 10 |
| 6 | HOME → PARK → BAKERY → OFFICE | 9 | | HOME → BAKERY → PARK → STATION → OFFICE | 13 |
| 7 | HOME → BAKERY → **PARK → BAKERY** → STATION → OFFICE | 9 | | HOME → PARK → STATION → BAKERY → OFFICE | 17 |
| 8 | HOME → PARK → **HOME → PARK** → BAKERY → STATION → OFFICE | 10 | | *(no more: these are all 7 routes)* | |

On the left, every path from number 3 on is "the best route plus a detour back and forth". They
are correct shortest *walks*, but nobody would drive them. On the right are the **simple paths**:
paths that never visit a vertex twice. They are what people mean by "alternative routes".

The bigger the network, the worse it gets: in a 30×30 grid of two-way streets, only 74 of the
first 10,000 walks are simple. Filtering the walks afterwards is therefore no solution.

### How to use it

```csharp
g.SimplePathsOnly = true;   // default: false (walks)

foreach (GraphPath route in g.EnumeratePaths("HOME", "OFFICE").Take(3))
    Console.WriteLine(route);   // the 3 best routes without loops
```

Everything else works as before: `FindShortestPath`, `FindNextShortestPath`, `EnumeratePaths`,
the corridor (`MaxDetour`, `MaxDetourRatio`) and the Graphviz output. After changing the setting,
call `FindShortestPath` (or start a new `EnumeratePaths`) again. In Go:
`g.SetSimplePathsOnly(true)`.

### Which mode should I use?

| Situation | Use |
|---|---|
| Connections go both ways (roads, networks, maps) and you want alternative routes | **`SimplePathsOnly = true`** |
| The graph has no cycles (e.g. schedules, dependency graphs, speech or sequence lattices) | Default: both modes give the same paths, the default is much faster |
| Repeating a vertex is meaningful (e.g. a state machine that may stay in a state) | Default |
| You need thousands of paths and loops do not matter | Default |

### What it costs

Simple paths are much harder to find than walks:

- **Walks (default):** one shortest-path search in total; after that every further path costs
  almost nothing.
- **Simple paths:** every further path needs several shortest-path searches (one for each
  possible place where a new route can branch off). They are fast because they are guided by the
  distances to the goal (see section 10), but they add up.

Measured on two-way grid streets with 1 million vertices, between two nearby points
(details in section 11):

| | Without corridor | With `MaxDetourRatio = 0.1` |
|---|---|---|
| Shortest route (repeated query) | 371 ms | 6 ms |
| + 2 alternatives | + 7 ms | + 6 ms |
| + 9 alternatives | + 13 ms | + 18 ms |

**Tips:**

- **Combine it with a corridor** (`MaxDetourRatio`, section 9). It limits the first search to the
  area around the route, and the branching searches stop as soon as no allowed route is possible.
  In practice you rarely want an alternative that is twice as long anyway.
- **Ask only for what you need** (`Take(3)`): the searches for the next route only run when you
  ask for it. The first route costs nothing extra.
- The number of simple paths is finite, so a loop over `EnumeratePaths` ends by itself. But it can
  be astronomically large in big networks, so still use `Take` or a corridor.
- `EnumeratePathWeights` is not faster than `EnumeratePaths` in this mode (the paths are built
  anyway), and arc flags only speed up the first search.

## 9. Faster queries: corridor, arc flags and contraction hierarchy

All three are optional. The results never change, except that the corridor deliberately leaves out
paths that are too long.

### Corridor: only paths that are not much longer

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
  with 1 million vertices, a repeated local query takes 3 ms instead of 78 ms (see section 11).
- The number of paths within the limit is finite, so a loop over `EnumeratePaths` (or
  `while (p.IsValid)` with `FindNextShortestPath`) ends by itself, even with cycles.
- If you set both, the **smaller** limit applies.
- After changing a limit, call `FindShortestPath` again.
- Remove the limits with `g.MaxDetour = long.MaxValue;` and
  `g.MaxDetourRatio = double.PositiveInfinity;` (the defaults).

### Arc flags: preparation for many queries

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

### Contraction hierarchy: all distances much faster

*For many queries **without** a corridor on large, unchanging graphs.*

Every query starts with the distances of all vertices to the goal (step 1 in section 10). On a large
graph without a corridor that is most of the time of a query. A **contraction hierarchy** prepares
the graph once, so that these distances come out 3 to 10 times faster:

```csharp
g.EnableContractionHierarchy();
g.PrecomputeContractionHierarchy();   // optional: prepare now instead of on the first query

foreach (GraphPath p in g.EnumeratePaths("HOME", "OFFICE").Take(10))
    Console.WriteLine(p);
Console.WriteLine(g.ContractionHierarchyShortcuts + " shortcuts");
```

Measured on the benchmark grids (section 11, C#, results identical in all cases):

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
  A corridor is usually the better choice anyway (section 11: 3 ms instead of 78 ms).
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
down ([Geisberger et al. 2008](#14-further-reading)).

A query uses **PHAST** ([Delling et al.](#14-further-reading)): a small search from the goal over
edges to higher-ranked vertices, then one linear sweep over all vertices from the highest to the
lowest rank, without any priority queue. The vertices are stored in sweep order, so the sweep reads
memory almost sequentially. Finally, the shortest-path tree is built from the original edges whose
weight matches the distances (breadth-first from the goal, so edges of weight 0 cannot form a
cycle). Negative (switched-off) edges and self-loops are ignored.

</details>

## 10. How the algorithm works

You don't need this to use the library, but here's the intuition.

1. **Distances to the goal.** One run of
   [Dijkstra's algorithm](https://en.wikipedia.org/wiki/Dijkstra%27s_algorithm), backwards from
   the goal, finds for every vertex its shortest distance to the goal and the best next step. These
   best steps form the **shortest-path tree** (the bold arrows in the drawings).
2. **Detours ("sidetracks").** Any other edge is a detour. Taking edge `u→v` instead of `u`'s best
   step costs extra:

   `extra = weight(u→v) + distance(v) − distance(u)`

   This is never negative.
3. **Every path = a list of detours.** Follow the tree, except where you take a detour. So a path
   is fully described by its detours, and its length is the shortest length plus the extra costs.
4. **Detours sorted per vertex.** For every vertex `v`, a heap (a structure that always knows its
   smallest element) holds all detours you can still take on the way from `v` to the goal, sorted
   by their extra cost. The heap of `v` is the heap of the next vertex plus `v`'s own detours.
   It is *persistent*: building it copies only a few nodes and shares the rest, so it is cheap.
   Heaps are only built for vertices that are actually needed.
5. **Best first.** A priority queue (a "to-do list" sorted by total length) starts with the
   shortest path (no detours). Each time a path is taken out, at most three new candidates are
   added: the *next best alternatives* for its last detour (two heap neighbors), and the *best
   additional detour* after it (top of the heap at its end). Extra costs are never negative, so
   paths come out in the right order. Only what is needed is computed, which is why graphs with
   cycles work. This is the core idea of Eppstein's paper.

### Simple paths (`SimplePathsOnly`): Yen and Lawler

Detours as above cannot forbid loops: a detour may lead back to a vertex the path already visited.
For simple paths the library uses **Yen's algorithm** (1971) with **Lawler's improvement** (1972):

1. **The first route** is the shortest path (it follows the tree, so it has no loops).
2. **Branching off.** Take the route found last. For each of its vertices `v`, keep the route up
   to `v` (the *root*) and search the best way from `v` to the goal (the *spur*) that
   - does not use the root's vertices again (otherwise the route would have a loop), and
   - does not leave `v` along an edge that an already found route with the same root uses
     (otherwise we would find that route again).

   Root + spur is a *candidate* for a later route.
3. **Best first.** All candidates wait in a priority queue sorted by length. The shortest one is the
   next route; then step 2 runs for it.

Example: the first route is `HOME → PARK → BAKERY → STATION → OFFICE`. Branching off at `BAKERY`
(root `HOME → PARK → BAKERY`, edge to `STATION` blocked, `HOME` and `PARK` not allowed) gives the
spur `BAKERY → OFFICE`, so the candidate `HOME → PARK → BAKERY → OFFICE` (9 minutes), the third route.

**Lawler's improvement:** a route that was created by branching off at its *i*-th vertex has the
same root as its parent up to there, and all branchings before that point were already tried for
the parent. So step 2 only has to start at the *i*-th vertex. That saves most of the searches.

**Making the searches fast.** Each spur is a shortest-path search with some vertices and edges
forbidden. Forbidding things only makes ways longer, so the distances to the goal that the first
search computed (step 1 of Eppstein above) are *lower bounds* for every spur. The library uses them
for an [A\* search](https://en.wikipedia.org/wiki/A*_search_algorithm): it always continues with
the vertex whose "distance so far + known distance to the goal" is smallest, and so goes almost
straight towards the goal instead of searching in all directions. With a corridor, a search stops
as soon as even this lower bound exceeds the allowed length. The found routes are kept in a
prefix tree (*trie*), which answers "which edges do routes with this root use?" directly, and a
spur search only runs when the next route is requested.

## 11. Performance

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

**Building the graph** with 1,000,000 vertices and 2,097,000 edges (section 5):

| Way of building | Time |
|---|---|
| `CreateVertices` + `CreateEdges` (by name) | 1.2 s |
| `AddVertex` + `AddEdge` (by index) | **0.29 s** |
| `ImportDimacs` (from text in memory) | 0.7 s |

**Between two nearby vertices** in the middle of the 1,000,000-vertex grid, with and without a
corridor (section 9):

| Corridor | First path | First path, repeated query | Next paths | Paths found |
|---|---|---|---|---|
| none | 273 ms | 78 ms | 32 ms | 10,000 (limit of the benchmark) |
| `MaxDetourRatio = 0.1` | 87 ms | **3 ms** | 35 ms | 10,000 (limit of the benchmark) |
| `MaxDetourRatio = 0.02` | 82 ms | **4 ms** | 4 ms | 1,169 (all within 2%) |

The first query after building or changing the graph includes one-time preparation (sorting the
edges by direction); the "repeated query" column shows a query without it.

**Simple paths** (`SimplePathsOnly`, section 8) on the same grids with **two-way streets**, from
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
- **At most three candidates per path**, thanks to the persistent detour heaps (section 10).
  Candidates store only their parent and one detour, not a copy of all detours.
- **The corridor** stops the shortest-path search where no allowed path can lie.
- **Optional contraction hierarchy:** for many queries without a corridor, the distances of all
  vertices come from one linear sweep (PHAST) instead of Dijkstra, 3 to 10 times faster (section 9).
- **Simple paths:** Lawler's rule saves most branch searches, and the remaining ones run as A*
  towards the goal, guided by the distances that are already known (section 10).
- **Edges are stored by direction**, so the searches never look at edges going the wrong way.

The benchmark now draws the weights from SplitMix64, identically in C#, Go (`go/cmd/benchmark`) and
Rust (`rust/src/bin/benchmark.rs`), so all three build the same graphs; the tables above were
measured earlier with `System.Random` (same weight distribution). The cross-language comparison is
in the [paper](paper/README.md).

Run the benchmark yourself (sizes are grid widths; `--k` = number of paths, `--local` = nearby
vertices, `--ratio` = `MaxDetourRatio`, `--build strings|index|dimacs` = way of building,
`--two-way` = every street in both directions, `--simple` = `SimplePathsOnly`, `--ch` = contraction
hierarchy, its preparation time is written to standard error):

```sh
dotnet run -c Release --project Eppstein.Benchmarks -- 50 100 200 --k 10000
dotnet run -c Release --project Eppstein.Benchmarks -- 1000 --local --ratio 0.1
dotnet run -c Release --project Eppstein.Benchmarks -- 1000 --build index
dotnet run -c Release --project Eppstein.Benchmarks -- 100 200 --two-way --simple --k 100
dotnet run -c Release --project Eppstein.Benchmarks -- 200 1000 --build index --ch
```

## 12. Project layout and tests

| Folder / file | Content |
|---|---|
| `Eppstein/` | The library (NuGet package `Eppstein.KShortestPaths`) |
| `Eppstein/Graph.cs` | Storage, building the graph, vertices and edges |
| `Eppstein/Graph.Paths.cs` | Queries: Dijkstra, corridor, path enumeration |
| `Eppstein/Graph.ArcFlags.cs` | Optional arc-flags speed-up |
| `Eppstein/Graph.ContractionHierarchy.cs` | Optional contraction hierarchy with PHAST |
| `Eppstein/Graph.Import.cs` | CSV and DIMACS import |
| `Eppstein/Graph.Serialize.cs` | Graphviz (DOT) output |
| `Eppstein/Vertex.cs`, `Edge.cs`, `GraphPath.cs` | The public value types |
| `Eppstein/SidetrackHeap.cs` | Persistent heap of detours (Eppstein's path enumeration) |
| `Eppstein/Graph.SimplePaths.cs` | Simple paths: Yen's algorithm with Lawler's improvement and A* |
| `Eppstein.Demo/` | Demo program (`dotnet run --project Eppstein.Demo -- [k] [--dot file]`) |
| `Eppstein.Tests/` | Automated tests (xUnit) |
| `Eppstein.Benchmarks/` | Timing benchmark (see section 11) |
| `testdata/golden/` | Graphs with expected paths, checked by the C# **and** the Go tests |
| `.github/workflows/ci.yml` | CI: build, tests and a benchmark comparison for every pull request |
| `docs/` | Images used in this README, and [an assessment of contraction hierarchies for this library](docs/research-ch-and-k-shortest-paths.md) (with measurements of the implementation) |
| `go/` | Go port of the library ([own README](go/README.md)) |
| `rust/` | Rust port of the library ([own README](rust/README.md)) |
| `paper/` | Paper (LaTeX) with the cross-language measurements and their scripts ([README](paper/README.md)) |

```sh
dotnet build Eppstein.sln   # compile (warnings count as errors)
dotnet test Eppstein.sln    # run all tests
dotnet pack Eppstein/Eppstein.csproj -c Release -o artifacts   # build the NuGet package
```

The tests compare the results with a brute-force search on hundreds of random graphs (with and
without cycles, corridors, arc flags and the contraction hierarchy; all golden cases also run with
the contraction hierarchy), and check the shared golden data, the Graphviz output
and the error handling. On top of that:

- **Property-based tests** (`Eppstein.Tests/PropertyTests.cs`, with
  [FsCheck](https://fscheck.github.io/FsCheck/)) generate thousands of graphs and import files per
  run, and shrink a failing input to a minimal example.
- **Fuzz tests** for the Go version (`go/eppstein/fuzz_test.go`) search for inputs that crash the
  imports or give wrong paths. Inputs they found are kept in `go/eppstein/testdata/fuzz` and run
  with every `go test`.

The CI (`.github/workflows/`) checks every pull request:

| Check | What it does |
|---|---|
| C# build and tests | Build without warnings, tests on .NET 8 and .NET 10, line coverage at least 95% (report in the job summary), NuGet package |
| Go vet and tests | Go 1.23, oldstable and stable: `gofmt`, `go vet`, `staticcheck`, tests with the race detector, coverage at least 93% |
| Go fuzzing | Each fuzz target for 1 minute (weekly for 10 minutes) |
| Benchmark comparison | Benchmarks of the base branch and of the pull request on the same machine |
| CodeQL | Static security analysis of the C# and Go code (also weekly) |

Measure the coverage yourself:

```sh
dotnet test Eppstein.sln --collect:"XPlat Code Coverage"
```

## 13. Upgrading from version 1

Version 2 changed the interface. What to change in your code:

| Version 1 | Version 2 |
|---|---|
| Project `Eppstein2` (a program) | Library `Eppstein` (NuGet `Eppstein.KShortestPaths`); demo in `Eppstein.Demo` |
| `Path` | `GraphPath` (read-only; `Weight` is stored) |
| `edge.Weight = 5;` | `g.SetEdgeWeight(edge, 5);` |
| `vertex.RelatedEdges` | `vertex.OutEdges` and `vertex.InEdges` |
| `vertex.Object` | `vertex.Tag` |
| `vertex.Distance = …`, `vertex.EdgeToPath`, `vertex.Region` set by hand | read-only: `Distance`, `TreeEdge`, `Region` |
| `int w = path.Weight;` | `long w = path.Weight;` |
| `bool ok = g.AddEdge(…)` | `int id = g.AddEdge(…)` (edge number, `-1` on error) |
| `g.AddVertex(null)` returned `-1` | throws `ArgumentNullException` |
| `vertex.Equals("A")` (vertex vs. string) | `vertex.Label == "A"` |
| `SP_Node`, `ST_Node`, `PriorityQueue<T>` | removed (internal details) |
| loop with `FindNextShortestPath` | still works; `EnumeratePaths` is shorter |

## 14. Further reading

- David Eppstein, *Finding the k Shortest Paths*, SIAM Journal on Computing, 1998:
  [paper (PDF)](http://www.ics.uci.edu/~eppstein/pubs/Epp-SJC-98.pdf).
  The algorithm finds the k shortest paths, allowing repeated vertices and edges. After one
  shortest-path computation it needs only constant time per path. It is widely cited in route
  planning, network routing, computational linguistics and bioinformatics.
- Robert Geisberger, Peter Sanders, Dominik Schultes, Daniel Delling, *Contraction Hierarchies:
  Faster and Simpler Hierarchical Routing in Road Networks*, WEA 2008, and Daniel Delling, Andrew
  V. Goldberg, Andreas Nowatzyk, Renato F. Werneck, *PHAST: Hardware-Accelerated Shortest Path
  Trees*, JPDC 2013 ([paper](https://i11www.iti.kit.edu/extra/publications/dgnw-phast-12.pdf)):
  the techniques behind `EnableContractionHierarchy`.
- Jin Y. Yen, *Finding the k shortest loopless paths in a network*, Management Science, 1971, and
  Eugene L. Lawler, *A procedure for computing the k best solutions to discrete optimization
  problems and its application to the shortest path problem*, Management Science, 1972: the
  algorithm behind `SimplePathsOnly`.
- [K shortest path routing](https://en.wikipedia.org/wiki/K_shortest_path_routing) and
  [Yen's algorithm](https://en.wikipedia.org/wiki/Yen%27s_algorithm) on Wikipedia.
- [Graphviz documentation](https://graphviz.org/documentation/).
- The paper on this library ([section 15](#15-the-paper)): algorithms, the C#/Go/Rust comparison and
  how the library was developed with Claude.

## 15. The paper

[`paper/main.pdf`](paper/main.pdf) (11 pages, English, LaTeX source in [`paper/`](paper/README.md)):
*From a 2010 Prototype to Contraction Hierarchies in Three Languages: Engineering k Shortest Paths
with Eppstein, Yen–Lawler, Arc Flags and PHAST*.

The paper is the result of an experiment: the code in all three languages, the tests, the
measurements and the text were generated with Claude (Anthropic) through Claude Code, under the
direction of the repository's author. It covers:

- **The algorithms and how they work together** (sections 9 and 10 of this README in more depth):
  Eppstein with lazy persistent heaps, the corridor, Yen–Lawler with exact A\*, arc flags with
  external partitions, and PHAST on a contraction hierarchy, with related work.
- **C#, Go and Rust compared** on exactly the same graphs, with the same benchmark program in all
  three languages:
  - the three are within a factor of 1.3 for the shortest-path tree on 1,000,000 vertices;
  - Rust is mostly 10–40% faster;
  - garbage collection makes no measurable difference (Go with the collector switched off is not
    faster);
  - the largest gaps come from .NET's tiered JIT compilation on short runs.
- **Algorithms matter more than languages:** the original 2010 version needs 40 s for the first path
  on a 12×12 grid, the current one 0.12 ms; a corridor turns a 69 ms query into 0.5 ms.
- **How the work was done:** what came from the author (the 2010 code, short requests, ideas from
  a domain expert), what Claude did, where it made mistakes, and how the tests caught them.

Every number in the paper comes from scripts and CSV files in `paper/`; see
[`paper/README.md`](paper/README.md) to reproduce the measurements and to build the PDF.
