# Suggestions

Ideas for increasing the value of this project, from the critical review of November 2026
(see section 7.4 of the [paper](paper/main.pdf) for what the review found in the paper itself).
They are ordered by expected benefit.

| # | Suggestion | Status |
|---|---|---|
| 1 | Experiments on real road networks | open |
| 2 | Skip spur searches when the tree path is a valid spur | open |
| 3 | Separate the query state from the graph | open |
| 4 | Replace the ball of the detour bound by the exact region | open |
| 5 | Publish the packages and make the paper citable | open |
| 6 | Bring the Rust tests up to the C# and Go suites | open |
| 7 | Reduce the memory use of Yen's algorithm | open |
| 8 | Shorten the main README | open |

## 1. Experiments on real road networks

All measurements in the paper use synthetic grids. Grids have no hierarchy of roads, so they are a
hard case for contraction hierarchies, and the one-way grids are almost acyclic. The paper's title
names contraction hierarchies, but whether they pay off for k shortest paths on road networks is
still open. This is the biggest scientific weakness.

What to do: run the benchmark on the DIMACS road networks (9th DIMACS Implementation Challenge) or
on an OpenStreetMap extract. The importers for the DIMACS format exist in all three languages; the
benchmark programs need an option to read a file and to pick start and goal vertices. The download
host was not reachable from the development environment, so either the host has to be allowed
there or the files have to be added by hand.

## 2. Skip spur searches when the tree path is a valid spur

Simple paths cost 5 to 10 ms each on a grid with 40,000 vertices, where a path with repeated
vertices costs microseconds. They are the slow part of the library. Every spur is found by an A*
search today, even when the shortest-path tree already contains the answer: if the tree path from
the spur vertex to the goal does not touch the root and does not start with a blocked edge, it is
the spur, and no search is needed.

What to do: check the tree path first (Feng 2014; Kurz and Mutzel 2016), and fall back to A* only
when the check fails. The next step would be the sidetrack-based algorithms of Kurz and Mutzel and
of Al Zoobi, Coudert and Nisse, which avoid most spur searches altogether.

## 3. Separate the query state from the graph

A query stores its distances, heaps and candidates inside the graph object. One graph therefore
serves one thread at a time, and starting a second query ends the first.

What to do: move the per-query data into a query object that only reads the graph. Several
queries could then run in parallel on one graph, which is what a routing service needs, and the
Rust iterator would no longer have to borrow the graph mutably.

## 4. Replace the ball of the detour bound by the exact region

The detour bound (corridor) stops the backward search at the distance "shortest path + allowed
detour" from the goal. The region it searches is a ball around the goal. This is small when start
and goal are close, and the whole graph when they are far apart. The exact region is an ellipse
around the route: a vertex matters only if its distance from the start plus its distance to the
goal is within the bound.

What to do: add a forward search from the start (bidirectional), or build the path graph on the
fly during an A* search as K* does (Aljazzar and Leue 2011).

## 5. Publish the packages and make the paper citable

None of the three libraries is published: there is no release tag, no NuGet package, no crate and
no tagged Go module. Nobody can use the library without cloning the repository, and the paper has
no citation metadata.

What to do: tag a release, push the NuGet package and the crate, tag the Go module, and add a
`CITATION.cff` file. A DOI for the paper and the code (for example through Zenodo) would make both
citable in the long term.

## 6. Bring the Rust tests up to the C# and Go suites

The Rust tests have 352 lines, against 1,212 (C#) and 1,733 (Go), and CI enforces no coverage
for Rust. The Rust version relies on the shared golden data and one brute-force test.

What to do: port the property-based and fuzz tests (`proptest`, `cargo fuzz`) and add a coverage
gate (`cargo llvm-cov`), as for the other two languages.

## 7. Reduce the memory use of Yen's algorithm

Every candidate path is stored as a full list of edges, and a second copy sits in the set that
detects duplicates. With k paths of up to l edges this is O(k l^2) memory in the worst case. The Go
version allocates 940 MB for 1,000 paths on a grid with 40,000 vertices.

What to do: store a candidate as a pointer to its root in the trie plus its spur, and detect
duplicates through the trie instead of a set of full paths.

## 8. Shorten the main README

The main README has almost 1,000 lines: tutorial, API reference, algorithm description, benchmark
tables and development notes in one file. A visitor cannot see at a glance what the project is and
how to start.

What to do: keep the overview, the quick start and the links in the README, and move the reference
material into separate files under `docs/`.
