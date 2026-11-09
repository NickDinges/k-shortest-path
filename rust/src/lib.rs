//! k shortest paths between two vertices of a directed graph, in order of weight.
//!
//! Rust port of the C# library (same algorithms, same results, shared golden tests):
//!
//! * Eppstein's algorithm ("Finding the k Shortest Paths", 1998): paths may repeat vertices;
//!   after one shortest-path tree, every further path costs O(log k) time.
//! * Yen's algorithm with Lawler's improvement and A* spur searches for simple paths
//!   ([`Graph::set_simple_paths_only`]).
//! * A detour corridor ([`Graph::set_max_detour`], [`Graph::set_max_detour_ratio`]).
//! * Optional arc flags and an optional contraction hierarchy with PHAST.
//!
//! ```
//! use eppstein::Graph;
//!
//! let mut g = Graph::new();
//! g.create_vertices("S,A,T");
//! g.create_edges("S,A,S", "A,T,T", 1, "roads");
//! let mut paths = g.paths("S", "T");
//! while let Some(p) = paths.next() {
//!     println!("{} {}", paths.graph().vertex_names(&p), p.weight());
//!     if p.weight() > 5 {
//!         break;
//!     }
//! }
//! ```
//!
//! Memory: vertices and edges live in plain vectors, queries reuse per-vertex arrays with stamps
//! (no reset), and the persistent sidetrack heaps and candidates live in arenas that are cleared
//! at the next query. There is no garbage collector and no reference counting.

mod arcflags;
mod ch;
mod graph;
mod import;
mod labels;
mod paths;
mod pqueue;
mod serialize;
mod simple;

pub use graph::Graph;
pub use import::{ImportError, DEFAULT_MAX_IMPORT_VERTICES};
pub use paths::{Path, Paths, Weights};

/// Marker for "no vertex / no edge / no heap node" in the internal u32 arrays.
pub(crate) const NONE: u32 = u32::MAX;

pub(crate) fn saturating_add(a: i64, b: i64) -> i64 {
    a.saturating_add(b)
}
