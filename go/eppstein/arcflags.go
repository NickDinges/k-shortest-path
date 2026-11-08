package eppstein

import "strings"

// Arc flags are an optional speed-up of the shortest path tree computation.
//
// The vertices are partitioned into regions. During preprocessing every arc (u,v) gets
// one flag per region r, set if the arc lies on a shortest path from u to some vertex
// of r. The backward Dijkstra in FindShortestPath then only relaxes arcs whose flag
// for the region of the endpoint t is set.
//
// Every shortest path u->t only uses arcs flagged for region(t), so all distances (and
// sidetrack deltas) stay exact: the k shortest paths are the same as without arc flags.
// Flags are recomputed lazily on the next query after the graph has been modified.

// arcFlagsState holds the arc-flags configuration of a graph.
type arcFlagsState struct {
	enabled         bool   // flags are used for shortest path tree computation
	valid           bool   // flags match current graph (structure, weights, partition)
	regionCount     int    // number of regions in current partition
	autoRegionCount int    // requested regions for automatic partition, 0 if manual
	manualRegions   string // user-defined partition, "" for automatic partition
}

// ArcFlagsEnabled tells if the arc-flags optimization is enabled.
func (g *Graph) ArcFlagsEnabled() bool {
	return g.arcFlags.enabled
}

// ArcFlagsRegionCount returns the number of regions used by arc flags, 0 if disabled.
// It runs pending preprocessing if needed.
func (g *Graph) ArcFlagsRegionCount() int {
	g.ensureArcFlags()
	if !g.arcFlags.enabled {
		return 0
	}
	return g.arcFlags.regionCount
}

// EnableArcFlags enables arc flags with an automatic partition of the vertices into
// regionCount regions (connected chunks of a breadth-first ordering).
// It returns false if regionCount is less than 1.
func (g *Graph) EnableArcFlags(regionCount int) bool {
	if regionCount < 1 {
		return false
	}
	g.arcFlags.autoRegionCount = regionCount
	g.arcFlags.manualRegions = ""
	g.arcFlags.enabled = true
	g.invalidateArcFlags()
	return true
}

// EnableArcFlagsRegions enables arc flags with a user-defined partition: semicolon-separated
// regions, each one a comma-separated list of labels, e.g. "S,A,B,C;D,E,F;G,H,I,J,K,T".
// Vertices not listed are put together into one additional region.
// It returns false if a label does not exist or is assigned twice.
func (g *Graph) EnableArcFlagsRegions(regions string) bool {
	if regions == "" || !g.applyManualPartition(regions) {
		return false
	}
	g.arcFlags.manualRegions = regions
	g.arcFlags.autoRegionCount = 0
	g.arcFlags.enabled = true
	g.invalidateArcFlags()
	return true
}

// DisableArcFlags disables the arc-flags optimization and releases all flags.
func (g *Graph) DisableArcFlags() {
	g.arcFlags.enabled = false
	g.arcFlags.autoRegionCount = 0
	g.arcFlags.manualRegions = ""
	g.clearArcFlagData()
}

// PrecomputeArcFlags runs the arc-flags preprocessing immediately (otherwise it is done
// lazily by the next query). It returns false if arc flags are disabled.
func (g *Graph) PrecomputeArcFlags() bool {
	g.ensureArcFlags()
	return g.arcFlags.enabled && g.arcFlags.valid
}

// resetArcFlags disables arc flags completely, used when all vertices are recreated.
func (g *Graph) resetArcFlags() {
	g.DisableArcFlags()
}

// invalidateArcFlags marks flags as outdated, they are recomputed on next query.
func (g *Graph) invalidateArcFlags() {
	g.arcFlags.valid = false
}

// clearArcFlagData removes regions and flags from all vertices and edges.
func (g *Graph) clearArcFlagData() {
	g.arcFlags.valid = false
	g.arcFlags.regionCount = 0
	for _, v := range g.vertices {
		v.Region = -1
		for _, e := range v.RelatedEdges {
			e.arcFlags = nil
		}
	}
}

// arcFlagsRegionOf returns the region used for pruning, -1 if arc flags are not used.
func (g *Graph) arcFlagsRegionOf(v *Vertex) int {
	if !g.arcFlags.enabled || !g.arcFlags.valid || v == nil {
		return -1
	}
	return v.Region
}

// ensureArcFlags recomputes partition and flags if enabled and outdated.
func (g *Graph) ensureArcFlags() {
	if !g.arcFlags.enabled || g.arcFlags.valid {
		return
	}
	g.clearArcFlagData()
	if g.arcFlags.manualRegions != "" {
		if !g.applyManualPartition(g.arcFlags.manualRegions) {
			g.DisableArcFlags() // partition no longer valid, fall back to plain Dijkstra
			return
		}
	} else {
		g.applyAutomaticPartition(g.arcFlags.autoRegionCount)
	}
	g.computeArcFlags()
	g.arcFlags.valid = true
}

// applyManualPartition assigns regions from a partition string.
// It returns false if a label does not exist or is repeated.
func (g *Graph) applyManualPartition(regions string) bool {
	for _, v := range g.vertices {
		v.Region = -1
	}
	fail := func() bool {
		for _, v := range g.vertices {
			v.Region = -1
		}
		return false
	}

	region := 0
	for _, group := range strings.Split(strings.ToUpper(regions), ";") {
		any := false
		for _, label := range strings.Split(group, ",") {
			label = strings.TrimSpace(label)
			if label == "" {
				continue
			}
			v := g.getVertex(label)
			if v == nil || v.Region >= 0 { // unknown or already assigned
				return fail()
			}
			v.Region = region
			any = true
		}
		if any {
			region++
		}
	}

	// Collects vertices not listed into one additional region
	unassigned := false
	for _, v := range g.vertices {
		if v.Region < 0 {
			v.Region = region
			unassigned = true
		}
	}
	g.arcFlags.regionCount = region
	if unassigned {
		g.arcFlags.regionCount++
	}
	return true
}

// applyAutomaticPartition splits the vertices into connected chunks of similar size,
// using a breadth-first ordering over the underlying undirected graph.
func (g *Graph) applyAutomaticPartition(count int) {
	n := len(g.vertices)
	if n == 0 {
		g.arcFlags.regionCount = 0
		return
	}
	count = min(max(count, 1), n)
	chunk := (n + count - 1) / count

	visited := make(map[*Vertex]bool, n)
	index := 0
	for _, start := range g.vertices {
		if visited[start] {
			continue
		}
		visited[start] = true
		queue := []*Vertex{start}
		for len(queue) > 0 {
			v := queue[0]
			queue = queue[1:]
			v.Region = index / chunk
			index++
			for _, e := range v.RelatedEdges { // incoming and outgoing edges
				w := e.Head
				if e.Head == v {
					w = e.Tail
				}
				if !visited[w] {
					visited[w] = true
					queue = append(queue, w)
				}
			}
		}
	}
	g.arcFlags.regionCount = (n + chunk - 1) / chunk
}

// computeArcFlags computes flags for the current partition: arcs inside a region get that
// region's flag; for every boundary vertex b of region r (a vertex with an incoming arc from
// another region) a backward Dijkstra to b is run and every tight arc
// (d(u,b) = w(u,v) + d(v,b)) gets flag r. Negative arcs are ignored.
func (g *Graph) computeArcFlags() {
	var arcs []*Edge
	var boundary []*Vertex
	isBoundary := make(map[*Vertex]bool)
	for _, v := range g.vertices {
		for _, e := range v.RelatedEdges {
			if e.Tail != v { // each edge is visited once, from its tail
				continue
			}
			arcs = append(arcs, e)
			e.arcFlags = make([]bool, g.arcFlags.regionCount)
			if e.Weight < 0 {
				continue
			}
			if e.Tail.Region == e.Head.Region {
				e.arcFlags[e.Head.Region] = true
			} else if !isBoundary[e.Head] {
				isBoundary[e.Head] = true
				boundary = append(boundary, e.Head)
			}
		}
	}

	for _, b := range boundary {
		dist := g.backwardDistances(b)
		for _, e := range arcs {
			if e.Weight < 0 {
				continue
			}
			dTail, okTail := dist[e.Tail]
			dHead, okHead := dist[e.Head]
			if okTail && okHead && dTail == e.Weight+dHead {
				e.arcFlags[b.Region] = true
			}
		}
	}
}

// backwardDistances computes the distance to target for every vertex that can reach it.
func (g *Graph) backwardDistances(target *Vertex) map[*Vertex]int {
	dist := map[*Vertex]int{target: 0}
	fringe := newPriorityQueue[*Edge](len(g.vertices))
	v := target
	for {
		if v != nil {
			dv := dist[v]
			for _, e := range v.RelatedEdges {
				if _, done := dist[e.Tail]; e.Head == v && e.Weight >= 0 && !done {
					fringe.Enqueue(e, e.Weight+dv)
				}
			}
		}
		e, weight, ok := fringe.Dequeue()
		if !ok {
			break
		}
		v = e.Tail
		if _, done := dist[v]; done {
			v = nil
		} else {
			dist[v] = weight
		}
	}
	return dist
}
