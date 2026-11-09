package eppstein

import (
	"iter"
	"math"
	"slices"
)

// queryState holds the current query. A vertex's entries are valid only if its stamp equals the
// current query stamp, so a new query does not have to reset all vertices (only the vertices it
// reaches are touched).
type queryState struct {
	queryStamp int32
	stamp      []int32
	distance   []int64
	treeEdge   []int32
	heapStamp  []int32
	heaps      []*heapNode

	ready             bool
	source, target    int
	pathBound         int64
	candidates        *priorityQueue[*candidate]
	candidatesCreated int64 // since the last FindShortestPath (for tests)
	pathBuffer        []int32
	sidetrackBuffer   []int32

	// maximum detour (corridor)
	maxDetour         int64
	hasMaxDetour      bool
	maxDetourRatio    float64
	hasMaxDetourRatio bool
}

// SetMaxDetour limits the returned paths to at most detour longer than the shortest path;
// math.MaxInt64 removes the limit (default). With a limit, the shortest path tree is only
// computed for vertices whose distance to the goal is at most (shortest path + detour), which
// makes queries between nearby vertices of a large graph much faster, and the number of paths is
// finite. Changing it requires a new query. It returns false if detour is negative.
func (g *Graph) SetMaxDetour(detour int64) bool {
	if detour < 0 {
		return false
	}
	g.maxDetour, g.hasMaxDetour = detour, detour != math.MaxInt64
	g.settingsChanged()
	return true
}

// MaxDetour returns the maximum absolute detour, math.MaxInt64 if there is no limit.
func (g *Graph) MaxDetour() int64 {
	if !g.hasMaxDetour {
		return math.MaxInt64
	}
	return g.maxDetour
}

// SetMaxDetourRatio limits the returned paths relative to the shortest path weight, e.g. 0.2 for
// paths at most 20% longer; math.Inf(1) removes the limit (default). If both limits are set, the
// smaller one applies. Changing it requires a new query. It returns false if ratio is negative or
// NaN.
func (g *Graph) SetMaxDetourRatio(ratio float64) bool {
	if math.IsNaN(ratio) || ratio < 0 {
		return false
	}
	g.maxDetourRatio, g.hasMaxDetourRatio = ratio, !math.IsInf(ratio, 1)
	g.settingsChanged()
	return true
}

// MaxDetourRatio returns the maximum relative detour, math.Inf(1) if there is no limit.
func (g *Graph) MaxDetourRatio() float64 {
	if !g.hasMaxDetourRatio {
		return math.Inf(1)
	}
	return g.maxDetourRatio
}

func (g *Graph) settingsChanged() {
	g.changeCount++
	g.ready = false
}

// detourBound returns the allowed detour for a given shortest path weight.
func (g *Graph) detourBound(shortest int64) int64 {
	bound := g.MaxDetour()
	if g.hasMaxDetourRatio {
		if relative := math.Floor(g.maxDetourRatio * float64(shortest)); relative < float64(bound) {
			bound = int64(relative)
		}
	}
	return bound
}

func saturatingAdd(a, b int64) int64 {
	if a > math.MaxInt64-b {
		return math.MaxInt64
	}
	return a + b
}

// FindShortestPath calculates the shortest path tree towards the goal and returns the shortest
// path from start (labels, case-insensitive). Further paths, in order of weight, come from
// FindNextShortestPath. The path is empty if there is none, a label does not exist or start and
// goal are the same vertex.
func (g *Graph) FindShortestPath(start, goal string) Path {
	return g.FindShortestPathByIndex(g.VertexIndex(start), g.VertexIndex(goal))
}

// FindShortestPathByIndex is FindShortestPath for vertices given by index. The path is empty if
// an index is not valid.
func (g *Graph) FindShortestPathByIndex(start, goal int) Path {
	if !g.startQuery(start, goal) {
		return Path{}
	}
	return g.FindNextShortestPath()
}

// FindNextShortestPath returns the next shortest path of the current query, or an empty path if
// there are no more, or if the graph or the query settings changed since FindShortestPath.
//
// Paths may repeat vertices and edges, so a graph with a cycle between start and goal has
// infinitely many paths: limit the number of calls, or set a corridor (SetMaxDetour).
func (g *Graph) FindNextShortestPath() Path {
	p, _, _ := g.nextResult(true)
	return p
}

// nextResult returns the next path of the current query, from Eppstein's enumeration or, with
// SetSimplePathsOnly(true), from Yen's algorithm. With buildPath false, Eppstein only returns the
// weight (faster). ok is false if there are no more paths or there is no current query.
func (g *Graph) nextResult(buildPath bool) (p Path, weight int64, ok bool) {
	if g.simpleOnly {
		return g.nextSimplePath()
	}
	c := g.nextCandidate()
	if c == nil {
		return Path{}, 0, false
	}
	if buildPath {
		p = g.buildPath(c)
	}
	return p, g.distance[g.source] + c.weight, true
}

// Paths returns all paths from start to goal (labels) in order of weight, for use with range:
//
//	for p := range g.Paths("S", "T") { ... }
//
// The query starts when the iteration starts. The graph keeps one query at a time: changing the
// graph or starting another query during the iteration panics. Without a corridor, a graph with
// cycles has infinitely many paths, so stop the loop yourself.
func (g *Graph) Paths(start, goal string) iter.Seq[Path] {
	return g.PathsByIndex(g.VertexIndex(start), g.VertexIndex(goal))
}

// PathsByIndex is Paths for vertices given by index.
func (g *Graph) PathsByIndex(start, goal int) iter.Seq[Path] {
	return func(yield func(Path) bool) {
		for r := range g.resultSeq(start, goal, true) {
			if !yield(r.path) {
				return
			}
		}
	}
}

// PathWeights returns the weights of all paths from start to goal in order, without building
// the paths (faster if only the weights are needed). Same rules as Paths.
func (g *Graph) PathWeights(start, goal string) iter.Seq[int64] {
	return g.PathWeightsByIndex(g.VertexIndex(start), g.VertexIndex(goal))
}

// PathWeightsByIndex is PathWeights for vertices given by index.
func (g *Graph) PathWeightsByIndex(start, goal int) iter.Seq[int64] {
	return func(yield func(int64) bool) {
		for r := range g.resultSeq(start, goal, false) {
			if !yield(r.weight) {
				return
			}
		}
	}
}

type result struct {
	path   Path
	weight int64
}

func (g *Graph) resultSeq(start, goal int, buildPaths bool) iter.Seq[result] {
	return func(yield func(result) bool) {
		if !g.startQuery(start, goal) {
			return
		}
		query, changes := g.queryStamp, g.changeCount
		for {
			if query != g.queryStamp || changes != g.changeCount {
				panic("eppstein: the graph was changed or another query was started during the iteration over paths")
			}
			p, weight, ok := g.nextResult(buildPaths)
			if !ok || !yield(result{p, weight}) {
				return
			}
		}
	}
}

// distanceOf returns the distance of a vertex to the goal of the current query, or Unreached.
func (g *Graph) distanceOf(v int) int64 {
	if v < len(g.stamp) && g.queryStamp != 0 && g.stamp[v] == g.queryStamp {
		return g.distance[v]
	}
	return Unreached
}

// startQuery prepares a query: shortest path tree towards the goal and the first candidate.
// It returns false if an index is not valid.
func (g *Graph) startQuery(start, goal int) bool {
	g.ready = false
	n := len(g.labels)
	if start < 0 || start >= n || goal < 0 || goal >= n {
		return false
	}
	g.ensureAdjacency()
	g.ensureArcFlags()
	g.ensureContractionHierarchy()
	if len(g.stamp) < n { // new arrays; the stamps start over
		size := max(n, 2*len(g.stamp))
		g.stamp = make([]int32, size)
		g.heapStamp = make([]int32, size)
		g.treeEdge = make([]int32, size)
		g.distance = make([]int64, size)
		g.heaps = make([]*heapNode, size)
		g.queryStamp = 0
	}
	g.queryStamp++
	if g.queryStamp == math.MaxInt32 { // stamps wrapped around: start over
		clear(g.stamp)
		clear(g.heapStamp)
		g.queryStamp = 1
	}

	g.source, g.target = start, goal
	g.buildShortestPathTree()
	g.pathBound = -1
	if g.stamp[start] == g.queryStamp {
		g.pathBound = g.detourBound(g.distance[start])
	}

	g.candidates = newPriorityQueue[*candidate](64)
	g.candidatesCreated = 0
	// no paths if the goal cannot be reached, or start and goal are the same vertex
	anyPath := g.stamp[start] == g.queryStamp && start != goal
	if g.simpleOnly {
		g.startSimplePaths(anyPath)
	} else if anyPath {
		g.candidates.Enqueue(&candidate{}, 0)
		g.candidatesCreated = 1
	}
	g.ready = true
	return true
}

// buildShortestPathTree runs Dijkstra on reversed edges from the goal. Negative edges are
// ignored. With a maximum detour, the search stops at distance (shortest path + detour): farther
// vertices cannot be part of any returned path.
func (g *Graph) buildShortestPathTree() {
	if g.usePhast() {
		g.buildShortestPathTreePhast()
		return
	}
	query := g.queryStamp
	fringe := newPriorityQueue[int32](1024) // edges by distance of their tail to the goal
	targetRegion := g.arcFlagsRegionOf(g.target)
	radius := int64(math.MaxInt64)
	if g.source == g.target {
		radius = g.detourBound(0)
	}

	g.stamp[g.target] = query
	g.distance[g.target] = 0
	g.treeEdge[g.target] = -1
	v := int32(g.target)
	for {
		if v >= 0 {
			dv := g.distance[v]
			for _, e := range g.inEdges[g.inStart[v]:g.inStart[v+1]] {
				if g.edgeWeight[e] >= 0 && g.stamp[g.edgeTail[e]] != query && g.hasArcFlag(int(e), targetRegion) {
					fringe.Enqueue(e, g.edgeWeight[e]+dv)
				}
			}
		}
		e, d, ok := fringe.Dequeue()
		if !ok || d > radius { // no pending edges, or outside of the corridor
			break
		}
		v = g.edgeTail[e]
		if g.stamp[v] == query { // already settled
			v = -1
			continue
		}
		g.stamp[v] = query
		g.distance[v] = d
		g.treeEdge[v] = e
		if int(v) == g.source {
			radius = saturatingAdd(d, g.detourBound(d))
		}
	}
}

// nextCandidate extracts the next candidate of the current query and enqueues its successors
// (Eppstein's path graph): the two heap children of its last sidetrack (same prefix, next best
// alternatives for the last sidetrack) and the best sidetrack after the last one (root of
// H(head)). Every sidetrack list is reached exactly once, heap keys never decrease from parent to
// child and deltas are non-negative, so candidates come out in order of weight; at most 3 new
// candidates per extracted path. It returns nil if there are no more or there is no current
// query.
func (g *Graph) nextCandidate() *candidate {
	if !g.ready {
		return nil
	}
	c, _, ok := g.candidates.Dequeue()
	if !ok {
		return nil
	}
	if last := c.heap; last == nil { // shortest path (no sidetracks): best first sidetrack
		g.enqueueCandidate(c, g.sidetrackHeapOf(int32(g.source)))
	} else {
		g.enqueueCandidate(c.parent, last.left) // alternatives for the last sidetrack
		g.enqueueCandidate(c.parent, last.right)
		g.enqueueCandidate(c, g.sidetrackHeapOf(g.edgeHead[last.edge])) // one more sidetrack
	}
	return c
}

// enqueueCandidate enqueues the parent's sidetracks plus the sidetrack of h, unless it exceeds
// the maximum detour.
func (g *Graph) enqueueCandidate(parent *candidate, h *heapNode) {
	if h == nil || parent.weight+h.key > g.pathBound {
		return
	}
	child := &candidate{parent: parent, heap: h, weight: parent.weight + h.key}
	g.candidates.Enqueue(child, child.weight)
	g.candidatesCreated++
}

// sidetrackHeapOf returns the heap of all sidetracks on the tree path from v to the goal,
// building it (and the heaps of the following vertices) if needed. Nil if there are none or v
// cannot reach the goal.
func (g *Graph) sidetrackHeapOf(v int32) *heapNode {
	query := g.queryStamp
	if g.stamp[v] != query {
		return nil
	}
	// vertices on the tree path whose heap is not built yet (iterative, no deep recursion)
	var pending []int32
	for x := v; x >= 0 && g.heapStamp[x] != query; {
		pending = append(pending, x)
		if g.treeEdge[x] < 0 {
			break
		}
		x = g.edgeHead[g.treeEdge[x]]
	}
	// builds heaps from the goal towards v: H(x) = H(next(x)) + sidetracks leaving x
	var own []*heapNode
	for i := len(pending) - 1; i >= 0; i-- {
		x := pending[i]
		own = own[:0]
		for _, e := range g.outEdges[g.outStart[x]:g.outStart[x+1]] {
			head := g.edgeHead[e]
			if e != g.treeEdge[x] && g.edgeWeight[e] >= 0 && g.stamp[head] == query { // head must reach the goal
				own = append(own, &heapNode{edge: e, key: g.edgeWeight[e] + g.distance[head] - g.distance[x], rank: 1})
			}
		}
		var next *heapNode
		if g.treeEdge[x] >= 0 {
			next = g.heaps[g.edgeHead[g.treeEdge[x]]]
		}
		g.heaps[x] = mergeHeaps(next, heapFromNodes(own))
		g.heapStamp[x] = query
	}
	return g.heaps[v]
}

// buildPath builds the full path of a candidate: from the start along the shortest path tree,
// crossing its sidetracks in order.
func (g *Graph) buildPath(c *candidate) Path {
	sidetracks := g.sidetrackBuffer[:0]
	for x := c; x != nil && x.heap != nil; x = x.parent {
		sidetracks = append(sidetracks, x.heap.edge)
	}
	slices.Reverse(sidetracks)
	g.sidetrackBuffer = sidetracks

	edges := g.pathBuffer[:0]
	var weight int64
	v, next := int32(g.source), 0
	for {
		var e int32
		switch {
		case next < len(sidetracks) && g.edgeTail[sidetracks[next]] == v: // cross the next sidetrack
			e = sidetracks[next]
			next++
		case g.treeEdge[v] >= 0 && int(v) != g.target: // follow the shortest path tree
			e = g.treeEdge[v]
		case int(v) == g.target && next < len(sidetracks):
			panic("eppstein: sidetrack not on the path (internal error)")
		default:
			g.pathBuffer = edges
			return Path{g: g, edges: slices.Clone(edges), weight: weight}
		}
		edges = append(edges, e)
		weight += g.edgeWeight[e]
		v = g.edgeHead[e]
	}
}
