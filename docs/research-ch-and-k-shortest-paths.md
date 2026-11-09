# Contraction hierarchies and k shortest paths: assessment

Status: November 2026. Question: should the library use contraction hierarchies (CH), customizable
CH (CCH) or PHAST to get faster, and is "Eppstein / k shortest paths + CH" a research contribution?

## Where the time goes today

Measured with the current implementation (C#, .NET 10, Release) on grid graphs with two-way streets
and random weights 1–100, `SimplePathsOnly = true`:

| Scenario | Shortest-path tree (Dijkstra) | Spur searches (Yen) |
|---|---|---|
| 40,000 vertices, corner to corner, 100 routes (406 edges each) | 36 ms | 14,162 searches, 244 vertices settled each, **798 ms** |
| 1,000,000 vertices, nearby points, 10 routes, no corridor | **353 ms** (all 1M vertices) | 651 searches, 40 vertices each, 32 ms |
| same, `MaxDetourRatio = 0.1` | 9 ms (18,280 vertices) | 651 searches, 40 vertices each, 1 ms |

Two different bottlenecks:

1. **Long routes, many alternatives:** the *number* of spur searches. Each search is already cheap
   (2–60 µs): A* with the exact distances to the goal as lower bound explores little more than the
   spur itself.
2. **Large graph without corridor:** the shortest-path tree over the whole graph. With a corridor it
   is already small.

## What CH-based techniques would change

| Technique | Helps with | Effect here |
|---|---|---|
| CH point-to-point queries | single shortest paths | Little: Eppstein needs the whole tree; Yen's spur searches are already goal-directed by exact bounds, so a CH query would not settle far fewer vertices, and shortcuts must be unpacked. |
| PHAST (one-to-all on a CH, linear sweep) | the shortest-path tree | Bottleneck 2 without a corridor. The paper reports about an order of magnitude over Dijkstra on road networks (more with SIMD/multicore). Tree edges can be recovered in one extra linear pass. |
| RPHAST (restricted PHAST, one-to-many) | trees restricted to a target set | Fits the corridor, but the corridor tree is already 9 ms. |
| CCH (fast re-customization of weights) | changing weights | Useful for `SetEdgeWeight`/`EdgeGroupWeights`-heavy workloads; not for the per-spur blocking of Yen (re-customizing per spur is far too expensive). |

Costs: CH preprocessing (seconds to minutes per graph, re-done after changes, or CCH), extra memory
for shortcuts, an implementation in both C# and Go, and arc flags would become largely redundant.

## Literature (closest work found)

- **Sidetrack-based simple paths:** D. Kurz, P. Mutzel, *A Sidetrack-Based Algorithm for Finding the
  k Shortest Simple Paths in a Directed Graph*, ISAAC 2016
  ([LIPIcs](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.ISAAC.2016.49),
  [arXiv](https://www.arxiv.org/abs/1601.02867)). Eppstein's sidetrack idea for simple paths; with
  one tree it produces many simple paths without spur searches; reported about an order of magnitude
  faster than earlier algorithms on random graphs and large road networks.
- **Improvements of it:** A. Al Zoobi, D. Coudert, N. Nisse, *Space and Time Trade-Off for the k
  Shortest Simple Paths Problem*, SEA 2020
  ([LIPIcs](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.SEA.2020.18)), and the journal
  version *Finding the k Shortest Simple Paths: Time and Space Trade-offs*, ACM JEA 2023
  ([dblp](https://dblp1.uni-trier.de/rec/journals/jea/ZoobiCN23.html),
  [report](https://hal.archives-ouvertes.fr/hal-03196830v1)): dynamic updates of the shortest-path
  tree, less memory, and a postponed node-classification algorithm (PNC); evaluated on road networks.
- **PHAST:** D. Delling, A. Goldberg, A. Nowatzyk, R. Werneck, *PHAST: Hardware-Accelerated Shortest
  Path Trees* (IPDPS 2011, JPDC) ([paper](https://i11www.iti.kit.edu/extra/publications/dgnw-phast-12.pdf)).
- **RPHAST:** D. Delling, A. Goldberg, R. Werneck, *Faster Batched Shortest Paths in Road Networks*,
  ATMOS 2011 ([MSR](https://www.microsoft.com/en-us/research/?p=161798)).
- **CCH:** J. Dibbelt, B. Strasser, D. Wagner, *Customizable Contraction Hierarchies*, SEA 2014 / JEA
  2016 ([paper](https://i11www.iti.kit.edu/extra/publications/dsw-cch-sea-14.pdf)); survey 2025
  ([arXiv 2502.10519](https://arxiv.org/pdf/2502.10519)).
- **Alternative routes (a different problem):** I. Abraham, D. Delling, A. Goldberg, R. Werneck,
  *Alternative Routes in Road Networks*, JEA 2013 (via-node alternatives on CH); T. Chondrogiannis et
  al., *Finding k-shortest paths with limited overlap*, VLDB Journal 2020
  ([FAU](https://cris.fau.de/publications/259224002)).

A web search found no work that combines Eppstein's algorithm or the sidetrack-based simple-path
algorithms with CH/PHAST/CCH. That is not proof that none exists; a proper literature review (and
the related-work sections of the papers above) is needed before claiming novelty.

## Decision

1. **No CH/CCH in the library for now.** The measured bottlenecks are elsewhere (number of spur
   searches) or already solved by the corridor; the cost (preprocessing, memory, two
   implementations) is high.
2. **Next performance step for simple paths: the sidetrack-based algorithm (Kurz & Mutzel) or PNC
   (Al Zoobi et al.).** It attacks bottleneck 1 directly by avoiding most spur searches, and it reuses
   the existing Eppstein machinery (tree, sidetracks, heaps).
3. **PHAST is the right tool if queries without a corridor on very large graphs matter**
   (bottleneck 2). It would be a self-contained addition (CH preprocessing + linear sweep for the
   tree) that leaves the rest unchanged.
4. **Research direction, if a paper is the goal:** "sidetrack-based k shortest simple paths
   (SB/PNC) with (R)PHAST or CCH for the (repeated) shortest-path-tree computations, on large road
   networks". The SB/PNC line already updates trees dynamically, so the question is whether
   CH-based trees make those updates faster. Prerequisites: the literature review above, and real
   road networks (DIMACS 9 `.gr`, OSM). Note: the DIMACS 9 server only serves plain HTTP, which the
   current cloud environment cannot reach; the data has to be downloaded elsewhere.
