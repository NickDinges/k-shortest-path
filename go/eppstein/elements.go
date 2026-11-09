package eppstein

import (
	"iter"
	"math"
	"strconv"
	"strings"
)

// Unreached is the distance of a vertex whose distance to the goal is unknown (no query yet,
// goal not reachable, or outside of the corridor).
const Unreached int64 = math.MinInt64

// Vertex is a vertex of a Graph: a lightweight value (graph and index). Two Vertex values are
// equal (==) if they refer to the same vertex of the same graph. Distance, TreeEdge and Next
// describe the shortest path tree of the last query.
type Vertex struct {
	g     *Graph
	index int
}

// Index returns the position of the vertex in its graph.
func (v Vertex) Index() int { return v.index }

// Label returns the label of the vertex (upper case).
func (v Vertex) Label() string { return v.g.labels[v.index] }

// Tag returns the user object attached to the vertex, nil by default.
func (v Vertex) Tag() any {
	if v.index < len(v.g.tags) {
		return v.g.tags[v.index]
	}
	return nil
}

// SetTag attaches any user object to the vertex.
func (v Vertex) SetTag(tag any) {
	if v.index >= len(v.g.tags) {
		if tag == nil {
			return
		}
		v.g.tags = append(v.g.tags, make([]any, len(v.g.labels)-len(v.g.tags))...)
	}
	v.g.tags[v.index] = tag
}

// Distance returns the distance to the goal of the last query, or Unreached.
func (v Vertex) Distance() int64 { return v.g.distanceOf(v.index) }

// TreeEdge returns the edge of the shortest path tree leaving the vertex (the best first step
// towards the goal of the last query); ok is false if there is none.
func (v Vertex) TreeEdge() (e Edge, ok bool) {
	if v.g.distanceOf(v.index) == Unreached || v.g.treeEdge[v.index] < 0 {
		return Edge{}, false
	}
	return Edge{v.g, int(v.g.treeEdge[v.index])}, true
}

// Next returns the next vertex on the shortest path to the goal of the last query; ok is false
// if there is none.
func (v Vertex) Next() (next Vertex, ok bool) {
	if e, ok := v.TreeEdge(); ok {
		return e.Head(), true
	}
	return Vertex{}, false
}

// Region returns the arc-flags region of the vertex, -1 if arc flags are not computed.
func (v Vertex) Region() int { return v.g.regionOf(v.index) }

// OutEdges returns the edges leaving the vertex.
func (v Vertex) OutEdges() iter.Seq[Edge] {
	return v.g.edgeRow(v.index, true)
}

// InEdges returns the edges arriving at the vertex.
func (v Vertex) InEdges() iter.Seq[Edge] {
	return v.g.edgeRow(v.index, false)
}

// String returns the label of the vertex.
func (v Vertex) String() string {
	if v.g == nil {
		return "(none)"
	}
	return v.Label()
}

func (g *Graph) edgeRow(v int, out bool) iter.Seq[Edge] {
	return func(yield func(Edge) bool) {
		g.ensureAdjacency()
		start, rows := g.inStart, g.inEdges
		if out {
			start, rows = g.outStart, g.outEdges
		}
		for _, e := range rows[start[v]:start[v+1]] {
			if !yield(Edge{g, int(e)}) {
				return
			}
		}
	}
}

// Edge is a directed edge of a Graph, from Tail to Head: a lightweight value (graph and edge
// number). Edges are changed only through the graph (SetEdgeWeight, EdgeGroupWeights). Two Edge
// values are equal (==) if they refer to the same edge of the same graph.
type Edge struct {
	g  *Graph
	id int
}

// ID returns the number of the edge in its graph, in order of creation.
func (e Edge) ID() int { return e.id }

// Tail returns the start vertex.
func (e Edge) Tail() Vertex { return Vertex{e.g, int(e.g.edgeTail[e.id])} }

// Head returns the end vertex.
func (e Edge) Head() Vertex { return Vertex{e.g, int(e.g.edgeHead[e.id])} }

// Weight returns the weight; edges with a negative weight are ignored by the path search.
func (e Edge) Weight() int64 { return e.g.edgeWeight[e.id] }

// Group returns the group name (upper case), empty if the edge has no group.
func (e Edge) Group() string { return e.g.groups[e.g.edgeGroup[e.id]] }

// Delta returns the extra weight of the edge compared to the shortest path, for the last query:
// weight + distance(head) - distance(tail). Only meaningful if both distances are known.
func (e Edge) Delta() int64 { return e.Weight() + e.Head().Distance() - e.Tail().Distance() }

// HasArcFlag tells if the edge may be part of a shortest path into an arc-flags region. It
// returns true if the flag is set, if arc flags are not computed or if region is negative.
func (e Edge) HasArcFlag(region int) bool { return e.g.hasArcFlag(e.id, region) }

// String returns tail, weight and head, e.g. "A--5-->B".
func (e Edge) String() string {
	if e.g == nil {
		return "(none)"
	}
	return e.Tail().String() + "--" + strconv.FormatInt(e.Weight(), 10) + "-->" + e.Head().String()
}

// Path is a path: the sequence of edges from the start to the goal of a query. Weight is the
// total weight at the time the path was found. The zero value is the empty path ("no path").
type Path struct {
	g      *Graph
	edges  []int32
	weight int64
}

// Len returns the number of edges.
func (p Path) Len() int { return len(p.edges) }

// Edge returns the edge at a position (0 .. Len()-1).
func (p Path) Edge(i int) Edge { return Edge{p.g, int(p.edges[i])} }

// Edges returns the edges in order.
func (p Path) Edges() iter.Seq[Edge] {
	return func(yield func(Edge) bool) {
		for _, e := range p.edges {
			if !yield(Edge{p.g, int(e)}) {
				return
			}
		}
	}
}

// Vertices returns the vertices in order, from start to goal (none for an empty path).
func (p Path) Vertices() iter.Seq[Vertex] {
	return func(yield func(Vertex) bool) {
		if len(p.edges) == 0 || !yield(p.Edge(0).Tail()) {
			return
		}
		for e := range p.Edges() {
			if !yield(e.Head()) {
				return
			}
		}
	}
}

// IsValid returns false if the path is empty ("no path").
func (p Path) IsValid() bool { return len(p.edges) > 0 }

// Weight returns the sum of the edge weights.
func (p Path) Weight() int64 { return p.weight }

// VertexNames returns the comma-separated vertex labels, e.g. "HOME,PARK,OFFICE", or "(empty)".
func (p Path) VertexNames() string {
	if !p.IsValid() {
		return "(empty)"
	}
	var b strings.Builder
	for v := range p.Vertices() {
		if b.Len() > 0 {
			b.WriteByte(',')
		}
		b.WriteString(v.Label())
	}
	return b.String()
}

// String returns the vertex labels and the weight, e.g. "HOME,PARK,OFFICE (9)".
func (p Path) String() string {
	if !p.IsValid() {
		return "(empty)"
	}
	return p.VertexNames() + " (" + strconv.FormatInt(p.weight, 10) + ")"
}
