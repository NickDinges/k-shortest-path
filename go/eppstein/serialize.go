package eppstein

import (
	"errors"
	"io"
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

	// Colors of every highlighted edge, one entry per path using it
	edgeColors := map[*Edge][]string{}
	highlighted := map[*Vertex]bool{}
	index := 0
	for _, p := range highlight {
		if !p.IsValid() {
			continue
		}
		color := highlightColors[index%len(highlightColors)]
		index++
		for _, e := range p {
			if !contains(edgeColors[e], color) { // path crossing an edge several times
				edgeColors[e] = append(edgeColors[e], color)
			}
			highlighted[e.Tail] = true
			highlighted[e.Head] = true
		}
	}

	var b strings.Builder
	b.WriteString("digraph Eppstein {\n")
	b.WriteString("    rankdir=LR;\n")
	b.WriteString("    node [shape=circle];\n")

	// Vertices, grouped by arc-flags region if available
	regions := g.arcFlags.enabled && g.arcFlags.valid && g.arcFlags.regionCount > 0
	if regions {
		for r := 0; r < g.arcFlags.regionCount; r++ {
			b.WriteString("    subgraph cluster_" + strconv.Itoa(r) + " {\n")
			b.WriteString("        label=\"Region " + strconv.Itoa(r) + "\";\n")
			b.WriteString("        style=dashed;\n")
			for _, v := range g.vertices {
				if v.Region == r {
					b.WriteString("        " + g.vertexStatement(v, highlighted) + "\n")
				}
			}
			b.WriteString("    }\n")
		}
	}
	for _, v := range g.vertices {
		if !regions || v.Region < 0 || v.Region >= g.arcFlags.regionCount {
			b.WriteString("    " + g.vertexStatement(v, highlighted) + "\n")
		}
	}

	// Edges, each one written once (from its tail)
	for _, v := range g.vertices {
		for _, e := range v.RelatedEdges {
			if e.Tail != v {
				continue
			}
			attributes := []string{
				"label=" + quote(strconv.FormatInt(e.Weight, 10)),
				"tooltip=" + quote("group "+e.Group),
			}
			var styles []string
			if e.Weight < 0 {
				styles = append(styles, "dashed")
			}
			if g.ready && e.Tail.EdgeToPath == e { // shortest path tree edge
				styles = append(styles, "bold")
			}
			if colors, ok := edgeColors[e]; ok {
				attributes = append(attributes,
					"color="+quote(strings.Join(colors, ":")),
					"fontcolor="+quote(colors[0]),
					"penwidth=2")
			}
			if len(styles) > 0 {
				attributes = append(attributes, "style="+quote(strings.Join(styles, ",")))
			}
			b.WriteString("    " + quote(e.Tail.String()) + " -> " + quote(e.Head.String()) +
				" [" + strings.Join(attributes, ", ") + "];\n")
		}
	}
	b.WriteString("}\n")

	_, err := io.WriteString(w, b.String())
	return err
}

// vertexStatement builds the DOT statement of a vertex.
func (g *Graph) vertexStatement(v *Vertex, highlighted map[*Vertex]bool) string {
	label := v.String()
	if g.ready && v.Distance != Unreached { // distance to endpoint is known
		label += "\n" + strconv.FormatInt(v.Distance, 10)
	}
	attributes := []string{"label=" + quote(label)}
	if g.ready && (v == g.sourceVertex || v == g.endVertex) {
		attributes = append(attributes, "shape=doublecircle")
	}
	if highlighted[v] {
		attributes = append(attributes, "penwidth=2")
	}
	return quote(v.String()) + " [" + strings.Join(attributes, ", ") + "];"
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

func contains(list []string, s string) bool {
	for _, x := range list {
		if x == s {
			return true
		}
	}
	return false
}
