package eppstein

import "strings"

// Arc flags are an optional speed-up of the shortest path tree computation for many queries.
//
// The vertices are partitioned into regions. During preprocessing every edge (u,v) gets one flag
// per region r, set if the edge lies on a shortest path from u to some vertex of r. The search
// towards a goal then only uses edges flagged for the goal's region. Every shortest path u->goal
// only uses such edges, so all distances (and therefore all results) stay exact. Flags are
// recomputed lazily on the next query after the graph has been changed.

// arcFlagsState holds the arc-flags configuration and data of a graph.
type arcFlagsState struct {
	arcFlagsEnabled bool
	arcFlagsValid   bool
	regionCount     int
	autoRegionCount int
	manualRegions   string
	vertexRegion    []int32
	arcFlags        []bool // flag of edge e for region r: arcFlags[e*regionCount+r]
}

// ArcFlagsEnabled tells if the arc-flags optimization is enabled.
func (g *Graph) ArcFlagsEnabled() bool { return g.arcFlagsEnabled }

// ArcFlagsRegionCount returns the number of regions, 0 if disabled. It runs pending
// preprocessing if needed.
func (g *Graph) ArcFlagsRegionCount() int {
	g.ensureArcFlags()
	if !g.arcFlagsEnabled {
		return 0
	}
	return g.regionCount
}

// EnableArcFlags enables arc flags with an automatic partition of the vertices into regionCount
// regions (connected chunks of a breadth-first ordering). It returns false if regionCount is less
// than 1.
func (g *Graph) EnableArcFlags(regionCount int) bool {
	if regionCount < 1 {
		return false
	}
	g.autoRegionCount, g.manualRegions, g.arcFlagsEnabled = regionCount, "", true
	g.arcFlagsValid, g.ready = false, false
	return true
}

// EnableArcFlagsRegions enables arc flags with a given partition: semicolon-separated regions,
// each a comma-separated list of labels, e.g. "S,A,B,C;D,E,F". Vertices not listed form one
// additional region. It returns false if a label does not exist or is listed twice.
func (g *Graph) EnableArcFlagsRegions(regions string) bool {
	if regions == "" || g.parsePartition(regions) == nil {
		return false
	}
	g.manualRegions, g.autoRegionCount, g.arcFlagsEnabled = regions, 0, true
	g.arcFlagsValid, g.ready = false, false
	return true
}

// DisableArcFlags disables arc flags and releases their memory.
func (g *Graph) DisableArcFlags() {
	g.arcFlagsState = arcFlagsState{}
	g.ready = false
}

// PrecomputeArcFlags runs the arc-flags preprocessing now (otherwise it runs on the next query).
// It returns false if arc flags are disabled.
func (g *Graph) PrecomputeArcFlags() bool {
	g.ensureArcFlags()
	return g.arcFlagsEnabled && g.arcFlagsValid
}

func (g *Graph) hasArcFlag(edge, region int) bool {
	return region < 0 || !g.arcFlagsValid || region >= g.regionCount || g.arcFlags[edge*g.regionCount+region]
}

func (g *Graph) regionOf(v int) int {
	if g.arcFlagsValid && v < len(g.vertexRegion) {
		return int(g.vertexRegion[v])
	}
	return -1
}

// arcFlagsRegionOf returns the region used to prune edges for a goal, -1 if arc flags are not
// used.
func (g *Graph) arcFlagsRegionOf(v int) int {
	if !g.arcFlagsEnabled {
		return -1
	}
	return g.regionOf(v)
}

// ensureArcFlags recomputes partition and flags if enabled and outdated.
func (g *Graph) ensureArcFlags() {
	if !g.arcFlagsEnabled || g.arcFlagsValid {
		return
	}
	g.ensureAdjacency()
	var partition []int32
	if g.manualRegions != "" {
		partition = g.parsePartition(g.manualRegions)
	} else {
		partition = g.automaticPartition(g.autoRegionCount)
	}
	if partition == nil {
		g.DisableArcFlags() // partition no longer valid (vertices were recreated)
		return
	}
	g.vertexRegion = partition
	g.regionCount = 0
	for _, r := range partition {
		g.regionCount = max(g.regionCount, int(r)+1)
	}
	g.computeArcFlags()
	g.arcFlagsValid = true
}

// parsePartition returns the region of every vertex from a partition string, nil if a label
// does not exist or is repeated.
func (g *Graph) parsePartition(regions string) []int32 {
	result := make([]int32, len(g.labels))
	for i := range result {
		result[i] = -1
	}
	region := int32(0)
	for _, group := range strings.Split(regions, ";") {
		any := false
		for _, label := range strings.Split(group, ",") {
			label = strings.TrimSpace(label)
			if label == "" {
				continue
			}
			v := g.VertexIndex(label)
			if v < 0 || result[v] >= 0 {
				return nil
			}
			result[v] = region
			any = true
		}
		if any {
			region++
		}
	}
	for v := range result {
		if result[v] < 0 {
			result[v] = region // vertices not listed: one additional region
		}
	}
	return result
}

// automaticPartition splits the vertices into connected chunks of similar size, using a
// breadth-first ordering over the underlying undirected graph.
func (g *Graph) automaticPartition(count int) []int32 {
	n := len(g.labels)
	result := make([]int32, n)
	if n == 0 {
		return result
	}
	count = min(max(count, 1), n)
	chunk := (n + count - 1) / count

	visited := make([]bool, n)
	var queue []int32
	index := 0
	visit := func(w int32) {
		if !visited[w] {
			visited[w] = true
			queue = append(queue, w)
		}
	}
	for start := range n {
		if visited[start] {
			continue
		}
		visit(int32(start))
		for len(queue) > 0 {
			v := queue[0]
			queue = queue[1:]
			result[v] = int32(index / chunk)
			index++
			for _, e := range g.outEdges[g.outStart[v]:g.outStart[v+1]] {
				visit(g.edgeHead[e])
			}
			for _, e := range g.inEdges[g.inStart[v]:g.inStart[v+1]] {
				visit(g.edgeTail[e])
			}
		}
	}
	return result
}

// computeArcFlags computes the flags: edges inside a region get that region's flag. For every
// boundary vertex b of region r (a vertex with an incoming edge from another region), a backward
// Dijkstra to b runs, and every tight edge between reached vertices (d(u,b) = w(u,v) + d(v,b))
// gets flag r. Only the edges of reached vertices are checked. Negative edges are ignored.
func (g *Graph) computeArcFlags() {
	n, regions := len(g.labels), g.regionCount
	g.arcFlags = make([]bool, len(g.edgeTail)*regions)
	var boundary []int32
	isBoundary := make([]bool, n)
	for e := range g.edgeTail {
		if g.edgeWeight[e] < 0 {
			continue
		}
		tailRegion, headRegion := g.vertexRegion[g.edgeTail[e]], g.vertexRegion[g.edgeHead[e]]
		if tailRegion == headRegion {
			g.arcFlags[e*regions+int(headRegion)] = true
		} else if head := g.edgeHead[e]; !isBoundary[head] {
			isBoundary[head] = true
			boundary = append(boundary, head)
		}
	}

	dist := make([]int64, n)
	seen := make([]int32, n) // search number that reached the vertex (0: none)
	var reached []int32
	for search, b := range boundary {
		mark := int32(search + 1)
		reached = reached[:0]
		fringe := newPriorityQueue[int32](64)
		fringe.Enqueue(b, 0)
		for {
			v, d, ok := fringe.Dequeue()
			if !ok {
				break
			}
			if seen[v] == mark {
				continue
			}
			seen[v], dist[v] = mark, d
			reached = append(reached, v)
			for _, e := range g.inEdges[g.inStart[v]:g.inStart[v+1]] {
				if g.edgeWeight[e] >= 0 && seen[g.edgeTail[e]] != mark {
					fringe.Enqueue(g.edgeTail[e], d+g.edgeWeight[e])
				}
			}
		}
		region := int(g.vertexRegion[b])
		for _, v := range reached {
			for _, e := range g.outEdges[g.outStart[v]:g.outStart[v+1]] {
				head := g.edgeHead[e]
				if g.edgeWeight[e] >= 0 && seen[head] == mark && dist[v] == g.edgeWeight[e]+dist[head] {
					g.arcFlags[int(e)*regions+region] = true
				}
			}
		}
	}
}
