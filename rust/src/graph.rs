//! Storage of the graph: vertices, edges, edge lists by direction.

use std::collections::HashMap;

use crate::arcflags::ArcFlagsState;
use crate::ch::ChState;
use crate::labels::Labels;
use crate::paths::QueryState;
use crate::simple::SimpleState;

/// A directed graph with weighted edges (64-bit weights; negative weights switch an edge off).
///
/// Vertices have case-insensitive labels (stored in upper case) and indices 0..n; edges have
/// numbers 0..m in order of creation.
#[derive(Default)]
pub struct Graph {
    pub(crate) labels: Labels,

    pub(crate) edge_tail: Vec<u32>,
    pub(crate) edge_head: Vec<u32>,
    pub(crate) edge_weight: Vec<i64>,
    pub(crate) edge_group: Vec<u32>,
    pub(crate) groups: Vec<String>,
    pub(crate) group_index: HashMap<String, u32>,

    // Edges by direction (compressed rows), rebuilt on demand after a change
    pub(crate) adjacency_valid: bool,
    pub(crate) out_start: Vec<u32>,
    pub(crate) out_edges: Vec<u32>,
    pub(crate) in_start: Vec<u32>,
    pub(crate) in_edges: Vec<u32>,
    pub(crate) change_count: u64,

    pub(crate) q: QueryState,
    pub(crate) simple: SimpleState,
    pub(crate) arc: ArcFlagsState,
    pub(crate) ch: ChState,
}

fn is_valid_label(label: &str) -> bool {
    let mut chars = label.chars();
    match chars.next() {
        Some(c) if c.is_alphabetic() => chars.all(|c| c.is_alphabetic() || c.is_numeric()),
        _ => false,
    }
}

impl Graph {
    /// Creates an empty graph.
    pub fn new() -> Self {
        Self::default()
    }

    fn init(&mut self) {
        if self.groups.is_empty() {
            self.groups.push(String::new());
            self.group_index.insert(String::new(), 0);
        }
    }

    /// Number of vertices; valid indices are 0..vertex_count().
    pub fn vertex_count(&self) -> usize {
        self.labels.len()
    }

    /// Number of edges; valid edge numbers are 0..edge_count().
    pub fn edge_count(&self) -> usize {
        self.edge_tail.len()
    }

    /// Label of a vertex. Panics if the index is not valid.
    pub fn label(&self, vertex: usize) -> &str {
        &self.labels[vertex]
    }

    /// Index of the vertex with a label (case-insensitive).
    pub fn vertex_index(&self, label: &str) -> Option<usize> {
        if label.is_empty() {
            return None;
        }
        if let Some(v) = self.labels.find(label) {
            return Some(v);
        }
        if label.bytes().any(|b| b.is_ascii_lowercase()) || !label.is_ascii() {
            return self.labels.find(&label.to_uppercase());
        }
        None
    }

    /// Creates all vertices from a comma-separated list of labels, removing all previous vertices
    /// and edges (and disabling arc flags). Empty and repeated labels are ignored. Returns false
    /// at the first invalid label (the vertices before it are created).
    pub fn create_vertices(&mut self, vertices: &str) -> bool {
        self.clear();
        for label in vertices.split(',') {
            if !label.is_empty() && self.add_vertex(label).is_none() {
                return false;
            }
        }
        true
    }

    /// Adds one vertex and returns its index (the existing one if the label is already used), or
    /// None if the label is invalid. A label starts with a letter and contains only letters and
    /// digits.
    pub fn add_vertex(&mut self, label: &str) -> Option<usize> {
        self.init();
        // No allocation if the label is already upper case (the common case)
        let converted;
        let upper = if label.bytes().any(|b| b.is_ascii_lowercase()) || !label.is_ascii() {
            converted = label.to_uppercase();
            converted.as_str()
        } else {
            label
        };
        if !is_valid_label(upper) {
            return None;
        }
        if let Some(v) = self.labels.find(upper) {
            return Some(v);
        }
        let index = self.labels.push(upper);
        self.changed(true);
        Some(index)
    }

    /// Creates edges from tails to heads (comma-separated labels, paired by position), all with
    /// the same weight and group. Returns false, creating no edge, if a vertex does not exist or
    /// the lists have different lengths.
    pub fn create_edges(&mut self, tails: &str, heads: &str, weight: i64, group: &str) -> bool {
        let tails: Vec<&str> = tails.split(',').collect();
        let heads: Vec<&str> = heads.split(',').collect();
        if tails.len() != heads.len() {
            return false;
        }
        let mut pairs = Vec::with_capacity(tails.len());
        for (t, h) in tails.iter().zip(&heads) {
            match (self.vertex_index(t), self.vertex_index(h)) {
                (Some(t), Some(h)) => pairs.push((t, h)),
                _ => return false,
            }
        }
        for (t, h) in pairs {
            self.add_edge_group(t, h, weight, group);
        }
        true
    }

    /// Adds an edge without a group between vertices given by index; returns its number, None if
    /// an index is not valid.
    pub fn add_edge(&mut self, tail: usize, head: usize, weight: i64) -> Option<usize> {
        self.add_edge_group(tail, head, weight, "")
    }

    /// Adds an edge with a group (case-insensitive, see [`Graph::edge_group_weights`]).
    pub fn add_edge_group(&mut self, tail: usize, head: usize, weight: i64, group: &str) -> Option<usize> {
        let n = self.labels.len();
        if tail >= n || head >= n {
            return None;
        }
        self.init();
        let id = self.edge_tail.len();
        let group = self.group_id(group);
        self.edge_tail.push(tail as u32);
        self.edge_head.push(head as u32);
        self.edge_weight.push(weight);
        self.edge_group.push(group);
        self.changed(true);
        Some(id)
    }

    /// Tail vertex of an edge. Panics if the number is not valid.
    pub fn edge_tail(&self, edge: usize) -> usize {
        self.edge_tail[edge] as usize
    }

    /// Head vertex of an edge.
    pub fn edge_head(&self, edge: usize) -> usize {
        self.edge_head[edge] as usize
    }

    /// Weight of an edge.
    pub fn edge_weight(&self, edge: usize) -> i64 {
        self.edge_weight[edge]
    }

    /// Group of an edge (upper case, empty for none).
    pub fn edge_group(&self, edge: usize) -> &str {
        &self.groups[self.edge_group[edge] as usize]
    }

    /// Changes the weight of one edge. Panics if the number is not valid.
    pub fn set_edge_weight(&mut self, edge: usize, weight: i64) {
        self.edge_weight[edge] = weight;
        self.changed(false);
    }

    /// Sets the weight of all edges of a group (case-insensitive).
    pub fn edge_group_weights(&mut self, group: &str, weight: i64) {
        let Some(&id) = self.group_index.get(&group.to_uppercase()) else {
            return;
        };
        for (w, &g) in self.edge_weight.iter_mut().zip(&self.edge_group) {
            if g == id {
                *w = weight;
            }
        }
        self.changed(false);
    }

    /// Edges leaving a vertex, in order of creation.
    pub fn out_edges(&mut self, vertex: usize) -> Vec<usize> {
        self.ensure_adjacency();
        let (a, b) = (self.out_start[vertex] as usize, self.out_start[vertex + 1] as usize);
        self.out_edges[a..b].iter().map(|&e| e as usize).collect()
    }

    /// Edges entering a vertex, in order of creation.
    pub fn in_edges(&mut self, vertex: usize) -> Vec<usize> {
        self.ensure_adjacency();
        let (a, b) = (self.in_start[vertex] as usize, self.in_start[vertex + 1] as usize);
        self.in_edges[a..b].iter().map(|&e| e as usize).collect()
    }

    fn group_id(&mut self, group: &str) -> u32 {
        if group.is_empty() {
            return 0;
        }
        if let Some(&id) = self.group_index.get(group) {
            return id;
        }
        let upper = group.to_uppercase();
        if let Some(&id) = self.group_index.get(&upper) {
            return id;
        }
        let id = self.groups.len() as u32;
        self.groups.push(upper.clone());
        self.group_index.insert(upper, id);
        id
    }

    /// Removes all vertices and edges and disables arc flags.
    fn clear(&mut self) {
        self.labels.clear();
        self.edge_tail.clear();
        self.edge_head.clear();
        self.edge_weight.clear();
        self.edge_group.clear();
        self.groups.clear();
        self.group_index.clear();
        self.init();
        self.disable_arc_flags();
        self.changed(true);
    }

    /// Invalidates the current query and precomputed data after a change.
    pub(crate) fn changed(&mut self, structure: bool) {
        self.change_count += 1;
        self.q.ready = false;
        if structure {
            self.adjacency_valid = false;
        }
        self.arc.valid = false;
        self.ch.valid = false;
    }

    /// Builds the edge lists by direction (counting sort) if the graph changed.
    pub(crate) fn ensure_adjacency(&mut self) {
        if self.adjacency_valid {
            return;
        }
        let n = self.labels.len();
        (self.out_start, self.out_edges) = build_rows(&self.edge_tail, n);
        (self.in_start, self.in_edges) = build_rows(&self.edge_head, n);
        self.adjacency_valid = true;
    }
}

/// Groups the edges by a vertex: rows[start[v]..start[v+1]] are the edges of v.
fn build_rows(vertex_of_edge: &[u32], n: usize) -> (Vec<u32>, Vec<u32>) {
    let mut start = vec![0u32; n + 1];
    for &v in vertex_of_edge {
        start[v as usize + 1] += 1;
    }
    for v in 0..n {
        start[v + 1] += start[v];
    }
    let mut rows = vec![0u32; vertex_of_edge.len()];
    let mut next = start.clone();
    for (e, &v) in vertex_of_edge.iter().enumerate() {
        rows[next[v as usize] as usize] = e as u32;
        next[v as usize] += 1;
    }
    (start, rows)
}
