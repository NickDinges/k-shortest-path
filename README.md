# k-shortest-path

Find the **shortest route** between two points in a network, then the **second shortest**, the
**third shortest**, and so on. This is a C# implementation of
[Eppstein's algorithm](http://www.ics.uci.edu/~eppstein/pubs/Epp-SJC-98.pdf) for the
*k shortest paths* problem.

![Example graph with the three shortest paths from S to T highlighted](docs/eppstein-example.svg)

*The example graph from Eppstein's paper. The three shortest routes from S to T are drawn in red,
blue and green. The number under each letter is the distance from that point to T.*

**Contents**

1. [What problem does this solve?](#1-what-problem-does-this-solve)
2. [Quick start](#2-quick-start)
3. [Your first program](#3-your-first-program)
4. [Key ideas in plain words](#4-key-ideas-in-plain-words)
5. [API reference](#5-api-reference)
6. [Drawing your graph (Graphviz)](#6-drawing-your-graph-graphviz)
7. [Things to know (common pitfalls)](#7-things-to-know-common-pitfalls)
8. [Speeding up repeated queries: arc flags](#8-speeding-up-repeated-queries-arc-flags)
9. [How the algorithm works](#9-how-the-algorithm-works)
10. [Project layout and tests](#10-project-layout-and-tests)
11. [Further reading](#11-further-reading)

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

**You need:** the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). It runs on
Linux, macOS and Windows. Check that it is installed:

```sh
dotnet --version     # should print 8.x.x or newer
```

**Get the code and run the demo:**

```sh
git clone https://github.com/nickdinges/k-shortest-path.git
cd k-shortest-path
dotnet run --project Eppstein2          # prints the 20 shortest paths of the example graph
dotnet run --project Eppstein2 -- 5     # prints only the 5 shortest paths
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
dotnet test Eppstein2.sln
```

## 3. Your first program

Imagine five places connected by one-way roads. Each road is labeled with the number of minutes it
takes. We want the fastest ways from `HOME` to `OFFICE`.

![Small road network, the fastest route is red](docs/quickstart-routes.svg)

**Step 1:** create your own console app and connect it to this library (run these commands next to
the `k-shortest-path` folder):

```sh
dotnet new console -o MyRoutes
dotnet add MyRoutes reference k-shortest-path/Eppstein2/Eppstein2.csproj
```

**Step 2:** replace the content of `MyRoutes/Program.cs` with:

```csharp
using System;
using Eppstein;
using Path = Eppstein.Path;   // see pitfall 7 in section 7

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
        Path p = g.FindShortestPath("HOME", "OFFICE");
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

> **Tip:** `CreateEdges` can create several edges in one call. The lists are paired by position, so
> `g.CreateEdges("A,B", "C,D", 5, "x")` creates `A→C` and `B→D`, both with weight 5.

## 4. Key ideas in plain words

| Term | Meaning | In this library |
|---|---|---|
| **Graph** | A network of points and connections. | `Graph` |
| **Vertex** (node) | A point in the network: a city, a router, a state. | Created by name with `CreateVertices` |
| **Edge** (arc) | A **one-way** connection from one vertex to another. For a two-way road, create two edges. | `Edge`, created with `CreateEdges` |
| **Weight** | The cost of using an edge: distance, time, price… Whole numbers. | `Edge.Weight` |
| **Group** | A name you give to edges, so you can change all their weights at once. | `EdgeGroupWeights` |
| **Path** (route) | A chain of edges from the start to the goal. | `Path` |
| **Path weight** | The sum of all edge weights in the path. "Shortest" means "smallest total weight". | `Path.Weight` |
| **k shortest paths** | The best path, the 2nd best, …, the k-th best. | `FindShortestPath` + `FindNextShortestPath` |

## 5. API reference

All methods are on the `Graph` class (namespace `Eppstein`).

### Building the graph

| Method | What it does | Returns |
|---|---|---|
| `CreateVertices("A,B,C")` | Creates the vertices. **Replaces** all existing vertices *and edges*. | `true`, or `false` if a name is invalid |
| `CreateEdges(from, to, weight, group)` | Adds one-way edges. `from` and `to` are comma-separated lists of the same length. | `true`, or `false` if a vertex doesn't exist or the lists differ in length |
| `EdgeGroupWeights(group, weight)` | Sets a new weight for every edge of a group. | nothing |

### Asking for paths

| Method | What it does | Returns |
|---|---|---|
| `FindShortestPath(start, goal)` | Prepares everything and returns the **shortest** path. | `Path` (empty if none exists) |
| `FindNextShortestPath()` | Returns the **next** shortest path. Call it again and again. | `Path` (empty when there are no more) |

### Working with a `Path`

A `Path` is a list of `Edge` objects (`List<Edge>`), so you can loop over it with `foreach`.

| Member | Meaning |
|---|---|
| `IsValid` | `true` if the path contains at least one edge. Use it to check for "no path". |
| `Weight` | Total weight (sum of all edges). |
| `VertexNames` | The route as text, e.g. `"HOME,PARK,OFFICE"`. |
| `Count` | Number of edges in the path. |
| `edge.Tail`, `edge.Head`, `edge.Weight`, `edge.Group` | Start vertex, end vertex, weight and group of each edge. |

### Extras

| Method | What it does | Section |
|---|---|---|
| `Serialize(...)` | Produces a Graphviz drawing of the graph. | [6](#6-drawing-your-graph-graphviz) |
| `EnableArcFlags(...)`, `DisableArcFlags()`, `PrecomputeArcFlags()`, `ArcFlagsEnabled`, `ArcFlagsRegionCount` | Optional speed-up. | [8](#8-speeding-up-repeated-queries-arc-flags) |

## 6. Drawing your graph (Graphviz)

`Serialize` turns your graph into text in the
[DOT language](https://graphviz.org/doc/info/lang.html). The free tool
[Graphviz](https://graphviz.org/download/) turns that text into a picture. All pictures in this
README were made this way.

```csharp
string dot = g.Serialize();                    // just the graph
dot = g.Serialize(path);                       // with one path highlighted in red
dot = g.Serialize(new List<Path> { p1, p2 });  // several paths, one color each
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
dotnet run --project Eppstein2 -- 5 --dot graph.dot
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
- If arc flags are on, each region is drawn as a dashed box (see section 8).

## 7. Things to know (common pitfalls)

**1. Graphs with loops produce endless paths.** Paths may visit the same vertex more than once.
For example, the demo graph has a loop `C→C`, so `S,A,B,C,C,G,T` and `S,A,B,C,C,C,G,T` are also
valid paths. If your graph has any cycle, there are infinitely many paths. **Always limit the
number of paths you ask for:**

```csharp
for (int i = 0; i < 10 && p.IsValid; i++) { ... }   // good: at most 10 paths
while (p.IsValid) { ... }                            // may never end on a graph with cycles
```

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

**5. Changing the graph means asking again.** After `CreateEdges` or `EdgeGroupWeights`, call
`FindShortestPath` again. Until then, `FindNextShortestPath` returns empty paths.

**6. Equal weights come in no particular order.** If two paths have the same total weight, either
one may come first.

**7. Name clash with `System.IO.Path`.** .NET has its own class called `Path`, for file paths.
New projects (`dotnet new console`) import it automatically, so `Path p = ...` fails with
*"'Path' is an ambiguous reference"*. Add this line at the top of your file:

```csharp
using Path = Eppstein.Path;
```

Or write `Eppstein.Path` instead of `Path`.

## 8. Speeding up repeated queries: arc flags

*Optional. You only need this for large graphs where you run many queries.*

**The idea in one sentence:** split the vertices into regions, and remember for every edge whether
it is ever useful for reaching each region. When you later search for routes into a region, edges
that are never useful for it are skipped.

Think of road signs: a small side street is never part of the fastest route to the airport, so a
search towards the airport can ignore it.

```csharp
g.EnableArcFlags(3);   // let the library split the graph into about 3 regions
// or choose regions yourself (separated by ';'), unlisted vertices form one extra region:
g.EnableArcFlags("S,A,B,C;D,E,F;G,H,I,J,K,T");

Path p = g.FindShortestPath("S", "T");   // same results as before, possibly faster
```

Good to know:

- **Results are exactly the same** as without arc flags. Only the work behind the scenes changes.
- The preparation happens automatically on the next query, or right away with
  `g.PrecomputeArcFlags()`.
- Changing edges or weights is fine; the flags are recomputed automatically.
- `CreateVertices` turns arc flags off. Call `EnableArcFlags` again afterwards.
- The preparation costs time. It pays off only if you run **many** queries on the same graph.
- `g.DisableArcFlags()` switches the feature off again.

<details>
<summary>Technical details</summary>

Every arc `(u,v)` gets one flag per region `r`. The flag is set if the arc lies on a shortest path
from `u` to some vertex of `r`. To compute the flags, one backward Dijkstra runs from each boundary
vertex of each region. A query then only relaxes arcs flagged for the region of the goal `t`.
Every shortest path `u → t` uses only such arcs, so all distances stay exact.

</details>

## 9. How the algorithm works

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
4. **Best first.** A priority queue (a "to-do list" sorted by total length) starts with the
   shortest path (no detours). Each time a path is taken out, the paths with *one more* detour are
   added. Detours never make a path shorter, so paths come out in the right order. Only what is
   needed is computed, which is why graphs with cycles work.

## 10. Project layout and tests

| File | Content |
|---|---|
| `Eppstein2/Graph.cs` | Main algorithm: building the graph, Dijkstra, path enumeration |
| `Eppstein2/Graph.ArcFlags.cs` | Optional arc-flags speed-up |
| `Eppstein2/Graph.Serialize.cs` | Graphviz (DOT) output |
| `Eppstein2/GraphElements.cs` | `Vertex`, `Edge` and `Path` classes |
| `Eppstein2/PriorityQueue.cs` | Sorted queue used by the algorithm |
| `Eppstein2/Program.cs` | Demo program (`dotnet run --project Eppstein2 -- [k] [--dot file]`) |
| `Eppstein2.Tests/` | Automated tests (xUnit) |
| `docs/` | Images used in this README |

```sh
dotnet build Eppstein2.sln   # compile
dotnet test Eppstein2.sln    # run all tests
```

The tests compare the results with a brute-force search on hundreds of random graphs (with and
without cycles and arc flags). They also check the Eppstein example and the Graphviz output.

## 11. Further reading

- David Eppstein, *Finding the k Shortest Paths*, SIAM Journal on Computing, 1998:
  [paper (PDF)](http://www.ics.uci.edu/~eppstein/pubs/Epp-SJC-98.pdf).
  The algorithm finds the k shortest paths, allowing repeated vertices and edges. After one
  shortest-path computation it needs only constant time per path. It is widely cited in route
  planning, network routing, computational linguistics and bioinformatics.
- [K shortest path routing](https://en.wikipedia.org/wiki/K_shortest_path_routing) on Wikipedia,
  including variants that only allow paths without repeated vertices (Yen's algorithm).
- [Graphviz documentation](https://graphviz.org/documentation/).
