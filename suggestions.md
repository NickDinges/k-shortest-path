# Suggestions

Ideas for increasing the value of this project, from the critical review of November 2026
(see section 7.4 of the [paper](paper/main.pdf) for what the review found in the paper itself).
They are ordered by expected benefit.

1. [Experiments on real road networks](#1-experiments-on-real-road-networks)
2. [Skip spur searches when the tree path is a valid spur](#2-skip-spur-searches-when-the-tree-path-is-a-valid-spur)
3. [Separate the query state from the graph](#3-separate-the-query-state-from-the-graph)
4. [Replace the ball of the detour bound by the exact region](#4-replace-the-ball-of-the-detour-bound-by-the-exact-region)
5. [Publish the first release and get a DOI](#5-publish-the-first-release-and-get-a-doi)
6. [Bring the Rust tests up to the C# and Go suites](#6-bring-the-rust-tests-up-to-the-c-and-go-suites)
7. [Reduce the memory use of Yen's algorithm](#7-reduce-the-memory-use-of-yens-algorithm)

Suggestions that have been carried out are removed from this list.

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

## 5. Publish the first release and get a DOI

Everything for a release is prepared: package metadata, a workflow that publishes the NuGet
package, the crate and the Go module from one tag, and `CITATION.cff` (see
[docs/releasing.md](docs/releasing.md)). Nothing is published yet, because publishing needs the
owner's accounts.

What to do: store the API keys of nuget.org and crates.io as repository secrets, then push the tag
`v2.0.0` on the default branch. Connect the repository to Zenodo before that, so that the release
gets a DOI, and add the DOI to `CITATION.cff` and to the paper.

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
