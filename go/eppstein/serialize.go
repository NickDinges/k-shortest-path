package eppstein

import (
	"errors"
	"io"
	"slices"
	"strconv"
	"strings"
)

// highlightColors are used for highlighted paths, in order (repeated if more paths are given).
var highlightColors = []string{"red", "blue", "darkgreen", "orange", "purple", "brown", "magenta", "cyan4"}

// Serialize returns the graph in Graphviz DOT format.
//
// Render the output with Graphviz, e.g. "dot -Tsvg graph.dot -o graph.svg".
// Edges are labeled with their weight, negative edges (ignored by the algorithm) are dashed.
// After FindShortestPath the source and endpoint are drawn as double circles, vertices show
// their distance to the endpoint and shortest path tree edges are bold.
// When arc flags are computed, every region is drawn as a cluster.
func (g *Graph) Serialize() string {
	return g.SerializePaths(nil)
}

// SerializePath returns the graph in Graphviz DOT format with one path highlighted.
func (g *Graph) SerializePath(highlight Path) string {
	return g.SerializePaths([]Path{highlight})
}

// SerializePaths returns the graph in Graphviz DOT format with several paths highlighted,
// each one in a different color. highlight may be nil.
func (g *Graph) SerializePaths(highlight []Path) string {
	var b strings.Builder
	_ = g.SerializeTo(&b, highlight) // strings.Builder never fails
	return b.String()
}

// SerializeTo writes the graph in Graphviz DOT format to w, highlighting the given paths.
func (g *Graph) SerializeTo(w io.Writer, highlight []Path) error {
	if w == nil {
		return errors.New("eppstein: SerializeTo: nil writer")
	}

	g.ensureAdjacency()
	// Colors of every highlighted edge, one entry per path using it
	edgeColors := map[int][]string{}
	highlighted := map[int]bool{}
	index := 0
	for _, p := range highlight {
		if !p.IsValid() {
			continue
		}
		color := highlightColors[index%len(highlightColors)]
		index++
		for _, e := range p.edges {
			if !slices.Contains(edgeColors[int(e)], color) { // a path may cross an edge several times
				edgeColors[int(e)] = append(edgeColors[int(e)], color)
			}
			highlighted[int(g.edgeTail[e])] = true
			highlighted[int(g.edgeHead[e])] = true
		}
	}

	var b strings.Builder
	b.WriteString("digraph Eppstein {\n")
	b.WriteString("    rankdir=LR;\n")
	b.WriteString("    node [shape=circle];\n")

	// Vertices, grouped by arc-flags region if available
	if g.arcFlagsEnabled && g.arcFlagsValid && g.regionCount > 0 {
		for r := 0; r < g.regionCount; r++ {
			b.WriteString("    subgraph cluster_" + strconv.Itoa(r) + " {\n")
			b.WriteString("        label=\"Region " + strconv.Itoa(r) + "\";\n")
			b.WriteString("        style=dashed;\n")
			for v := range g.labels {
				if int(g.vertexRegion[v]) == r {
					b.WriteString("        " + g.vertexStatement(v, highlighted) + "\n")
				}
			}
			b.WriteString("    }\n")
		}
	} else {
		for v := range g.labels {
			b.WriteString("    " + g.vertexStatement(v, highlighted) + "\n")
		}
	}

	// Edges, grouped by tail in vertex order
	for v := range g.labels {
		for _, e := range g.outEdges[g.outStart[v]:g.outStart[v+1]] {
			attributes := []string{
				"label=" + quote(strconv.FormatInt(g.edgeWeight[e], 10)),
				"tooltip=" + quote("group "+g.groups[g.edgeGroup[e]]),
			}
			var styles []string
			if g.edgeWeight[e] < 0 {
				styles = append(styles, "dashed")
			}
			if g.ready && g.distanceOf(v) != Unreached && g.treeEdge[v] == e { // shortest path tree
				styles = append(styles, "bold")
			}
			if colors, ok := edgeColors[int(e)]; ok {
				attributes = append(attributes,
					"color="+quote(strings.Join(colors, ":")),
					"fontcolor="+quote(colors[0]),
					"penwidth=2")
			}
			if len(styles) > 0 {
				attributes = append(attributes, "style="+quote(strings.Join(styles, ",")))
			}
			b.WriteString("    " + quote(g.labels[v]) + " -> " + quote(g.labels[g.edgeHead[e]]) +
				" [" + strings.Join(attributes, ", ") + "];\n")
		}
	}
	b.WriteString("}\n")

	_, err := io.WriteString(w, b.String())
	return err
}

// vertexStatement builds the DOT statement of a vertex.
func (g *Graph) vertexStatement(v int, highlighted map[int]bool) string {
	label := g.labels[v]
	if g.ready && g.distanceOf(v) != Unreached { // distance to the goal is known
		label += "\n" + strconv.FormatInt(g.distance[v], 10)
	}
	attributes := []string{"label=" + quote(label)}
	if g.ready && (v == g.source || v == g.target) {
		attributes = append(attributes, "shape=doublecircle")
	}
	if highlighted[v] {
		attributes = append(attributes, "penwidth=2")
	}
	return quote(g.labels[v]) + " [" + strings.Join(attributes, ", ") + "];"
}

// quote quotes and escapes a DOT identifier or string.
func quote(text string) string {
	var b strings.Builder
	b.WriteByte('"')
	for _, c := range text {
		switch c {
		case '"', '\\':
			b.WriteByte('\\')
			b.WriteRune(c)
		case '\n':
			b.WriteString(`\n`)
		default:
			b.WriteRune(c)
		}
	}
	b.WriteByte('"')
	return b.String()
}
