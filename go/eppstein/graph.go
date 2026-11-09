// Package eppstein finds the k shortest paths between two vertices of a directed graph with
// Eppstein's algorithm ("Finding the k Shortest Paths", 1998).
//
// Paths may repeat vertices and edges. After one shortest path tree computation, every further
// path is produced on demand:
//
//	g := eppstein.NewGraph()
//	g.CreateVertices("S,A,T")
//	g.CreateEdges("S,A,S", "A,T,T", 1, "roads")
//	for p := range g.Paths("S", "T") {
//		fmt.Println(p.VertexNames(), p.Weight())
//	}
//
// Vertices and edges are stored in arrays; Vertex and Edge are lightweight values referring to
// them. A graph is changed only through its methods, so precomputed data (edge lists by
// direction, arc flags, the current query) is invalidated automatically.
//
// Error handling: methods that create or look up something from user data report a failure in
// their result (false or -1). Programming errors (an index out of range, an edge of another
// graph, changing the graph while ranging over its paths) panic, like an index out of range on a
// slice. Malformed import files return an error. A Graph is not safe for concurrent use.
package eppstein

import (
	"fmt"
	"iter"
	"strings"
	"unicode"
)

// Graph is a directed graph with weighted edges.
type Graph struct {
	// vertices
	labels     []string
	labelIndex map[string]int
	tags       []any

	// edges, as parallel slices
	edgeTail, edgeHead []int32
	edgeWeight         []int64
	edgeGroup          []int32
	groups             []string // group 0: no group
	groupIndex         map[string]int32

	// edges by direction (compressed rows), rebuilt on demand after a change: the edges leaving
	// vertex v are outEdges[outStart[v]:outStart[v+1]]
	adjacencyValid     bool
	outStart, outEdges []int32
	inStart, inEdges   []int32
	changeCount        int // incremented on every change of the graph and of query settings
	queryState             // current query (paths.go)
	arcFlagsState          // arc flags (arcflags.go)
}

// NewGraph creates an empty graph. The zero value Graph{} is usable as well.
func NewGraph() *Graph {
	return &Graph{}
}

func (g *Graph) init() {
	if g.labelIndex == nil {
		g.labelIndex = map[string]int{}
		g.groupIndex = map[string]int32{"": 0}
		g.groups = []string{""}
	}
}

// VertexCount returns the number of vertices; valid indices are 0 .. VertexCount()-1.
func (g *Graph) VertexCount() int { return len(g.labels) }

// EdgeCount returns the number of edges; valid edge numbers are 0 .. EdgeCount()-1.
func (g *Graph) EdgeCount() int { return len(g.edgeTail) }

// Vertex returns the vertex with an index. It panics if the index is not valid.
func (g *Graph) Vertex(index int) Vertex {
	if index < 0 || index >= len(g.labels) {
		panic(fmt.Sprintf("eppstein: no vertex with index %d", index))
	}
	return Vertex{g, index}
}

// Edge returns the edge with a number. It panics if the number is not valid.
func (g *Graph) Edge(id int) Edge {
	g.checkEdge(id)
	return Edge{g, id}
}

// FindVertex returns the vertex with a label (case-insensitive); ok is false if there is none.
func (g *Graph) FindVertex(label string) (v Vertex, ok bool) {
	if index := g.VertexIndex(label); index >= 0 {
		return Vertex{g, index}, true
	}
	return Vertex{}, false
}

// VertexIndex returns the index of the vertex with a label (case-insensitive), or -1 if it does
// not exist.
func (g *Graph) VertexIndex(label string) int {
	if label == "" {
		return -1
	}
	if index, ok := g.labelIndex[label]; ok {
		return index
	}
	if index, ok := g.labelIndex[strings.ToUpper(label)]; ok {
		return index
	}
	return -1
}

// Vertices returns all vertices in order of their index.
func (g *Graph) Vertices() iter.Seq[Vertex] {
	return func(yield func(Vertex) bool) {
		for v := range g.labels {
			if !yield(Vertex{g, v}) {
				return
			}
		}
	}
}

// Edges returns all edges in order of creation.
func (g *Graph) Edges() iter.Seq[Edge] {
	return func(yield func(Edge) bool) {
		for e := range g.edgeTail {
			if !yield(Edge{g, e}) {
				return
			}
		}
	}
}

// CreateVertices creates all vertices from a comma-separated list of labels, removing all
// previous vertices and edges. Repeated and empty labels are ignored. A label starts with a
// letter and contains only letters and digits (case-insensitive, stored in upper case).
// It returns false at the first invalid label (the vertices before it are created).
func (g *Graph) CreateVertices(vertices string) bool {
	g.clear()
	for _, label := range strings.Split(vertices, ",") {
		if label == "" {
			continue
		}
		if g.AddVertex(label) < 0 {
			return false
		}
	}
	return true
}

// AddVertex adds one vertex, keeping all existing vertices and edges. It returns the index of
// the vertex (the existing one if the label is already used), or -1 if the label is invalid.
func (g *Graph) AddVertex(label string) int {
	g.init()
	label = strings.ToUpper(label)
	if !isValidLabel(label) {
		return -1
	}
	if index, ok := g.labelIndex[label]; ok {
		return index
	}
	index := len(g.labels)
	g.labels = append(g.labels, label)
	g.labelIndex[label] = index
	g.changed(true)
	return index
}

// CreateEdges creates directed edges from tails to heads (comma-separated lists of labels,
// paired by position), all with the same weight and group (empty for none). It returns false,
// creating no edge, if a vertex does not exist or the lists have different lengths.
func (g *Graph) CreateEdges(tails, heads string, weight int64, group string) bool {
	tailLabels := strings.Split(tails, ",")
	headLabels := strings.Split(heads, ",")
	if len(tailLabels) != len(headLabels) {
		return false
	}
	tailIndices := make([]int, len(tailLabels))
	headIndices := make([]int, len(headLabels))
	for i := range tailLabels {
		tailIndices[i] = g.VertexIndex(tailLabels[i])
		headIndices[i] = g.VertexIndex(headLabels[i])
		if tailIndices[i] < 0 || headIndices[i] < 0 {
			return false
		}
	}
	for i := range tailIndices {
		g.AddEdgeGroup(tailIndices[i], headIndices[i], weight, group)
	}
	return true
}

// AddEdge adds one directed edge without a group between vertices given by index. It returns
// the number of the new edge, or -1 if an index is not valid.
func (g *Graph) AddEdge(tail, head int, weight int64) int {
	return g.AddEdgeGroup(tail, head, weight, "")
}

// AddEdgeGroup adds one directed edge with a group (case-insensitive, see EdgeGroupWeights;
// empty for none) between vertices given by index. It returns the number of the new edge, or
// -1 if an index is not valid.
func (g *Graph) AddEdgeGroup(tail, head int, weight int64, group string) int {
	if tail < 0 || tail >= len(g.labels) || head < 0 || head >= len(g.labels) {
		return -1
	}
	g.init()
	id := len(g.edgeTail)
	g.edgeTail = append(g.edgeTail, int32(tail))
	g.edgeHead = append(g.edgeHead, int32(head))
	g.edgeWeight = append(g.edgeWeight, weight)
	g.edgeGroup = append(g.edgeGroup, g.groupID(group))
	g.changed(true)
	return id
}

// SetEdgeWeight changes the weight of one edge. It panics if the edge belongs to another graph.
func (g *Graph) SetEdgeWeight(e Edge, weight int64) {
	if e.g != g {
		panic("eppstein: SetEdgeWeight: the edge belongs to another graph")
	}
	g.edgeWeight[e.id] = weight
	g.changed(false)
}

// EdgeGroupWeights sets the weight of all edges of a group (case-insensitive); for example a
// negative weight switches them off.
func (g *Graph) EdgeGroupWeights(group string, weight int64) {
	id, ok := g.groupIndex[strings.ToUpper(group)]
	if !ok {
		return
	}
	for e, eg := range g.edgeGroup {
		if eg == id {
			g.edgeWeight[e] = weight
		}
	}
	g.changed(false)
}

// clear removes all vertices and edges and disables arc flags.
func (g *Graph) clear() {
	g.labels = nil
	g.labelIndex = nil
	g.tags = nil
	g.edgeTail, g.edgeHead, g.edgeWeight, g.edgeGroup = nil, nil, nil, nil
	g.groups, g.groupIndex = nil, nil
	g.init()
	g.DisableArcFlags()
	g.changed(true)
}

// changed invalidates the current query and precomputed data. structure is true if vertices or
// edges were added or removed (not only weights).
func (g *Graph) changed(structure bool) {
	g.changeCount++
	g.ready = false
	if structure {
		g.adjacencyValid = false
	}
	g.arcFlagsValid = false
}

func (g *Graph) groupID(group string) int32 {
	if group == "" {
		return 0
	}
	group = strings.ToUpper(group)
	id, ok := g.groupIndex[group]
	if !ok {
		id = int32(len(g.groups))
		g.groups = append(g.groups, group)
		g.groupIndex[group] = id
	}
	return id
}

func (g *Graph) checkEdge(id int) {
	if id < 0 || id >= len(g.edgeTail) {
		panic(fmt.Sprintf("eppstein: no edge with number %d", id))
	}
}

// isValidLabel tells if a label starts with a letter and contains only letters and digits.
func isValidLabel(label string) bool {
	if label == "" {
		return false
	}
	for i, c := range label {
		if !unicode.IsLetter(c) && (i == 0 || !unicode.IsDigit(c)) {
			return false
		}
	}
	return true
}

// ensureAdjacency builds the edge lists by direction (counting sort by vertex) if the graph
// changed.
func (g *Graph) ensureAdjacency() {
	if g.adjacencyValid {
		return
	}
	g.outStart, g.outEdges = buildRows(g.edgeTail, len(g.labels))
	g.inStart, g.inEdges = buildRows(g.edgeHead, len(g.labels))
	g.adjacencyValid = true
}

// buildRows groups the edges by a vertex (tail or head): start[v] is the start of v's row in
// rows, which lists the edge numbers in order of creation.
func buildRows(vertexOfEdge []int32, vertexCount int) (start, rows []int32) {
	start = make([]int32, vertexCount+1)
	for _, v := range vertexOfEdge {
		start[v+1]++
	}
	for v := 0; v < vertexCount; v++ {
		start[v+1] += start[v]
	}
	rows = make([]int32, len(vertexOfEdge))
	next := append([]int32(nil), start...)
	for e, v := range vertexOfEdge {
		rows[next[v]] = int32(e)
		next[v]++
	}
	return start, rows
}
