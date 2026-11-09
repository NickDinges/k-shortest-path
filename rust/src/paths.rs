//! Queries: shortest-path tree (Dijkstra, corridor), Eppstein's path enumeration, iterators.

use crate::pqueue::PriorityQueue;
use crate::{saturating_add, Graph, NONE};

/// Node of a persistent leftist heap of sidetracks, ordered by delta (Eppstein's H_T). Nodes live
/// in an arena (vector) that is cleared at the next query; links are indices.
#[derive(Clone, Copy)]
pub(crate) struct HeapNode {
    edge: u32,
    key: i64,
    rank: u32,
    left: u32,
    right: u32,
}

/// A candidate path: the sidetracks of its parent plus the sidetrack of one heap node.
#[derive(Clone, Copy)]
pub(crate) struct Candidate {
    parent: u32,
    heap: u32,
    weight: i64,
}

/// The current query. A vertex's entries are valid only if its stamp equals the query stamp.
pub(crate) struct QueryState {
    pub(crate) query_stamp: u32,
    pub(crate) stamp: Vec<u32>,
    pub(crate) distance: Vec<i64>,
    pub(crate) tree_edge: Vec<u32>,
    heap_stamp: Vec<u32>,
    heaps: Vec<u32>,

    pub(crate) ready: bool,
    pub(crate) source: usize,
    pub(crate) target: usize,
    pub(crate) path_bound: i64,
    nodes: Vec<HeapNode>,
    candidates: Vec<Candidate>,
    queue: PriorityQueue<u32>,
    fringe: PriorityQueue<u32>,
    pending: Vec<u32>,
    own: Vec<u32>,
    pub(crate) candidates_created: u64,

    pub(crate) max_detour: Option<i64>,
    pub(crate) max_detour_ratio: Option<f64>,
}

impl Default for QueryState {
    fn default() -> Self {
        QueryState {
            query_stamp: 0,
            stamp: Vec::new(),
            distance: Vec::new(),
            tree_edge: Vec::new(),
            heap_stamp: Vec::new(),
            heaps: Vec::new(),
            ready: false,
            source: 0,
            target: 0,
            path_bound: -1,
            nodes: Vec::new(),
            candidates: Vec::new(),
            queue: PriorityQueue::new(),
            fringe: PriorityQueue::new(),
            pending: Vec::new(),
            own: Vec::new(),
            candidates_created: 0,
            max_detour: None,
            max_detour_ratio: None,
        }
    }
}

/// A path: edge numbers from start to goal and the total weight. Empty if there is no path.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct Path {
    pub(crate) edges: Vec<u32>,
    pub(crate) weight: i64,
}

impl Path {
    /// True if the path has at least one edge.
    pub fn is_valid(&self) -> bool {
        !self.edges.is_empty()
    }

    /// Total weight.
    pub fn weight(&self) -> i64 {
        self.weight
    }

    /// Number of edges.
    pub fn len(&self) -> usize {
        self.edges.len()
    }

    /// True if the path has no edges.
    pub fn is_empty(&self) -> bool {
        self.edges.is_empty()
    }

    /// Edge numbers in order.
    pub fn edges(&self) -> impl Iterator<Item = usize> + '_ {
        self.edges.iter().map(|&e| e as usize)
    }
}

/// Iterator over paths in order of weight, see [`Graph::paths`]. It borrows the graph mutably, so
/// the graph cannot change during the iteration (checked by the compiler); use
/// [`Paths::graph`] to read it.
pub struct Paths<'a> {
    graph: &'a mut Graph,
}

impl Paths<'_> {
    /// The graph, e.g. for [`Graph::vertex_names`].
    pub fn graph(&self) -> &Graph {
        self.graph
    }
}

impl Iterator for Paths<'_> {
    type Item = Path;
    fn next(&mut self) -> Option<Path> {
        let p = self.graph.find_next_shortest_path();
        p.is_valid().then_some(p)
    }
}

/// Iterator over path weights, see [`Graph::path_weights`].
pub struct Weights<'a> {
    graph: &'a mut Graph,
}

impl Iterator for Weights<'_> {
    type Item = i64;
    fn next(&mut self) -> Option<i64> {
        self.graph.next_result(false).map(|(_, w)| w)
    }
}

impl Graph {
    /// Limits the paths to at most `detour` longer than the shortest one; `i64::MAX` removes the
    /// limit. Returns false if negative.
    pub fn set_max_detour(&mut self, detour: i64) -> bool {
        if detour < 0 {
            return false;
        }
        self.q.max_detour = (detour != i64::MAX).then_some(detour);
        self.settings_changed();
        true
    }

    /// Maximum absolute detour, `i64::MAX` if there is no limit.
    pub fn max_detour(&self) -> i64 {
        self.q.max_detour.unwrap_or(i64::MAX)
    }

    /// Limits the paths relative to the shortest one (0.2: at most 20% longer); infinity removes
    /// the limit. Returns false if negative or NaN.
    pub fn set_max_detour_ratio(&mut self, ratio: f64) -> bool {
        if ratio.is_nan() || ratio < 0.0 {
            return false;
        }
        self.q.max_detour_ratio = (ratio != f64::INFINITY).then_some(ratio);
        self.settings_changed();
        true
    }

    /// Maximum relative detour, infinity if there is no limit.
    pub fn max_detour_ratio(&self) -> f64 {
        self.q.max_detour_ratio.unwrap_or(f64::INFINITY)
    }

    pub(crate) fn settings_changed(&mut self) {
        self.change_count += 1;
        self.q.ready = false;
    }

    pub(crate) fn has_corridor(&self) -> bool {
        self.q.max_detour.is_some() || self.q.max_detour_ratio.is_some()
    }

    /// Allowed detour for a given shortest path weight.
    pub(crate) fn detour_bound(&self, shortest: i64) -> i64 {
        let mut bound = self.max_detour();
        if let Some(ratio) = self.q.max_detour_ratio {
            let relative = (ratio * shortest as f64).floor();
            if relative < bound as f64 {
                bound = relative as i64;
            }
        }
        bound
    }

    /// Computes the shortest-path tree towards the goal and returns the shortest path (labels,
    /// case-insensitive). Further paths come from [`Graph::find_next_shortest_path`]. Empty if
    /// there is none, a label does not exist, or start and goal are the same vertex.
    pub fn find_shortest_path(&mut self, start: &str, goal: &str) -> Path {
        match (self.vertex_index(start), self.vertex_index(goal)) {
            (Some(s), Some(t)) => self.find_shortest_path_by_index(s, t),
            _ => {
                self.q.ready = false;
                Path::default()
            }
        }
    }

    /// Like [`Graph::find_shortest_path`] with vertex indices (empty path if not valid).
    pub fn find_shortest_path_by_index(&mut self, start: usize, goal: usize) -> Path {
        if !self.start_query(start, goal) {
            return Path::default();
        }
        self.find_next_shortest_path()
    }

    /// Next shortest path of the current query; empty if there are no more, or the graph or the
    /// settings changed since the query started.
    pub fn find_next_shortest_path(&mut self) -> Path {
        self.next_result(true).map(|(p, _)| p).unwrap_or_default()
    }

    /// All paths from start to goal (labels) in order of weight. Without a corridor, a graph with
    /// cycles has infinitely many: stop the loop yourself.
    pub fn paths(&mut self, start: &str, goal: &str) -> Paths<'_> {
        self.find_shortest_path_query(start, goal);
        Paths { graph: self }
    }

    /// Like [`Graph::paths`] with vertex indices.
    pub fn paths_by_index(&mut self, start: usize, goal: usize) -> Paths<'_> {
        if !self.start_query(start, goal) {
            self.q.ready = false;
        }
        Paths { graph: self }
    }

    /// Weights of all paths in order, without building the paths (faster).
    pub fn path_weights(&mut self, start: &str, goal: &str) -> Weights<'_> {
        self.find_shortest_path_query(start, goal);
        Weights { graph: self }
    }

    /// Like [`Graph::path_weights`] with vertex indices.
    pub fn path_weights_by_index(&mut self, start: usize, goal: usize) -> Weights<'_> {
        if !self.start_query(start, goal) {
            self.q.ready = false;
        }
        Weights { graph: self }
    }

    fn find_shortest_path_query(&mut self, start: &str, goal: &str) {
        let ok = match (self.vertex_index(start), self.vertex_index(goal)) {
            (Some(s), Some(t)) => self.start_query(s, t),
            _ => false,
        };
        if !ok {
            self.q.ready = false;
        }
    }

    /// Distance of a vertex to the goal of the current query (None if unknown or unreachable).
    pub fn distance(&self, vertex: usize) -> Option<i64> {
        let q = &self.q;
        (q.query_stamp != 0 && vertex < q.stamp.len() && q.stamp[vertex] == q.query_stamp).then(|| q.distance[vertex])
    }

    /// Edge from a vertex to the next vertex on its shortest path to the goal.
    pub fn tree_edge(&self, vertex: usize) -> Option<usize> {
        self.distance(vertex)?;
        let e = self.q.tree_edge[vertex];
        (e != NONE).then_some(e as usize)
    }

    /// Vertices of a path, from start to goal.
    pub fn path_vertices(&self, p: &Path) -> Vec<usize> {
        let mut result = Vec::with_capacity(p.edges.len() + 1);
        if let Some(&first) = p.edges.first() {
            result.push(self.edge_tail[first as usize] as usize);
            result.extend(p.edges.iter().map(|&e| self.edge_head[e as usize] as usize));
        }
        result
    }

    /// Labels of the path's vertices, comma-separated ("(empty)" for an empty path).
    pub fn vertex_names(&self, p: &Path) -> String {
        if !p.is_valid() {
            return "(empty)".to_string();
        }
        self.path_vertices(p)
            .iter()
            .map(|&v| &self.labels[v])
            .collect::<Vec<_>>()
            .join(",")
    }

    /// Next path (and weight) from Eppstein or Yen; with `build_path` false, Eppstein only
    /// computes the weight.
    pub(crate) fn next_result(&mut self, build_path: bool) -> Option<(Path, i64)> {
        if self.simple.enabled {
            return self.next_simple_path();
        }
        let c = self.next_candidate()?;
        let weight = self.q.distance[self.q.source] + self.q.candidates[c as usize].weight;
        let p = if build_path {
            self.build_path(c)
        } else {
            Path::default()
        };
        Some((p, weight))
    }

    /// Prepares a query: shortest-path tree towards the goal and the first candidate.
    pub(crate) fn start_query(&mut self, start: usize, goal: usize) -> bool {
        self.q.ready = false;
        let n = self.labels.len();
        if start >= n || goal >= n {
            return false;
        }
        self.ensure_adjacency();
        self.ensure_arc_flags();
        self.ensure_contraction_hierarchy();
        let q = &mut self.q;
        if q.stamp.len() < n {
            let size = n.max(2 * q.stamp.len());
            q.stamp.resize(size, 0);
            q.distance.resize(size, 0);
            q.tree_edge.resize(size, NONE);
            q.heap_stamp.resize(size, 0);
            q.heaps.resize(size, NONE);
        }
        q.query_stamp += 1;
        if q.query_stamp == u32::MAX {
            q.stamp.fill(0);
            q.heap_stamp.fill(0);
            q.query_stamp = 1;
        }
        q.source = start;
        q.target = goal;
        self.build_shortest_path_tree();

        let q = &mut self.q;
        let reached = q.stamp[start] == q.query_stamp;
        q.path_bound = -1;
        if reached {
            let d = q.distance[start];
            self.q.path_bound = self.detour_bound(d);
        }
        let q = &mut self.q;
        q.nodes.clear();
        q.candidates.clear();
        q.queue.clear();
        q.candidates_created = 0;
        let any_path = reached && start != goal;
        if self.simple.enabled {
            self.start_simple_paths(any_path);
        } else if any_path {
            q.candidates.push(Candidate {
                parent: NONE,
                heap: NONE,
                weight: 0,
            });
            q.queue.push(0, 0);
            q.candidates_created = 1;
        }
        self.q.ready = true;
        true
    }

    /// Dijkstra on reversed edges from the goal (or PHAST, see ch.rs). Negative edges are ignored.
    /// With a corridor, the search stops at distance (shortest path + detour).
    fn build_shortest_path_tree(&mut self) {
        if self.use_phast() {
            self.build_shortest_path_tree_phast();
            return;
        }
        let target_region = self.arc_flags_region_of(self.q.target);
        let mut radius = if self.q.source == self.q.target {
            self.detour_bound(0)
        } else {
            i64::MAX
        };
        let mut fringe = std::mem::replace(&mut self.q.fringe, PriorityQueue::new());
        fringe.clear();
        let q = &mut self.q;
        let query = q.query_stamp;
        let (source, target) = (q.source, q.target);
        q.stamp[target] = query;
        q.distance[target] = 0;
        q.tree_edge[target] = NONE;
        let mut v = target as u32;
        loop {
            if v != NONE {
                let dv = self.q.distance[v as usize];
                for i in self.in_start[v as usize]..self.in_start[v as usize + 1] {
                    let e = self.in_edges[i as usize];
                    let w = self.edge_weight[e as usize];
                    if w >= 0
                        && self.q.stamp[self.edge_tail[e as usize] as usize] != query
                        && self.has_arc_flag_internal(e as usize, target_region)
                    {
                        fringe.push(e, w + dv);
                    }
                }
            }
            let Some((edge, d)) = fringe.pop() else { break };
            if d > radius {
                break;
            }
            let tail = self.edge_tail[edge as usize];
            let q = &mut self.q;
            if q.stamp[tail as usize] == query {
                v = NONE;
                continue;
            }
            v = tail;
            q.stamp[v as usize] = query;
            q.distance[v as usize] = d;
            q.tree_edge[v as usize] = edge;
            if v as usize == source {
                radius = saturating_add(d, self.detour_bound(d));
            }
        }
        self.q.fringe = fringe;
    }

    /// Extracts the next candidate and enqueues its successors (Eppstein's path graph): the two
    /// heap children of its last sidetrack and the best sidetrack after it. At most 3 per path.
    fn next_candidate(&mut self) -> Option<u32> {
        if !self.q.ready {
            return None;
        }
        let (c, _) = self.q.queue.pop()?;
        let cand = self.q.candidates[c as usize];
        if cand.heap == NONE {
            let h = self.sidetrack_heap_of(self.q.source as u32);
            self.enqueue_candidate(c, h);
        } else {
            let last = self.q.nodes[cand.heap as usize];
            self.enqueue_candidate(cand.parent, last.left);
            self.enqueue_candidate(cand.parent, last.right);
            let h = self.sidetrack_heap_of(self.edge_head[last.edge as usize]);
            self.enqueue_candidate(c, h);
        }
        Some(c)
    }

    fn enqueue_candidate(&mut self, parent: u32, h: u32) {
        let q = &mut self.q;
        if h == NONE {
            return;
        }
        let weight = q.candidates[parent as usize].weight + q.nodes[h as usize].key;
        if weight > q.path_bound {
            return;
        }
        let id = q.candidates.len() as u32;
        q.candidates.push(Candidate {
            parent,
            heap: h,
            weight,
        });
        q.queue.push(id, weight);
        q.candidates_created += 1;
    }

    /// Heap of all sidetracks on the tree path from v to the goal, built on demand (iteratively).
    fn sidetrack_heap_of(&mut self, v: u32) -> u32 {
        let query = self.q.query_stamp;
        if self.q.stamp[v as usize] != query {
            return NONE;
        }
        let mut pending = std::mem::take(&mut self.q.pending);
        let mut own = std::mem::take(&mut self.q.own);
        pending.clear();
        let mut x = v;
        while self.q.heap_stamp[x as usize] != query {
            pending.push(x);
            let t = self.q.tree_edge[x as usize];
            if t == NONE {
                break;
            }
            x = self.edge_head[t as usize];
        }
        for &x in pending.iter().rev() {
            own.clear();
            let tree = self.q.tree_edge[x as usize];
            let dx = self.q.distance[x as usize];
            for i in self.out_start[x as usize]..self.out_start[x as usize + 1] {
                let e = self.out_edges[i as usize];
                let head = self.edge_head[e as usize] as usize;
                let w = self.edge_weight[e as usize];
                if e != tree && w >= 0 && self.q.stamp[head] == query {
                    let id = self.q.nodes.len() as u32;
                    self.q.nodes.push(HeapNode {
                        edge: e,
                        key: w + self.q.distance[head] - dx,
                        rank: 1,
                        left: NONE,
                        right: NONE,
                    });
                    own.push(id);
                }
            }
            let next = if tree == NONE {
                NONE
            } else {
                self.q.heaps[self.edge_head[tree as usize] as usize]
            };
            let nodes = &mut self.q.nodes;
            own.sort_by_key(|&id| nodes[id as usize].key); // stable
            let mut root = NONE;
            for &id in own.iter().rev() {
                nodes[id as usize].left = root;
                root = id;
            }
            let merged = merge_heaps(nodes, next, root);
            self.q.heaps[x as usize] = merged;
            self.q.heap_stamp[x as usize] = query;
        }
        self.q.pending = pending;
        self.q.own = own;
        self.q.heaps[v as usize]
    }

    /// Full path of a candidate: along the tree, crossing its sidetracks in order.
    fn build_path(&self, c: u32) -> Path {
        let q = &self.q;
        let mut sidetracks = Vec::new();
        let mut x = c;
        while x != NONE && q.candidates[x as usize].heap != NONE {
            sidetracks.push(q.nodes[q.candidates[x as usize].heap as usize].edge);
            x = q.candidates[x as usize].parent;
        }
        sidetracks.reverse();
        let mut edges = Vec::new();
        let mut weight = 0;
        let mut v = q.source as u32;
        let mut next = 0;
        loop {
            let e = if next < sidetracks.len() && self.edge_tail[sidetracks[next] as usize] == v {
                next += 1;
                sidetracks[next - 1]
            } else if q.tree_edge[v as usize] != NONE && v as usize != q.target {
                q.tree_edge[v as usize]
            } else {
                assert!(next == sidetracks.len(), "sidetrack not on the path (internal error)");
                return Path { edges, weight };
            };
            edges.push(e);
            weight += self.edge_weight[e as usize];
            v = self.edge_head[e as usize];
        }
    }
}

/// Merges two heaps persistently (copies only the nodes on the merge path, O(log n)).
fn merge_heaps(nodes: &mut Vec<HeapNode>, a: u32, b: u32) -> u32 {
    if a == NONE {
        return b;
    }
    if b == NONE {
        return a;
    }
    let (a, b) = if nodes[b as usize].key < nodes[a as usize].key {
        (b, a)
    } else {
        (a, b)
    };
    let mut result = nodes[a as usize];
    result.right = merge_heaps(nodes, result.right, b);
    let rank = |nodes: &Vec<HeapNode>, h: u32| if h == NONE { 0 } else { nodes[h as usize].rank };
    if rank(nodes, result.left) < rank(nodes, result.right) {
        std::mem::swap(&mut result.left, &mut result.right);
    }
    result.rank = rank(nodes, result.right) + 1;
    let id = nodes.len() as u32;
    nodes.push(result);
    id
}
