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
	"strings"
	"unicode"
)

// stNode is a track (path) described only by its sidetracks.
type stNode struct {
	sidetracks Path
}

// Graph is a directed graph, composed by vertices and directed edges.
type Graph struct {
	// vertices in creation order
	vertices []*Vertex
	// pathsHeap holds candidate tracks ordered by total delta
	pathsHeap *priorityQueue[stNode]
	// ready tells if shortest paths have been calculated for current graph
	ready bool
	// sourceVertex and endVertex are valid after FindShortestPath
	sourceVertex *Vertex
	endVertex    *Vertex

	arcFlags arcFlagsState
}

// NewGraph creates an empty graph.
func NewGraph() *Graph {
	return &Graph{}
}

// CreateVertices creates all vertices of the graph from a comma-separated list of labels,
// removing all previous vertices and edges.
// A label must start with a letter and contain only letters and digits; labels are
// converted to upper case. Repeated and empty labels are ignored.
// It returns false if a label is invalid.
func (g *Graph) CreateVertices(vertices string) bool {
	g.ready = false   // must rebuild trees
	g.vertices = nil  // remove all previous vertices
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
			g.vertices = append(g.vertices, NewVertex(label))
		}
	}
	return true
}

// CreateEdges creates directional edges from tails to heads (comma-separated lists of
// labels, paired by position), all with the same weight and group.
// It returns false if a vertex does not exist or the lists have different lengths.
func (g *Graph) CreateEdges(tails, heads string, weight int, group string) bool {
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
	g.invalidateArcFlags() // new arcs need flags
	return true
}

// EdgeGroupWeights sets the weight of all edges of a group.
func (g *Graph) EdgeGroupWeights(group string, weight int) {
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

	// Recomputes arc flags lazily if enabled and graph changed since last preprocessing
	g.ensureArcFlags()
	// Builds shortest path tree for all vertices to t (Dijkstra)
	g.buildShortestPathTree()
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
	// Lazy expansion: enqueues all tracks having this one as prefix plus one sidetrack
	g.addSidetracks(node.sidetracks)
	return g.rebuildPath(node.sidetracks)
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
	for _, v := range g.vertices {
		if v.Equals(label) {
			return v
		}
	}
	return nil
}

// resetGraphState clears shortest path tree pointers and distances.
func (g *Graph) resetGraphState() {
	for _, v := range g.vertices {
		v.EdgeToPath = nil
		v.Distance = Unreached
	}
}

// buildShortestPathTree runs Dijkstra backwards from the endpoint. Negative edges are ignored.
func (g *Graph) buildShortestPathTree() {
	g.resetGraphState()

	v := g.endVertex
	v.Distance = 0
	fringe := newPriorityQueue[*Edge](len(g.vertices))
	// Region of endpoint for arc-flag pruning (-1 = no pruning)
	targetRegion := g.arcFlagsRegionOf(g.endVertex)

	for {
		if v != nil {
			for _, e := range v.RelatedEdges { // evaluate all incoming edges
				if e.Head == v && e.Weight >= 0 && e.HasArcFlag(targetRegion) {
					fringe.Enqueue(e, e.Weight+e.Head.Distance)
				}
			}
		}

		e, _, ok := fringe.Dequeue()
		if !ok { // no pending edges, finished
			break
		}
		v = e.Tail
		if v.Distance == Unreached { // distance not calculated yet
			v.Distance = e.Weight + e.Head.Distance
			v.EdgeToPath = e
		} else {
			v = nil
		}
	}
}

// buildSidetracksHeap initializes the heap with the shortest path (no sidetracks).
func (g *Graph) buildSidetracksHeap() {
	g.pathsHeap = newPriorityQueue[stNode](len(g.vertices))
	g.pathsHeap.Enqueue(stNode{sidetracks: Path{}}, 0)
}

// addSidetracks enqueues all children of a track: the same sidetracks plus one more,
// taken from a vertex of the shortest path tree between the head of the last sidetrack
// (or s) and t. Every track has a unique parent, so each path is generated once; deltas
// are non-negative, so children never come before their parent.
func (g *Graph) addSidetracks(p Path) {
	v := g.sourceVertex
	if len(p) > 0 {
		v = p[len(p)-1].Head
	}
	// Walks shortest path tree from v to t (tree paths are simple, so this terminates)
	for ; v != nil; v = v.Next() {
		for _, e := range v.RelatedEdges {
			if e.IsSidetrackOf(v) && (e.Head.EdgeToPath != nil || e.Head == g.endVertex) {
				child := make(Path, len(p), len(p)+1)
				copy(child, p)
				child = append(child, e)
				g.pathsHeap.Enqueue(stNode{sidetracks: child}, child.DeltaWeight())
			}
		}
	}
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
