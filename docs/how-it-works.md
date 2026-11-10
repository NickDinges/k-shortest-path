# How the algorithm works

A description in plain words. The [paper](../paper/main.pdf) explains the same algorithms in full,
with a worked example and their running times.

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

## Simple paths (`SimplePathsOnly`): Yen and Lawler

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

## Further reading

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
- The paper on this library ([The paper](../README.md#the-paper)): algorithms, the C#/Go/Rust comparison and
  how the library was developed with Claude.

---

[Back to the main README](../README.md)
