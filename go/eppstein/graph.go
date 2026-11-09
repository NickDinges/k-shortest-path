// Package eppstein finds the k shortest paths between two vertices of a directed
// graph with Eppstein's algorithm ("Finding the k Shortest Paths", 1998).
//
// Paths may repeat vertices and edges. After one shortest path tree computation,
// every further path is produced on demand:
//
//	g := eppstein.NewGraph()
//	g.CreateVertices("S,A,T")
//	g.CreateEdges("S,A,S", "A,T,T", 1, "roads")
//	for p, i := g.FindShortestPath("S", "T"), 0; p.IsValid() && i < 10; p, i = g.FindNextShortestPath(), i+1 {
//		fmt.Println(p.VertexNames(), p.Weight())
//	}
package eppstein

import (
	"math"
	"strings"
	"unicode"
)

// stNode is a track (path) described only by its sidetracks. It stores its parent
// (same sidetracks without the last one) and one sidetrack, so creating a candidate
// costs O(1) time and memory instead of copying the whole sidetrack list.
type stNode struct {
	parent    *stNode   // nil for the root (shortest path, no sidetracks)
	sidetrack *Edge     // last sidetrack, nil for the root
	heap      *heapNode // heap node of the last sidetrack, nil for the root
	weight    int64     // sum of sidetrack deltas
}

// sidetracks builds the full sidetrack list from the chain of parent nodes.
func (n *stNode) sidetracks() Path {
	count := 0
	for x := n; x.sidetrack != nil; x = x.parent {
		count++
	}
	result := make(Path, count)
	for x := n; x.sidetrack != nil; x = x.parent {
		count--
		result[count] = x.sidetrack
	}
	return result
}

// Graph is a directed graph, composed by vertices and directed edges.
type Graph struct {
	// vertices in creation order
	vertices []*Vertex
	// vertexIndex finds vertices by (upper case) label in constant time
	vertexIndex map[string]*Vertex
	// pathsHeap holds candidate tracks ordered by total delta
	pathsHeap *priorityQueue[*stNode]
	// ready tells if shortest paths have been calculated for current graph
	ready bool
	// sourceVertex and endVertex are valid after FindShortestPath
	sourceVertex *Vertex
	endVertex    *Vertex

	// pathBound is the maximum sum of sidetrack deltas of returned paths (corridor)
	pathBound int64
	// maximum detour (corridor), see SetMaxDetour and SetMaxDetourRatio
	maxDetour         int64
	hasMaxDetour      bool
	maxDetourRatio    float64
	hasMaxDetourRatio bool
	// candidatesCreated counts candidate paths since the last FindShortestPath (for tests)
	candidatesCreated int64
	// adjacencyValid tells if inEdges/outEdges of all vertices match RelatedEdges
	adjacencyValid bool

	arcFlags arcFlagsState
}

// SetMaxDetour limits the returned paths to at most detour longer than the shortest path.
// math.MaxInt64 removes the limit (default). With a limit, the shortest path tree is only
// computed for vertices whose distance to t is at most (shortest path + detour), which makes
// queries between nearby vertices of a large graph much faster. FindNextShortestPath returns
// an empty path once all paths within the limit have been returned.
// Changing it requires a new call of FindShortestPath. It returns false if detour is negative.
func (g *Graph) SetMaxDetour(detour int64) bool {
	if detour < 0 {
		return false
	}
	g.maxDetour = detour
	g.hasMaxDetour = detour != math.MaxInt64
	g.ready = false
	return true
}

// MaxDetour returns the maximum absolute detour, math.MaxInt64 if there is no limit.
func (g *Graph) MaxDetour() int64 {
	if !g.hasMaxDetour {
		return math.MaxInt64
	}
	return g.maxDetour
}

// SetMaxDetourRatio limits the returned paths relative to the shortest path weight, e.g. 0.2
// for paths at most 20% longer. math.Inf(1) removes the limit (default). If both limits are
// set, the smaller one applies. Changing it requires a new call of FindShortestPath.
// It returns false if ratio is negative or NaN.
func (g *Graph) SetMaxDetourRatio(ratio float64) bool {
	if math.IsNaN(ratio) || ratio < 0 {
		return false
	}
	g.maxDetourRatio = ratio
	g.hasMaxDetourRatio = !math.IsInf(ratio, 1)
	g.ready = false
	return true
}

// MaxDetourRatio returns the maximum relative detour, math.Inf(1) if there is no limit.
func (g *Graph) MaxDetourRatio() float64 {
	if !g.hasMaxDetourRatio {
		return math.Inf(1)
	}
	return g.maxDetourRatio
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

// saturatingAdd adds two non-negative values, saturating at math.MaxInt64.
func saturatingAdd(a, b int64) int64 {
	if a > math.MaxInt64-b {
		return math.MaxInt64
	}
	return a + b
}

// NewGraph creates an empty graph.
func NewGraph() *Graph {
	return &Graph{vertexIndex: map[string]*Vertex{}}
}

// CreateVertices creates all vertices of the graph from a comma-separated list of labels,
// removing all previous vertices and edges.
// A label must start with a letter and contain only letters and digits; labels are
// converted to upper case. Repeated and empty labels are ignored.
// It returns false if a label is invalid.
func (g *Graph) CreateVertices(vertices string) bool {
	g.ready = false  // must rebuild trees
	g.vertices = nil // remove all previous vertices
	g.vertexIndex = map[string]*Vertex{}
	g.adjacencyValid = false
	g.resetArcFlags() // partition refers to removed vertices

	for _, label := range strings.Split(strings.ToUpper(vertices), ",") {
		if label == "" { // ignore empty labels
			continue
		}
		for i, c := range label {
			if !unicode.IsLetter(c) && (i == 0 || !unicode.IsDigit(c)) {
				return false // must start with a letter, then letters or digits
			}
		}
		if g.getVertex(label) == nil {
			v := NewVertex(label)
			g.vertices = append(g.vertices, v)
			g.vertexIndex[label] = v
		}
	}
	return true
}

// CreateEdges creates directional edges from tails to heads (comma-separated lists of
// labels, paired by position), all with the same weight and group.
// It returns false if a vertex does not exist or the lists have different lengths.
func (g *Graph) CreateEdges(tails, heads string, weight int64, group string) bool {
	tailList := g.parseVertices(tails)
	headList := g.parseVertices(heads)
	if tailList == nil || headList == nil || len(tailList) != len(headList) {
		return false
	}
	group = strings.ToUpper(group)

	for i := range tailList {
		e := NewEdge(tailList[i], headList[i], weight, group)
		tailList[i].RelatedEdges = append(tailList[i].RelatedEdges, e)
		if tailList[i] != headList[i] { // avoids duplicated reference for self-pointing edges
			headList[i].RelatedEdges = append(headList[i].RelatedEdges, e)
		}
	}
	g.ready = false
	g.adjacencyValid = false
	g.invalidateArcFlags() // new arcs need flags
	return true
}

// EdgeGroupWeights sets the weight of all edges of a group.
func (g *Graph) EdgeGroupWeights(group string, weight int64) {
	group = strings.ToUpper(group)
	for _, v := range g.vertices {
		for _, e := range v.RelatedEdges {
			if e.Group == group {
				e.Weight = weight
			}
		}
	}
	g.ready = false
	g.invalidateArcFlags() // shortest paths may have changed
}

// FindShortestPath calculates the shortest path tree towards t and returns the shortest
// path from s to t. It returns an empty path if no path exists or a label is not valid.
// Further paths are obtained with FindNextShortestPath.
func (g *Graph) FindShortestPath(s, t string) Path {
	g.ready = false

	g.sourceVertex = g.getVertex(strings.ToUpper(s))
	g.endVertex = g.getVertex(strings.ToUpper(t))
	if g.sourceVertex == nil || g.endVertex == nil {
		return Path{}
	}

	// Splits edges into incoming and outgoing slices if the graph changed
	g.ensureAdjacency()
	// Recomputes arc flags lazily if enabled and graph changed since last preprocessing
	g.ensureArcFlags()
	// Builds shortest path tree for all vertices to t (Dijkstra)
	g.buildShortestPathTree()
	// Maximum sum of sidetrack deltas (corridor), -1 if t cannot be reached
	g.pathBound = -1
	if g.sourceVertex.Distance != Unreached {
		g.pathBound = g.detourBound(g.sourceVertex.Distance)
	}
	// Initializes the heap of tracks, it is expanded lazily
	g.buildSidetracksHeap()
	g.ready = true

	return g.FindNextShortestPath()
}

// FindNextShortestPath returns the next shortest path, or an empty path if there are no
// more paths or FindShortestPath has not been called since the last graph change.
//
// Paths may repeat vertices and edges, so a graph with a cycle reachable between s and t
// has infinitely many paths: limit the number of calls.
func (g *Graph) FindNextShortestPath() Path {
	if !g.ready {
		return Path{}
	}
	node, _, ok := g.pathsHeap.Dequeue()
	if !ok {
		return Path{}
	}
	// Lazy expansion: enqueues at most 3 successors of this track
	g.addSidetracks(node)
	return g.rebuildPath(node.sidetracks())
}

// parseVertices parses a comma-separated list of labels, nil if a vertex does not exist.
func (g *Graph) parseVertices(labels string) []*Vertex {
	var result []*Vertex
	for _, label := range strings.Split(strings.ToUpper(labels), ",") {
		v := g.getVertex(label)
		if v == nil {
			return nil
		}
		result = append(result, v)
	}
	return result
}

// getVertex returns the vertex with the given label, nil if not found or empty label.
func (g *Graph) getVertex(label string) *Vertex {
	if label == "" {
		return nil
	}
	if g.vertexIndex == nil { // zero value Graph{} instead of NewGraph()
		g.vertexIndex = map[string]*Vertex{}
	}
	return g.vertexIndex[strings.ToUpper(label)]
}

// ensureAdjacency builds the incoming and outgoing edge slices of all vertices from
// RelatedEdges if the graph changed. Done once per graph change instead of in CreateEdges,
// so building a graph stays cheap, while Dijkstra and the path enumeration only visit edges
// of the right direction.
func (g *Graph) ensureAdjacency() {
	if g.adjacencyValid {
		return
	}
	for _, v := range g.vertices {
		outCount, inCount := 0, 0
		for _, e := range v.RelatedEdges {
			if e.Tail == v {
				outCount++
			}
			if e.Head == v {
				inCount++
			}
		}
		v.outEdges = make([]*Edge, 0, outCount)
		v.inEdges = make([]*Edge, 0, inCount)
		for _, e := range v.RelatedEdges {
			if e.Tail == v {
				v.outEdges = append(v.outEdges, e)
			}
			if e.Head == v {
				v.inEdges = append(v.inEdges, e)
			}
		}
	}
	g.adjacencyValid = true
}

// resetGraphState clears shortest path tree pointers and distances.
func (g *Graph) resetGraphState() {
	for _, v := range g.vertices {
		v.EdgeToPath = nil
		v.Distance = Unreached
		v.sidetrackHeap = nil
		v.sidetrackHeapBuilt = false
	}
}

// buildShortestPathTree runs Dijkstra backwards from the endpoint. Negative edges are ignored.
// With a maximum detour, the search stops at distance (shortest path + detour): farther
// vertices cannot be part of any returned path.
func (g *Graph) buildShortestPathTree() {
	g.resetGraphState()

	v := g.endVertex
	v.Distance = 0
	fringe := newPriorityQueue[*Edge](1024)
	// Region of endpoint for arc-flag pruning (-1 = no pruning)
	targetRegion := g.arcFlagsRegionOf(g.endVertex)
	// Search radius, reduced to (shortest path + detour) once s is settled
	radius := int64(math.MaxInt64)
	if g.sourceVertex == g.endVertex {
		radius = g.detourBound(0)
	}

	for {
		if v != nil {
			for _, e := range v.inEdges {
				// skip negative edges, settled tails and arcs pruned by arc flags
				if e.Weight >= 0 && e.Tail.Distance == Unreached && e.HasArcFlag(targetRegion) {
					fringe.Enqueue(e, e.Weight+v.Distance)
				}
			}
		}

		e, distance, ok := fringe.Dequeue()
		if !ok || distance > radius { // no pending edges, or outside of corridor
			break
		}
		v = e.Tail
		if v.Distance == Unreached { // distance not calculated yet
			v.Distance = distance
			v.EdgeToPath = e
			if v == g.sourceVertex {
				radius = saturatingAdd(distance, g.detourBound(distance))
			}
		} else {
			v = nil
		}
	}
}

// buildSidetracksHeap initializes the heap with the shortest path (no sidetracks).
func (g *Graph) buildSidetracksHeap() {
	g.pathsHeap = newPriorityQueue[*stNode](64)
	g.pathsHeap.Enqueue(&stNode{}, 0)
	g.candidatesCreated = 1
}

// addSidetracks enqueues the successors of an extracted track (Eppstein's path graph):
// the two heap children of its last sidetrack (same prefix, next best alternatives for the
// last sidetrack) and the best sidetrack after the last one (root of H(head)).
// Every sidetrack list is reached exactly once, heap keys never decrease from parent to child
// and deltas are non-negative, so tracks come out in order of weight. At most 3 candidates
// per extracted path; works with cycles.
func (g *Graph) addSidetracks(node *stNode) {
	last := node.heap
	if last == nil { // shortest path (no sidetracks): best first sidetrack
		g.enqueueCandidate(node, g.sidetrackHeapOf(g.sourceVertex))
		return
	}
	g.enqueueCandidate(node.parent, last.left) // alternatives for the last sidetrack
	g.enqueueCandidate(node.parent, last.right)
	g.enqueueCandidate(node, g.sidetrackHeapOf(last.edge.Head)) // one more sidetrack
}

// enqueueCandidate enqueues the parent's sidetracks plus the sidetrack of h, unless it
// exceeds the maximum detour.
func (g *Graph) enqueueCandidate(parent *stNode, h *heapNode) {
	if h == nil || parent.weight+h.key > g.pathBound {
		return
	}
	child := &stNode{parent: parent, sidetrack: h.edge, heap: h, weight: parent.weight + h.key}
	g.pathsHeap.Enqueue(child, child.weight)
	g.candidatesCreated++
}

// sidetrackHeapOf returns the heap of all sidetracks on the tree path from v to t, building
// it (and the heaps of the following vertices) if needed. Nil if there are none or v cannot
// reach t.
func (g *Graph) sidetrackHeapOf(v *Vertex) *heapNode {
	if v.Distance == Unreached {
		return nil
	}
	// Vertices on the tree path whose heap is not built yet (iterative, no deep recursion)
	var pending []*Vertex
	for x := v; x != nil && !x.sidetrackHeapBuilt; x = x.Next() {
		pending = append(pending, x)
	}
	// Builds heaps from t towards v: H(x) = H(next(x)) + sidetracks leaving x
	var own []*heapNode
	for i := len(pending) - 1; i >= 0; i-- {
		x := pending[i]
		own = own[:0]
		for _, e := range x.outEdges {
			if e.IsSidetrackOf(x) && e.Head.Distance != Unreached { // head must reach t
				own = append(own, &heapNode{edge: e, key: e.Delta(), rank: 1})
			}
		}
		var next *heapNode
		if n := x.Next(); n != nil {
			next = n.sidetrackHeap
		}
		x.sidetrackHeap = mergeHeaps(next, heapFromNodes(own))
		x.sidetrackHeapBuilt = true
	}
	return v.sidetrackHeap
}

// rebuildPath reconstructs the full path from s to t crossing the given sidetracks.
func (g *Graph) rebuildPath(sidetracks Path) Path {
	path := Path{}
	v := g.sourceVertex
	i := 0
	for v != nil {
		if i < len(sidetracks) && sidetracks[i].Tail == v { // cross next sidetrack
			path = append(path, sidetracks[i])
			v = sidetracks[i].Head
			i++
		} else { // continue on shortest path
			if v.EdgeToPath == nil {
				break
			}
			path = append(path, v.EdgeToPath)
			v = v.Next()
		}
	}
	return path
}
