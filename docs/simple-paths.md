# Routes without loops: `SimplePathsOnly`

## The problem: paths with loops

By default the library returns the k shortest **walks**: a path may visit the same vertex more than
once. That is what Eppstein's algorithm computes, and it is very fast. But in networks where
connections go **both ways** (roads, computer networks, pipes, game maps) most of these walks are
useless as alternatives. Here is the road network from [Your first program](../README.md#your-first-program), with every road in both
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

## How to use it

```csharp
g.SimplePathsOnly = true;   // default: false (walks)

foreach (GraphPath route in g.EnumeratePaths("HOME", "OFFICE").Take(3))
    Console.WriteLine(route);   // the 3 best routes without loops
```

Everything else works as before: `FindShortestPath`, `FindNextShortestPath`, `EnumeratePaths`,
the corridor (`MaxDetour`, `MaxDetourRatio`) and the Graphviz output. After changing the setting,
call `FindShortestPath` (or start a new `EnumeratePaths`) again. In Go:
`g.SetSimplePathsOnly(true)`.

## Which mode should I use?

| Situation | Use |
|---|---|
| Connections go both ways (roads, networks, maps) and you want alternative routes | **`SimplePathsOnly = true`** |
| The graph has no cycles (e.g. schedules, dependency graphs, speech or sequence lattices) | Default: both modes give the same paths, the default is much faster |
| Repeating a vertex is meaningful (e.g. a state machine that may stay in a state) | Default |
| You need thousands of paths and loops do not matter | Default |

## What it costs

Simple paths are much harder to find than walks:

- **Walks (default):** one shortest-path search in total; after that every further path costs
  almost nothing.
- **Simple paths:** every further path needs several shortest-path searches (one for each
  possible place where a new route can branch off). They are fast because they are guided by the
  distances to the goal (see [How the algorithm works](how-it-works.md)), but they add up.

Measured on two-way grid streets with 1 million vertices, between two nearby points
(details in [Performance](performance.md)):

| | Without corridor | With `MaxDetourRatio = 0.1` |
|---|---|---|
| Shortest route (repeated query) | 371 ms | 6 ms |
| + 2 alternatives | + 7 ms | + 6 ms |
| + 9 alternatives | + 13 ms | + 18 ms |

**Tips:**

- **Combine it with a corridor** (`MaxDetourRatio`, [Faster queries](faster-queries.md)). It limits the first search to the
  area around the route, and the branching searches stop as soon as no allowed route is possible.
  In practice you rarely want an alternative that is twice as long anyway.
- **Ask only for what you need** (`Take(3)`): the searches for the next route only run when you
  ask for it. The first route costs nothing extra.
- The number of simple paths is finite, so a loop over `EnumeratePaths` ends by itself. But it can
  be astronomically large in big networks, so still use `Take` or a corridor.
- `EnumeratePathWeights` is not faster than `EnumeratePaths` in this mode (the paths are built
  anyway), and arc flags only speed up the first search.

---

[Back to the main README](../README.md)
