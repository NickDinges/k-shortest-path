//! k shortest simple paths: Yen's algorithm with Lawler's improvement, A* spur searches guided
//! by the exact distances to the goal, and the detour corridor.

use std::collections::{HashMap, HashSet};

use crate::paths::Path;
use crate::pqueue::PriorityQueue;
use crate::{saturating_add, Graph, NONE};

struct SimpleCandidate {
    edges: Vec<u32>,
    weight: i64,
    deviation: usize,
}

/// State of a query in simple mode. The trie of found paths is an arena of nodes; the children
/// of a root are the edges to block at its end.
#[derive(Default)]
pub(crate) struct SimpleState {
    pub(crate) enabled: bool,
    candidates: Vec<SimpleCandidate>,
    queue: Option<PriorityQueue<u32>>,
    seen: HashSet<Vec<u32>>,
    trie: Vec<HashMap<u32, u32>>,
    pending: Option<u32>,
    max_weight: i64,
    pub(crate) spur_searches: u64,

    // A* state, valid where the stamp matches the current search
    spur_stamp: u32,
    open_stamp: Vec<u32>,
    closed_stamp: Vec<u32>,
    cost: Vec<i64>,
    parent: Vec<u32>,
    path_stamp: u32,
    on_path_stamp: Vec<u32>,
    on_path_position: Vec<u32>,
    open: Option<PriorityQueue<u32>>,
}

impl SimpleState {
    fn child(&mut self, node: u32, e: u32) -> u32 {
        if let Some(&c) = self.trie[node as usize].get(&e) {
            return c;
        }
        let c = self.trie.len() as u32;
        self.trie.push(HashMap::new());
        self.trie[node as usize].insert(e, c);
        c
    }
}

impl Graph {
    /// Only simple paths (no vertex twice): Yen's algorithm with Lawler's improvement instead of
    /// Eppstein. Requires a new query.
    pub fn set_simple_paths_only(&mut self, simple: bool) {
        self.simple.enabled = simple;
        self.settings_changed();
    }

    /// True if only simple paths are returned.
    pub fn simple_paths_only(&self) -> bool {
        self.simple.enabled
    }

    /// Spur searches since the last query started (for tests and measurements).
    pub fn spur_searches(&self) -> u64 {
        self.simple.spur_searches
    }

    pub(crate) fn start_simple_paths(&mut self, any_path: bool) {
        let n = self.labels.len();
        let s = &mut self.simple;
        s.candidates.clear();
        s.queue = Some(PriorityQueue::new());
        s.seen.clear();
        s.trie.clear();
        s.trie.push(HashMap::new());
        s.pending = None;
        s.spur_searches = 0;
        if s.open.is_none() {
            s.open = Some(PriorityQueue::new());
        }
        if !any_path {
            return;
        }
        if s.open_stamp.len() < n {
            s.open_stamp = vec![0; n];
            s.closed_stamp = vec![0; n];
            s.cost = vec![0; n];
            s.parent = vec![NONE; n];
            s.on_path_stamp = vec![0; n];
            s.on_path_position = vec![0; n];
            s.spur_stamp = 0;
            s.path_stamp = 0;
        }
        let mut edges = Vec::new();
        let mut v = self.q.source;
        while v != self.q.target {
            let e = self.q.tree_edge[v];
            edges.push(e);
            v = self.edge_head[e as usize] as usize;
        }
        let d = self.q.distance[self.q.source];
        self.simple.max_weight = saturating_add(d, self.q.path_bound);
        self.add_simple_candidate(edges, d, 0);
    }

    pub(crate) fn next_simple_path(&mut self) -> Option<(Path, i64)> {
        if !self.q.ready {
            return None;
        }
        if let Some(p) = self.simple.pending.take() {
            self.generate_spurs(p);
        }
        let (next, _) = self.simple.queue.as_mut()?.pop()?;
        let s = &mut self.simple;
        let mut node = 0;
        for i in 0..s.candidates[next as usize].edges.len() {
            let e = s.candidates[next as usize].edges[i];
            node = s.child(node, e);
        }
        s.pending = Some(next);
        let c = &s.candidates[next as usize];
        Some((
            Path {
                edges: c.edges.clone(),
                weight: c.weight,
            },
            c.weight,
        ))
    }

    /// Yen with Lawler: spurs only from the deviation vertex on.
    fn generate_spurs(&mut self, path: u32) {
        let edges = std::mem::take(&mut self.simple.candidates[path as usize].edges);
        let deviation = self.simple.candidates[path as usize].deviation;
        let s = &mut self.simple;
        s.path_stamp += 1;
        if s.path_stamp == u32::MAX {
            s.on_path_stamp.fill(0);
            s.path_stamp = 1;
        }
        let mut v = self.q.source as u32;
        for i in 0..=edges.len() {
            s.on_path_stamp[v as usize] = s.path_stamp;
            s.on_path_position[v as usize] = i as u32;
            if i == edges.len() {
                break;
            }
            v = self.edge_head[edges[i] as usize];
        }

        let mut node = 0u32;
        let mut root_weight = 0i64;
        let mut v = self.q.source as u32;
        for (i, &e) in edges.iter().enumerate() {
            if i >= deviation {
                let max_cost = self.simple.max_weight - root_weight;
                if let Some((spur, spur_weight)) = self.spur_search(v, i as u32, node, max_cost) {
                    let mut candidate = Vec::with_capacity(i + spur.len());
                    candidate.extend_from_slice(&edges[..i]);
                    candidate.extend_from_slice(&spur);
                    self.add_simple_candidate(candidate, root_weight + spur_weight, i);
                }
            }
            root_weight += self.edge_weight[e as usize];
            node = self.simple.child(node, e);
            v = self.edge_head[e as usize];
        }
        self.simple.candidates[path as usize].edges = edges;
    }

    fn add_simple_candidate(&mut self, edges: Vec<u32>, weight: i64, deviation: usize) {
        let s = &mut self.simple;
        if !s.seen.insert(edges.clone()) {
            return; // same path reached from another root
        }
        let id = s.candidates.len() as u32;
        s.candidates.push(SimpleCandidate {
            edges,
            weight,
            deviation,
        });
        s.queue.as_mut().unwrap().push(id, weight);
    }

    /// A* from the spur vertex to the goal with the root's vertices and the trie children's edges
    /// blocked, guided by the exact distances to the goal.
    fn spur_search(&mut self, spur_vertex: u32, position: u32, root: u32, max_cost: i64) -> Option<(Vec<u32>, i64)> {
        let query = self.q.query_stamp;
        let sv = spur_vertex as usize;
        if max_cost < 0 || self.q.stamp[sv] != query || self.q.distance[sv] > max_cost {
            return None;
        }
        let s = &mut self.simple;
        s.spur_searches += 1;
        s.spur_stamp += 1;
        if s.spur_stamp == u32::MAX {
            s.open_stamp.fill(0);
            s.closed_stamp.fill(0);
            s.spur_stamp = 1;
        }
        let search = s.spur_stamp;
        let mut open = s.open.take().unwrap();
        open.clear();
        s.cost[sv] = 0;
        s.parent[sv] = NONE;
        s.open_stamp[sv] = search;
        open.push(spur_vertex, self.q.distance[sv]);
        let result = loop {
            let Some((v, estimate)) = open.pop() else { break None };
            let s = &mut self.simple;
            if s.closed_stamp[v as usize] == search {
                continue;
            }
            if estimate > max_cost {
                break None;
            }
            s.closed_stamp[v as usize] = search;
            if v as usize == self.q.target {
                let mut spur = Vec::new();
                let mut x = v;
                while s.parent[x as usize] != NONE {
                    spur.push(s.parent[x as usize]);
                    x = self.edge_tail[s.parent[x as usize] as usize];
                }
                spur.reverse();
                break Some((spur, s.cost[v as usize]));
            }
            for i in self.out_start[v as usize]..self.out_start[v as usize + 1] {
                let e = self.out_edges[i as usize];
                let head = self.edge_head[e as usize] as usize;
                let w = self.edge_weight[e as usize];
                if w < 0 || self.q.stamp[head] != query || s.closed_stamp[head] == search {
                    continue;
                }
                if v == spur_vertex && s.trie[root as usize].contains_key(&e) {
                    continue; // a found path with the same root leaves along this edge
                }
                if s.on_path_stamp[head] == s.path_stamp && s.on_path_position[head] < position {
                    continue; // vertex of the root: the path would not be simple
                }
                let new_cost = s.cost[v as usize] + w;
                if s.open_stamp[head] == search && s.cost[head] <= new_cost {
                    continue;
                }
                s.open_stamp[head] = search;
                s.cost[head] = new_cost;
                s.parent[head] = e;
                open.push(head as u32, new_cost + self.q.distance[head]);
            }
        };
        self.simple.open = Some(open);
        result
    }
}
