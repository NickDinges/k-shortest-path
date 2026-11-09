package eppstein

import (
	"bufio"
	"errors"
	"fmt"
	"io"
	"math"
	"strconv"
	"strings"
	"unicode"
)

// Bulk import from CSV and DIMACS files.

// ImportEdges adds edges from CSV text: one edge per line, "tail,head,weight" or
// "tail,head,weight,group". Vertices are created as needed (same label rules as
// CreateVertices). Empty lines and lines starting with '#' are skipped, spaces around values
// are ignored. Existing vertices and edges are kept.
//
// It returns the number of edges added. On an invalid line it returns an error naming the
// line number; the edges of the lines before it stay in the graph.
func (g *Graph) ImportEdges(r io.Reader) (int, error) {
	if r == nil {
		return 0, errors.New("eppstein: ImportEdges: nil reader")
	}
	scanner := bufio.NewScanner(r)
	count, lineNumber := 0, 0
	for scanner.Scan() {
		lineNumber++
		line := strings.TrimSpace(scanner.Text())
		if line == "" || line[0] == '#' {
			continue
		}
		fields := strings.Split(line, ",")
		if len(fields) < 3 || len(fields) > 4 {
			return count, importError(lineNumber, "expected tail,head,weight[,group] but found %d values", len(fields))
		}
		for i := range fields {
			fields[i] = strings.TrimSpace(fields[i])
		}
		tail := g.AddVertex(fields[0])
		if tail < 0 {
			return count, importError(lineNumber, "invalid vertex label %q", fields[0])
		}
		head := g.AddVertex(fields[1])
		if head < 0 {
			return count, importError(lineNumber, "invalid vertex label %q", fields[1])
		}
		weight, err := strconv.ParseInt(fields[2], 10, 64)
		if err != nil {
			return count, importError(lineNumber, "invalid weight %q", fields[2])
		}
		group := ""
		if len(fields) == 4 {
			group = fields[3]
		}
		g.AddEdgeGroup(tail, head, weight, group)
		count++
	}
	return count, scanner.Err()
}

// ImportDimacs adds a graph in DIMACS shortest path format (used by the DIMACS challenge road
// networks): "c comment", "p sp VERTICES EDGES" (once, before the edges) and
// "a TAIL HEAD WEIGHT" with vertex numbers 1 .. VERTICES. Vertex number n gets the label
// "V" + n (e.g. V1). Existing vertices and edges are kept.
//
// It returns the number of edges added. On an invalid line it returns an error naming the
// line number; the edges of the lines before it stay in the graph. A file that declares more
// than DefaultMaxImportVertices vertices is rejected; see ImportDimacsLimit.
func (g *Graph) ImportDimacs(r io.Reader) (int, error) {
	return g.ImportDimacsLimit(r, DefaultMaxImportVertices)
}

// DefaultMaxImportVertices is the default upper limit for the number of vertices a DIMACS file
// may declare (100 million; the largest DIMACS road network, USA, has about 24 million). The
// problem line creates all declared vertices at once, so without a limit a few bytes of a broken
// or hostile file could make the import allocate all memory.
const DefaultMaxImportVertices = 100_000_000

// ImportDimacsLimit is ImportDimacs with an own limit for the number of vertices the file may
// declare. It panics if maxVertices is negative.
func (g *Graph) ImportDimacsLimit(r io.Reader, maxVertices int) (int, error) {
	if maxVertices < 0 {
		panic("eppstein: ImportDimacsLimit: negative maxVertices")
	}
	if r == nil {
		return 0, errors.New("eppstein: ImportDimacs: nil reader")
	}
	scanner := bufio.NewScanner(r)
	var index []int // vertex number -> vertex index
	count, lineNumber := 0, 0
	for scanner.Scan() {
		lineNumber++
		line := scanner.Text()
		lineType, rest := nextField(line)
		if lineType == "" || lineType == "c" {
			continue
		}
		if lineType == "a" { // most frequent line: parsed without allocations
			if index == nil {
				return count, importError(lineNumber, "edge before the problem line 'p sp VERTICES EDGES'")
			}
			tailField, rest := nextField(rest)
			headField, rest := nextField(rest)
			weightField, rest := nextField(rest)
			extra, _ := nextField(rest)
			tail, err1 := strconv.Atoi(tailField)
			head, err2 := strconv.Atoi(headField)
			weight, err3 := strconv.ParseInt(weightField, 10, 64)
			if err1 != nil || err2 != nil || err3 != nil || extra != "" {
				return count, importError(lineNumber, "expected 'a TAIL HEAD WEIGHT'")
			}
			if tail < 1 || tail >= len(index) || head < 1 || head >= len(index) {
				return count, importError(lineNumber, "vertex number out of range 1..%d", len(index)-1)
			}
			g.AddEdgeGroup(index[tail], index[head], weight, "")
			count++
			continue
		}

		fields := strings.Fields(line) // same separators as nextField
		if len(fields) == 0 {
			continue
		}
		switch fields[0] {
		case "p":
			if index != nil {
				return count, importError(lineNumber, "more than one problem line")
			}
			if len(fields) != 4 || fields[1] != "sp" {
				return count, importError(lineNumber, "expected 'p sp VERTICES EDGES'")
			}
			declared, err1 := strconv.ParseUint(fields[2], 10, 64)
			_, err2 := strconv.ParseUint(fields[3], 10, 64)
			if err1 != nil || err2 != nil {
				return count, importError(lineNumber, "expected 'p sp VERTICES EDGES'")
			}
			limit := min(maxVertices, math.MaxInt32-1) // vertex indices are stored as int32
			if declared > uint64(limit) {
				return count, importError(lineNumber, "%d vertices exceed the limit of %d (see ImportDimacsLimit)", declared, limit)
			}
			vertices := int(declared)
			index = make([]int, vertices+1)
			for i := 1; i <= vertices; i++ {
				index[i] = g.AddVertex("V" + strconv.Itoa(i))
			}
		default:
			return count, importError(lineNumber, "unknown line type %q", fields[0])
		}
	}
	return count, scanner.Err()
}

// nextField returns the first field of s separated by white space (unicode.IsSpace, like
// strings.Fields), and the rest of s. The field shares memory with s (no allocation).
func nextField(s string) (field, rest string) {
	s = strings.TrimLeftFunc(s, unicode.IsSpace)
	if end := strings.IndexFunc(s, unicode.IsSpace); end >= 0 {
		return s[:end], s[end:]
	}
	return s, ""
}

// importError creates an import error with line number.
func importError(line int, format string, args ...interface{}) error {
	return fmt.Errorf("line %d: "+format, append([]interface{}{line}, args...)...)
}
