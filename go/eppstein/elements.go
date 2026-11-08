package eppstein

import (
	"math"
	"strconv"
	"strings"
)

// Unreached is the value of Vertex.Distance for vertices whose distance to the
// endpoint has not been calculated (or that cannot reach the endpoint).
const Unreached = math.MinInt

// Vertex is a simple vertex with a label.
type Vertex struct {
	// label of the vertex (upper case)
	label string
	// Object is a reference for a user object, for future use.
	Object interface{}

	// EdgeToPath points to the edge in the shortest path tree.
	EdgeToPath *Edge
	// Distance to endpoint in shortest path calculation, Unreached if unknown.
	Distance int
	// RelatedEdges contains incoming and outgoing edges of the vertex.
	RelatedEdges []*Edge
	// Region of the vertex for the arc-flags optimization, -1 if not assigned.
	Region int
}

// NewVertex creates a vertex with the given label.
func NewVertex(label string) *Vertex {
	return &Vertex{label: label, Distance: Unreached, Region: -1}
}

// Next returns the next vertex in the shortest path, nil if there is none.
func (v *Vertex) Next() *Vertex {
	if v.EdgeToPath == nil {
		return nil
	}
	return v.EdgeToPath.Head
}

// String returns the vertex label.
func (v *Vertex) String() string {
	return v.label
}

// Equals compares a vertex with another vertex or with a label (case-insensitive).
func (v *Vertex) Equals(obj interface{}) bool {
	switch o := obj.(type) {
	case *Vertex:
		return o != nil && strings.EqualFold(v.label, o.label)
	case string:
		return strings.EqualFold(v.label, o)
	}
	return false
}

// Edge is a directional edge, it goes from Tail to Head.
type Edge struct {
	// Tail is the start vertex.
	Tail *Vertex
	// Head is the end vertex.
	Head *Vertex
	// Weight of the edge, negative edges are ignored by the algorithm.
	Weight int
	// Group label of the edge.
	Group string

	// arcFlags[r] is true if this arc lies on a shortest path from its tail to some
	// vertex of region r. Nil if arc flags have not been computed.
	arcFlags []bool
}

// NewEdge creates a directional edge from tail to head.
func NewEdge(tail, head *Vertex, weight int, group string) *Edge {
	return &Edge{Tail: tail, Head: head, Weight: weight, Group: group}
}

// Delta returns the sidetrack delta (deviation from shortest path).
// Only valid when the shortest path tree has been calculated.
func (e *Edge) Delta() int {
	return e.Weight + e.Head.Distance - e.Tail.Distance
}

// HasArcFlag tells if the arc may be part of a shortest path towards the given region.
// It returns true if the flag is set, if no flags are available or if region is negative.
func (e *Edge) HasArcFlag(region int) bool {
	if region < 0 || e.arcFlags == nil || region >= len(e.arcFlags) {
		return true
	}
	return e.arcFlags[region]
}

// IsSidetrackOf tells if the edge is a possible sidetrack of the given vertex.
func (e *Edge) IsSidetrackOf(v *Vertex) bool {
	return e.Tail == v && e != v.EdgeToPath && e.Weight >= 0
}

// String returns tail and head labels with the weight, e.g. "A--5-->B".
func (e *Edge) String() string {
	return e.Tail.String() + "--" + strconv.Itoa(e.Weight) + "-->" + e.Head.String()
}

// Path is a path composed by a sequence of edges.
type Path []*Edge

// IsValid returns false if the path is empty, true if not.
func (p Path) IsValid() bool {
	return len(p) > 0
}

// VertexNames returns a comma-separated list containing all vertices of the path.
func (p Path) VertexNames() string {
	if !p.IsValid() {
		return "(empty)"
	}
	var b strings.Builder
	b.WriteString(p[0].Tail.String())
	for _, e := range p {
		b.WriteByte(',')
		b.WriteString(e.Head.String())
	}
	return b.String()
}

// Weight returns the sum of all edge weights of the path.
func (p Path) Weight() int {
	total := 0
	for _, e := range p {
		total += e.Weight
	}
	return total
}

// DeltaWeight returns the sum of all edge deltas of the path.
func (p Path) DeltaWeight() int {
	total := 0
	for _, e := range p {
		total += e.Delta()
	}
	return total
}

// String returns the comma-separated list of deltas of the path.
func (p Path) String() string {
	if !p.IsValid() {
		return "(empty)"
	}
	parts := make([]string, len(p))
	for i, e := range p {
		parts[i] = strconv.Itoa(e.Delta())
	}
	return strings.Join(parts, ",")
}
