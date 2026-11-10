# k-shortest-path (Rust)

Rust port of the [C# library](../README.md), with the same algorithms and results as the C# and
[Go](../go/README.md) versions. It checks the same golden data in `../testdata/golden`.

> **Paper:** [`../paper/main.pdf`](../paper/main.pdf) compares this Rust version with the C# and Go
> versions on identical graphs. See [the summary in the main README](../README.md#the-paper).

- Eppstein's k shortest paths (paths may repeat vertices), with lazily built persistent heaps
- `set_simple_paths_only(true)`: k shortest simple paths (Yen with Lawler's rule, A* spur searches)
- corridor: `set_max_detour`, `set_max_detour_ratio`
- arc flags (`enable_arc_flags`, `enable_arc_flags_regions`, `enable_arc_flags_from_partition`,
  `write_metis_graph`) and a contraction hierarchy with PHAST (`enable_contraction_hierarchy`)
- CSV and DIMACS import, Graphviz output (`serialize`)

```rust
use eppstein::Graph;

let mut g = Graph::new();
g.create_vertices("S,A,T");
g.create_edges("S,A,S", "A,T,T", 1, "roads");
let mut paths = g.paths("S", "T");
while let Some(p) = paths.next() {
    println!("{} {}", paths.graph().vertex_names(&p), p.weight());
    if p.weight() > 5 {
        break; // the cycle S->A->S gives infinitely many paths
    }
}
```

## API: C# → Rust

| C# | Rust |
|---|---|
| `g.AddVertex(label)` → `int` (`-1` if invalid) | `g.add_vertex(label)` → `Option<usize>` |
| `g.AddEdge(t, h, w, group)` → `int` | `g.add_edge_group(t, h, w, group)` → `Option<usize>` (`add_edge` without group) |
| `g.FindShortestPath(s, t)` / `FindNextShortestPath()` | `g.find_shortest_path(s, t)` / `find_next_shortest_path()` → `Path` |
| `foreach (var p in g.EnumeratePaths(s, t))` | `g.paths(s, t)` (iterator; `paths.graph()` reads the graph) |
| `g.EnumeratePathWeights(s, t)` | `g.path_weights(s, t)` |
| `p.VertexNames` | `g.vertex_names(&p)` |
| `vertex.Distance`, `vertex.TreeEdge` | `g.distance(v)`, `g.tree_edge(v)` → `Option` |
| `g.MaxDetour = 15` | `g.set_max_detour(15)` → `bool` |
| `g.ImportDimacs(reader)` (throws) | `g.import_dimacs(reader)` → `Result<usize, ImportError>` |
| enumeration after a change: exception | does not compile: the iterator borrows the graph mutably |

## Differences in the implementation

- Persistent heap nodes and candidates live in arenas (vectors with `u32` links), cleared at the next
  query: no garbage collector, no reference counting, no `unsafe`.
- Labels are stored once in one string, indexed by an open-addressing hash table.

## Build, test, benchmark

```sh
cargo test --release
cargo clippy --all-targets -- -D warnings
cargo run --release --bin benchmark -- 200 1000 --k 10000 --build index
```

The benchmark is identical to the C# (`Eppstein.Benchmarks`) and Go (`go/cmd/benchmark`) programs: same
graphs (SplitMix64, seed 1), same options and output, so the weight of the last path must match.
`paper/scripts/run_benchmarks.py` runs all three and compares them.
