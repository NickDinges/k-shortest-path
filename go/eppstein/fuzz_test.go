package eppstein

import (
	"fmt"
	"slices"
	"strings"
	"testing"
)

// Fuzz tests: run one with e.g.
//
//	go test -run '^$' -fuzz '^FuzzImportDimacs$' -fuzztime 1m ./eppstein
//
// Without -fuzz, "go test" runs the seed inputs below and the corpus in testdata/fuzz as
// normal tests.

// FuzzImportDimacs: any input returns a graph or an error, never a panic, and a successful import
// gives a consistent graph that can be queried.
func FuzzImportDimacs(f *testing.F) {
	for _, seed := range []string{
		"p sp 3 2\na 1 2 5\na 2 3 5\n",
		"c comment\np sp 2 1\na 1 2 -1\n",
		"p sp 2147483647 1\na 1 2 3\n",
		"p sp 2 1\na 1 3 1\n",
		"a 1 2 3\n",
		"p sp 1 0\n\n\t\n",
	} {
		f.Add(seed)
	}
	f.Fuzz(func(t *testing.T, data string) {
		g := NewGraph()
		count, err := g.ImportDimacsLimit(strings.NewReader(data), 1000) // small limit: fast fuzzing
		if err != nil {
			if !strings.HasPrefix(err.Error(), "line ") {
				t.Fatalf("error without line number: %v", err)
			}
			return
		}
		checkImportedGraph(t, g, count)
	})
}

// FuzzImportEdges: like FuzzImportDimacs, for the CSV import.
func FuzzImportEdges(f *testing.F) {
	for _, seed := range []string{
		"A,B,1\nB,C,2,g\n",
		"# comment\n home , park , 2 \n",
		"A,B\n",
		"A,1B,1\n",
		"A,B,x\n",
		"A,B,9223372036854775807\nB,A,-9223372036854775808\n",
	} {
		f.Add(seed)
	}
	f.Fuzz(func(t *testing.T, data string) {
		g := NewGraph()
		count, err := g.ImportEdges(strings.NewReader(data))
		if err != nil {
			if !strings.HasPrefix(err.Error(), "line ") && !strings.Contains(err.Error(), "token too long") {
				t.Fatalf("error without line number: %v", err)
			}
			return
		}
		checkImportedGraph(t, g, count)
	})
}

// checkImportedGraph checks that an imported graph is consistent and can be queried.
func checkImportedGraph(t *testing.T, g *Graph, count int) {
	t.Helper()
	if count != g.EdgeCount() {
		t.Fatalf("import reported %d edges, graph has %d", count, g.EdgeCount())
	}
	for e := range g.Edges() {
		if e.Tail().Index() >= g.VertexCount() || e.Head().Index() >= g.VertexCount() {
			t.Fatalf("edge %v refers to a missing vertex", e)
		}
	}
	if g.VertexCount() == 0 {
		return
	}
	// Weights may be huge (sums overflow), so only check that querying does not panic.
	n := 0
	for p := range g.PathsByIndex(0, g.VertexCount()-1) {
		if p.Len() == 0 {
			t.Fatal("empty path from the iterator")
		}
		if n++; n == 5 {
			break
		}
	}
}

// FuzzKShortestPaths builds a small graph (with cycles) from the fuzz input and compares the k
// shortest paths with a brute-force enumeration of all walks up to a maximum weight.
func FuzzKShortestPaths(f *testing.F) {
	f.Add([]byte{3, 0, 1, 0, 1, 5, 1, 2, 3, 0, 2, 9})
	f.Add([]byte{5, 1, 0, 0, 1, 1, 1, 0, 1, 1, 2, 2, 2, 3, 1, 3, 1, 4})
	f.Add([]byte{6, 2, 7, 0, 1, 2, 1, 2, 2, 2, 0, 2, 2, 3, 2, 3, 4, 2, 4, 5, 2})
	f.Fuzz(func(t *testing.T, data []byte) {
		if len(data) < 3 {
			return
		}
		n := 2 + int(data[0])%6
		options := data[1]
		s, target := int(data[2])%n, int(data[2]/16)%n
		if s == target {
			target = (s + 1) % n
		}
		var edges []testEdge
		for i := 3; i+2 < len(data) && len(edges) < 16; i += 3 {
			edges = append(edges, testEdge{int(data[i]) % n, int(data[i+1]) % n, 1 + int64(data[i+2])%9})
		}

		labels := make([]string, n)
		g := NewGraph()
		for i := range labels {
			labels[i] = fmt.Sprintf("N%d", i)
			g.AddVertex(labels[i])
		}
		for _, e := range edges {
			g.AddEdge(e.from, e.to, e.weight)
		}
		if options&1 != 0 {
			g.EnableArcFlags(1 + int(options/2)%3)
		}
		const maxWeight = 25
		limit := int64(maxWeight)
		if options&8 != 0 { // corridor
			g.SetMaxDetour(int64(options/16) % 10)
		}

		expected, ok := boundedWalkWeights(edges, s, target, limit, 200000)
		if !ok || len(expected) > 5000 {
			return // too many walks: keep every fuzz input fast
		}
		if len(expected) > 0 && g.hasMaxDetour {
			bound := expected[0] + g.MaxDetour()
			var within []int64
			for _, w := range expected {
				if w <= bound {
					within = append(within, w)
				}
			}
			expected = within
		}

		var actual []int64
		for p := range g.PathsByIndex(s, target) {
			assertValidPath(t, p, labels[s], labels[target])
			if p.Weight() > limit {
				break
			}
			actual = append(actual, p.Weight())
			if len(actual) > len(expected) {
				break
			}
		}
		if !equalInts(actual, expected) {
			t.Fatalf("weights %v, want %v (n=%d, edges=%v, s=%d, t=%d, options=%d)", actual, expected, n, edges, s, target, options)
		}
	})
}

// boundedWalkWeights returns the sorted weights of all s-t walks up to maxWeight, or ok=false if
// the enumeration would visit more than maxStates states.
func boundedWalkWeights(edges []testEdge, s, t int, maxWeight int64, maxStates int) (weights []int64, ok bool) {
	type state struct {
		vertex int
		weight int64
	}
	stack := []state{{s, 0}}
	for visited := 0; len(stack) > 0; visited++ {
		if visited > maxStates {
			return nil, false
		}
		cur := stack[len(stack)-1]
		stack = stack[:len(stack)-1]
		if cur.vertex == t && cur.weight > 0 {
			weights = append(weights, cur.weight)
		}
		for _, e := range edges {
			if e.from == cur.vertex && cur.weight+e.weight <= maxWeight {
				stack = append(stack, state{e.to, cur.weight + e.weight})
			}
		}
	}
	slices.Sort(weights)
	return weights, true
}
