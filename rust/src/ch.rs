//! Contraction hierarchy (CH) with PHAST: optional speed-up of the shortest-path tree.
//!
//! Preprocessing contracts the vertices from unimportant to important and adds shortcuts where a
//! shortest connection ran through a contracted vertex (checked by local witness searches). PHAST
//! then computes all distances to the goal with a small upward search from the goal plus one
//! linear sweep in descending rank. The tree is rebuilt from tight original edges.

use crate::pqueue::PriorityQueue;
use crate::{saturating_add, Graph, NONE};

const WITNESS_LIMIT_ORDERING: usize = 50;
const WITNESS_LIMIT_CONTRACTION: usize = 500;

#[derive(Default)]
pub(crate) struct ChState {
    pub(crate) enabled: bool,
    pub(crate) valid: bool,
    shortcut_count: usize,
    position: Vec<u32>,  // vertex -> sweep position (n - 1 - rank)
    up_start: Vec<u32>,  // edges to higher-ranked vertices, by position
    up_target: Vec<u32>, // ... as sweep position of the head
    up_weight: Vec<i64>,
    down_start: Vec<u32>,  // edges from higher-ranked vertices, by vertex
    down_source: Vec<u32>, // ... their tails
    down_weight: Vec<i64>,
    distance: Vec<i64>, // by sweep position, reused per query
    bfs: Vec<u32>,
}

#[derive(Clone, Copy)]
struct ChEdge {
    other: u32,
    weight: i64,
}

impl Graph {
    /// True if the contraction hierarchy is enabled.
    pub fn contraction_hierarchy_enabled(&self) -> bool {
        self.ch.enabled
    }

    /// Number of shortcuts (0 if not computed).
    pub fn contraction_hierarchy_shortcuts(&self) -> usize {
        if self.ch.valid {
            self.ch.shortcut_count
        } else {
            0
        }
    }

    /// Enables the contraction hierarchy: queries without a corridor compute the shortest-path
    /// tree with PHAST. Preprocessing runs on the next query and after every change.
    pub fn enable_contraction_hierarchy(&mut self) {
        self.ch.enabled = true;
        self.q.ready = false;
    }

    /// Disables the contraction hierarchy and releases its memory.
    pub fn disable_contraction_hierarchy(&mut self) {
        self.ch = ChState::default();
        self.q.ready = false;
    }

    /// Runs the contraction now; false if disabled.
    pub fn precompute_contraction_hierarchy(&mut self) -> bool {
        self.ensure_contraction_hierarchy();
        self.ch.enabled && self.ch.valid
    }

    pub(crate) fn use_phast(&self) -> bool {
        self.ch.enabled && !self.has_corridor()
    }

    pub(crate) fn ensure_contraction_hierarchy(&mut self) {
        if !self.ch.enabled || self.ch.valid {
            return;
        }
        self.contract();
        self.ch.valid = true;
    }

    fn contract(&mut self) {
        let n = self.labels.len();
        let mut state = Contraction::new(self, n);
        let mut queue = PriorityQueue::new();
        let key = |priority: i64, v: u32| priority * n as i64 + v as i64; // by priority, then vertex
        for v in 0..n as u32 {
            let p = state.priority(v);
            queue.push(v, key(p, v));
        }
        let mut rank = vec![0u32; n];
        let mut up: Vec<Vec<ChEdge>> = vec![Vec::new(); n];
        let mut down: Vec<Vec<ChEdge>> = vec![Vec::new(); n];
        let mut next = 0u32;
        let mut shortcuts = Vec::new();
        let mut shortcut_count = 0;
        while let Some((v, _)) = queue.pop() {
            if state.contracted[v as usize] {
                continue;
            }
            let k = key(state.priority(v), v); // lazy update
            if queue.peek_weight().is_some_and(|top| k > top) {
                queue.push(v, k);
                continue;
            }
            shortcuts.clear();
            state.find_shortcuts(v, WITNESS_LIMIT_CONTRACTION, &mut shortcuts);
            for &(tail, head, weight) in &shortcuts {
                state.add_edge(tail, head, weight);
            }
            shortcut_count += shortcuts.len();
            up[v as usize] = state.live(&state.out[v as usize]);
            down[v as usize] = state.live(&state.inn[v as usize]);
            state.contracted[v as usize] = true;
            rank[v as usize] = next;
            next += 1;
            for e in &up[v as usize] {
                state.contracted_neighbors[e.other as usize] += 1;
            }
            for e in &down[v as usize] {
                state.contracted_neighbors[e.other as usize] += 1;
            }
        }

        let ch = &mut self.ch;
        ch.shortcut_count = shortcut_count;
        ch.position = (0..n).map(|v| (n - 1) as u32 - rank[v]).collect();
        let mut vertex_at = vec![0u32; n];
        for v in 0..n {
            vertex_at[ch.position[v] as usize] = v as u32;
        }
        ch.up_start = vec![0; n + 1];
        ch.up_target.clear();
        ch.up_weight.clear();
        for p in 0..n {
            for e in &up[vertex_at[p] as usize] {
                ch.up_target.push(ch.position[e.other as usize]);
                ch.up_weight.push(e.weight);
            }
            ch.up_start[p + 1] = ch.up_target.len() as u32;
        }
        ch.down_start = vec![0; n + 1];
        ch.down_source.clear();
        ch.down_weight.clear();
        for (v, list) in down.iter().enumerate() {
            for e in list {
                ch.down_source.push(e.other);
                ch.down_weight.push(e.weight);
            }
            ch.down_start[v + 1] = ch.down_source.len() as u32;
        }
        ch.distance = vec![0; n];
    }

    /// PHAST towards the goal, then the tree from tight original edges (breadth-first).
    pub(crate) fn build_shortest_path_tree_phast(&mut self) {
        let n = self.labels.len();
        let query = self.q.query_stamp;
        let target = self.q.target;
        let ch = &mut self.ch;
        let d = &mut ch.distance;
        d.fill(i64::MAX);

        // 1. upward search from the goal over reversed downward edges
        let mut fringe = PriorityQueue::new();
        d[ch.position[target] as usize] = 0;
        fringe.push(target as u32, 0);
        while let Some((y, dy)) = fringe.pop() {
            if dy > d[ch.position[y as usize] as usize] {
                continue;
            }
            for i in ch.down_start[y as usize]..ch.down_start[y as usize + 1] {
                let x = ch.down_source[i as usize];
                let nd = saturating_add(dy, ch.down_weight[i as usize]);
                let px = ch.position[x as usize] as usize;
                if nd < d[px] {
                    d[px] = nd;
                    fringe.push(x, nd);
                }
            }
        }

        // 2. sweep: highest rank first
        for p in 0..n {
            let mut best = d[p];
            for i in ch.up_start[p] as usize..ch.up_start[p + 1] as usize {
                let dh = d[ch.up_target[i] as usize];
                if dh != i64::MAX {
                    best = best.min(saturating_add(dh, ch.up_weight[i]));
                }
            }
            d[p] = best;
        }

        // 3. tree from tight original edges, breadth-first from the goal
        let q = &mut self.q;
        q.stamp[target] = query;
        q.distance[target] = 0;
        q.tree_edge[target] = NONE;
        let bfs = &mut ch.bfs;
        bfs.clear();
        bfs.push(target as u32);
        let mut head = 0;
        while head < bfs.len() {
            let y = bfs[head] as usize;
            head += 1;
            let dy = q.distance[y];
            for i in self.in_start[y]..self.in_start[y + 1] {
                let e = self.in_edges[i as usize];
                let x = self.edge_tail[e as usize] as usize;
                let w = self.edge_weight[e as usize];
                if q.stamp[x] == query || w < 0 {
                    continue;
                }
                let dx = d[ch.position[x] as usize];
                if dx != i64::MAX && dx == saturating_add(w, dy) {
                    q.stamp[x] = query;
                    q.distance[x] = dx;
                    q.tree_edge[x] = e;
                    bfs.push(x as u32);
                }
            }
        }
    }
}

/// The graph during contraction: adjacency lists (may contain contracted vertices, skipped) and
/// witness searches.
struct Contraction {
    contracted: Vec<bool>,
    contracted_neighbors: Vec<i64>,
    out: Vec<Vec<ChEdge>>,
    inn: Vec<Vec<ChEdge>>,
    distance: Vec<i64>,
    seen: Vec<u32>,
    search: u32,
    queue: PriorityQueue<u32>,
    scratch: Vec<(u32, u32, i64)>,
}

impl Contraction {
    fn new(g: &Graph, n: usize) -> Self {
        let mut c = Contraction {
            contracted: vec![false; n],
            contracted_neighbors: vec![0; n],
            out: vec![Vec::new(); n],
            inn: vec![Vec::new(); n],
            distance: vec![0; n],
            seen: vec![0; n],
            search: 0,
            queue: PriorityQueue::new(),
            scratch: Vec::new(),
        };
        for e in 0..g.edge_tail.len() {
            if g.edge_weight[e] >= 0 && g.edge_tail[e] != g.edge_head[e] {
                c.add_edge(g.edge_tail[e], g.edge_head[e], g.edge_weight[e]);
            }
        }
        c
    }

    /// Adds an edge, or lowers the weight of an existing parallel edge.
    fn add_edge(&mut self, tail: u32, head: u32, weight: i64) {
        if let Some(e) = self.out[tail as usize].iter_mut().find(|e| e.other == head) {
            if weight < e.weight {
                e.weight = weight;
                for f in self.inn[head as usize].iter_mut().filter(|f| f.other == tail) {
                    f.weight = weight;
                }
            }
            return;
        }
        self.out[tail as usize].push(ChEdge { other: head, weight });
        self.inn[head as usize].push(ChEdge { other: tail, weight });
    }

    fn live(&self, edges: &[ChEdge]) -> Vec<ChEdge> {
        edges
            .iter()
            .copied()
            .filter(|e| !self.contracted[e.other as usize])
            .collect()
    }

    /// Edge difference plus contracted neighbors.
    fn priority(&mut self, v: u32) -> i64 {
        let mut scratch = std::mem::take(&mut self.scratch);
        scratch.clear();
        self.find_shortcuts(v, WITNESS_LIMIT_ORDERING, &mut scratch);
        let live = |list: &Vec<ChEdge>| list.iter().filter(|e| !self.contracted[e.other as usize]).count();
        let removed = (live(&self.out[v as usize]) + live(&self.inn[v as usize])) as i64;
        let result = 2 * scratch.len() as i64 - removed + self.contracted_neighbors[v as usize];
        self.scratch = scratch;
        result
    }

    /// Shortcuts needed when v is contracted (an incomplete witness search adds the shortcut).
    fn find_shortcuts(&mut self, v: u32, settled_limit: usize, result: &mut Vec<(u32, u32, i64)>) {
        for i in 0..self.inn[v as usize].len() {
            let ChEdge {
                other: u,
                weight: in_weight,
            } = self.inn[v as usize][i];
            if self.contracted[u as usize] {
                continue;
            }
            let mut max_total = 0;
            let mut any_target = false;
            for out in &self.out[v as usize] {
                if !self.contracted[out.other as usize] && out.other != u {
                    max_total = max_total.max(saturating_add(in_weight, out.weight));
                    any_target = true;
                }
            }
            if !any_target {
                continue;
            }
            self.witness_search(u, v, max_total, settled_limit);
            for out in &self.out[v as usize] {
                let x = out.other;
                if self.contracted[x as usize] || x == u {
                    continue;
                }
                let via_v = saturating_add(in_weight, out.weight);
                if self.seen[x as usize] != self.search || self.distance[x as usize] > via_v {
                    result.push((u, x, via_v));
                }
            }
        }
    }

    /// Dijkstra from source over live vertices except `avoid`.
    fn witness_search(&mut self, source: u32, avoid: u32, max_distance: i64, settled_limit: usize) {
        self.search += 1;
        if self.search == u32::MAX {
            self.seen.fill(0);
            self.search = 1;
        }
        self.queue.clear();
        self.seen[source as usize] = self.search;
        self.distance[source as usize] = 0;
        self.queue.push(source, 0);
        let mut settled = 0;
        while let Some((v, d)) = self.queue.pop() {
            if d > self.distance[v as usize] {
                continue;
            }
            settled += 1;
            if d > max_distance || settled > settled_limit {
                break;
            }
            for e in &self.out[v as usize] {
                let x = e.other;
                if self.contracted[x as usize] || x == avoid {
                    continue;
                }
                let nd = saturating_add(d, e.weight);
                if self.seen[x as usize] != self.search || nd < self.distance[x as usize] {
                    self.seen[x as usize] = self.search;
                    self.distance[x as usize] = nd;
                    self.queue.push(x, nd);
                }
            }
        }
    }
}
