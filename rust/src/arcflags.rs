//! Arc flags: optional speed-up of the shortest-path tree for many queries. Every edge gets one
//! flag per region, set if it lies on a shortest path to some vertex of the region; a query only
//! relaxes edges flagged for the goal's region, so all distances stay exact.

use std::io::{BufRead, Write};

use crate::import::ImportError;
use crate::pqueue::PriorityQueue;
use crate::Graph;

#[derive(Default)]
pub(crate) struct ArcFlagsState {
    pub(crate) enabled: bool,
    pub(crate) valid: bool,
    pub(crate) region_count: usize,
    auto_region_count: usize,
    manual_regions: String,
    file_partition: Option<Vec<u32>>,
    pub(crate) vertex_region: Vec<u32>,
    flags: Vec<bool>, // flag of edge e for region r: flags[e * region_count + r]
}

impl Graph {
    /// True if arc flags are enabled.
    pub fn arc_flags_enabled(&self) -> bool {
        self.arc.enabled
    }

    /// Number of regions (0 if disabled); runs pending preprocessing.
    pub fn arc_flags_region_count(&mut self) -> usize {
        self.ensure_arc_flags();
        if self.arc.enabled {
            self.arc.region_count
        } else {
            0
        }
    }

    /// Enables arc flags with an automatic partition into `region_count` regions (chunks of a
    /// breadth-first order). Returns false if `region_count` is 0.
    pub fn enable_arc_flags(&mut self, region_count: usize) -> bool {
        if region_count < 1 {
            return false;
        }
        self.arc = ArcFlagsState {
            enabled: true,
            auto_region_count: region_count,
            ..Default::default()
        };
        self.q.ready = false;
        true
    }

    /// Enables arc flags with a given partition: semicolon-separated regions of comma-separated
    /// labels ("S,A,B;C,D"). Unlisted vertices form one more region. False if a label does not
    /// exist or is repeated.
    pub fn enable_arc_flags_regions(&mut self, regions: &str) -> bool {
        if regions.is_empty() || self.parse_partition(regions).is_none() {
            return false;
        }
        self.arc = ArcFlagsState {
            enabled: true,
            manual_regions: regions.to_string(),
            ..Default::default()
        };
        self.q.ready = false;
        true
    }

    /// Enables arc flags with a partition from an external partitioner (mt-KaHyPar, KaHIP,
    /// METIS): one region number per line, in vertex order; empty lines and lines starting with
    /// '%' or '#' are skipped. Arc flags stay unchanged on an error.
    pub fn enable_arc_flags_from_partition<R: BufRead>(&mut self, reader: R) -> Result<(), ImportError> {
        let n = self.labels.len();
        let mut partition = Vec::with_capacity(n);
        for (i, line) in reader.lines().enumerate() {
            let line_number = i + 1;
            let line = line.map_err(|e| ImportError::new(line_number, e.to_string()))?;
            let line = line.trim();
            if line.is_empty() || line.starts_with('%') || line.starts_with('#') {
                continue;
            }
            let region = line
                .parse::<u32>()
                .ok()
                .filter(|&r| r < i32::MAX as u32 && (r as usize) < n.max(1));
            let Some(region) = region else {
                return Err(ImportError::new(
                    line_number,
                    format!(
                        "expected a region number between 0 and {}, found {:?}",
                        n as i64 - 1,
                        line
                    ),
                ));
            };
            if partition.len() == n {
                return Err(ImportError::new(
                    line_number,
                    format!("more lines than the {n} vertices of the graph"),
                ));
            }
            partition.push(region);
        }
        if partition.len() != n {
            return Err(ImportError::new(
                0,
                format!(
                    "the partition has {} regions, the graph has {} vertices",
                    partition.len(),
                    n
                ),
            ));
        }
        self.arc = ArcFlagsState {
            enabled: true,
            file_partition: Some(partition),
            ..Default::default()
        };
        self.q.ready = false;
        Ok(())
    }

    /// Writes the graph in METIS format (undirected, without self-loops, parallel and negative
    /// edges), the input of graph partitioners.
    pub fn write_metis_graph<W: Write>(&self, writer: W) -> std::io::Result<()> {
        let n = self.labels.len();
        let mut neighbors: Vec<Vec<u32>> = vec![Vec::new(); n];
        for e in 0..self.edge_tail.len() {
            let (a, b) = (self.edge_tail[e], self.edge_head[e]);
            if a != b && self.edge_weight[e] >= 0 {
                neighbors[a as usize].push(b);
                neighbors[b as usize].push(a);
            }
        }
        let mut undirected = 0;
        for list in &mut neighbors {
            list.sort_unstable();
            list.dedup();
            undirected += list.len();
        }
        let mut out = std::io::BufWriter::new(writer);
        writeln!(out, "{} {}", n, undirected / 2)?;
        for list in &neighbors {
            let line: Vec<String> = list.iter().map(|u| (u + 1).to_string()).collect();
            writeln!(out, "{}", line.join(" "))?;
        }
        out.flush()
    }

    /// Disables arc flags and releases their memory.
    pub fn disable_arc_flags(&mut self) {
        self.arc = ArcFlagsState::default();
        self.q.ready = false;
    }

    /// Runs the preprocessing now; false if arc flags are disabled.
    pub fn precompute_arc_flags(&mut self) -> bool {
        self.ensure_arc_flags();
        self.arc.enabled && self.arc.valid
    }

    /// Flag of an edge for a region (true if flags are not computed or the region is unknown).
    pub fn has_arc_flag(&self, edge: usize, region: usize) -> bool {
        self.has_arc_flag_internal(edge, region as i64)
    }

    /// Region of a vertex, None if arc flags are not computed.
    pub fn region_of(&self, vertex: usize) -> Option<usize> {
        (self.arc.valid && vertex < self.arc.vertex_region.len()).then(|| self.arc.vertex_region[vertex] as usize)
    }

    #[inline]
    pub(crate) fn has_arc_flag_internal(&self, edge: usize, region: i64) -> bool {
        let a = &self.arc;
        region < 0 || !a.valid || region as usize >= a.region_count || a.flags[edge * a.region_count + region as usize]
    }

    /// Region used to prune edges for a goal, -1 if arc flags are not used.
    pub(crate) fn arc_flags_region_of(&self, v: usize) -> i64 {
        if !self.arc.enabled {
            return -1;
        }
        self.region_of(v).map_or(-1, |r| r as i64)
    }

    pub(crate) fn ensure_arc_flags(&mut self) {
        if !self.arc.enabled || self.arc.valid {
            return;
        }
        self.ensure_adjacency();
        let partition = if let Some(p) = &self.arc.file_partition {
            (p.len() == self.labels.len()).then(|| p.clone())
        } else if !self.arc.manual_regions.is_empty() {
            let regions = self.arc.manual_regions.clone();
            self.parse_partition(&regions)
        } else {
            Some(self.automatic_partition(self.arc.auto_region_count))
        };
        let Some(partition) = partition else {
            self.disable_arc_flags(); // partition no longer valid (vertices were recreated)
            return;
        };
        self.arc.region_count = partition.iter().map(|&r| r as usize + 1).max().unwrap_or(0);
        self.arc.vertex_region = partition;
        self.compute_arc_flags();
        self.arc.valid = true;
    }

    fn parse_partition(&self, regions: &str) -> Option<Vec<u32>> {
        let mut result = vec![u32::MAX; self.labels.len()];
        let mut region = 0u32;
        for group in regions.split(';') {
            let mut any = false;
            for label in group.split(',') {
                let label = label.trim();
                if label.is_empty() {
                    continue;
                }
                let v = self.vertex_index(label)?;
                if result[v] != u32::MAX {
                    return None;
                }
                result[v] = region;
                any = true;
            }
            if any {
                region += 1;
            }
        }
        for r in &mut result {
            if *r == u32::MAX {
                *r = region;
            }
        }
        Some(result)
    }

    /// Connected chunks of similar size of a breadth-first order of the undirected graph.
    fn automatic_partition(&self, count: usize) -> Vec<u32> {
        let n = self.labels.len();
        let mut result = vec![0u32; n];
        if n == 0 {
            return result;
        }
        let count = count.clamp(1, n);
        let chunk = n.div_ceil(count);
        let mut visited = vec![false; n];
        let mut queue = std::collections::VecDeque::new();
        let mut index = 0;
        for start in 0..n {
            if visited[start] {
                continue;
            }
            visited[start] = true;
            queue.push_back(start as u32);
            while let Some(v) = queue.pop_front() {
                result[v as usize] = (index / chunk) as u32;
                index += 1;
                let v = v as usize;
                for i in self.out_start[v]..self.out_start[v + 1] {
                    let w = self.edge_head[self.out_edges[i as usize] as usize];
                    if !visited[w as usize] {
                        visited[w as usize] = true;
                        queue.push_back(w);
                    }
                }
                for i in self.in_start[v]..self.in_start[v + 1] {
                    let w = self.edge_tail[self.in_edges[i as usize] as usize];
                    if !visited[w as usize] {
                        visited[w as usize] = true;
                        queue.push_back(w);
                    }
                }
            }
        }
        result
    }

    /// Edges inside a region get its flag; for every boundary vertex b, a backward Dijkstra runs
    /// and every tight edge between reached vertices gets b's region flag.
    fn compute_arc_flags(&mut self) {
        let n = self.labels.len();
        let regions = self.arc.region_count;
        let mut flags = vec![false; self.edge_tail.len() * regions];
        let mut boundary = Vec::new();
        let mut is_boundary = vec![false; n];
        let region = &self.arc.vertex_region;
        for e in 0..self.edge_tail.len() {
            if self.edge_weight[e] < 0 {
                continue;
            }
            let (tr, hr) = (region[self.edge_tail[e] as usize], region[self.edge_head[e] as usize]);
            let head = self.edge_head[e] as usize;
            if tr == hr {
                flags[e * regions + hr as usize] = true;
            } else if !is_boundary[head] {
                is_boundary[head] = true;
                boundary.push(head);
            }
        }
        let mut dist = vec![0i64; n];
        let mut seen = vec![0u32; n];
        let mut reached = Vec::new();
        let mut fringe = PriorityQueue::new();
        for (search, &b) in boundary.iter().enumerate() {
            let mark = search as u32 + 1;
            reached.clear();
            fringe.clear();
            fringe.push(b as u32, 0);
            while let Some((v, d)) = fringe.pop() {
                let v = v as usize;
                if seen[v] == mark {
                    continue;
                }
                seen[v] = mark;
                dist[v] = d;
                reached.push(v);
                for i in self.in_start[v]..self.in_start[v + 1] {
                    let e = self.in_edges[i as usize] as usize;
                    let tail = self.edge_tail[e];
                    if self.edge_weight[e] >= 0 && seen[tail as usize] != mark {
                        fringe.push(tail, d + self.edge_weight[e]);
                    }
                }
            }
            let r = region[b] as usize;
            for &v in &reached {
                for i in self.out_start[v]..self.out_start[v + 1] {
                    let e = self.out_edges[i as usize] as usize;
                    let head = self.edge_head[e] as usize;
                    if self.edge_weight[e] >= 0 && seen[head] == mark && dist[v] == self.edge_weight[e] + dist[head] {
                        flags[e * regions + r] = true;
                    }
                }
            }
        }
        self.arc.flags = flags;
    }
}
