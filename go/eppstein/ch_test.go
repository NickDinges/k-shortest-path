package eppstein

import (
	"fmt"
	"math"
	"math/rand"
	"slices"
	"testing"
)

// collect returns the weights of up to k paths.
func collect(g *Graph, s, t, k int) []int64 {
	var result []int64
	for p := range g.PathsByIndex(s, t) {
		result = append(result, p.Weight())
		if len(result) == k {
			break
		}
	}
	return result
}

func distances(g *Graph) []int64 {
	var result []int64
	for v := range g.Vertices() {
		result = append(result, v.Distance())
	}
	return result
}

// TestContractionHierarchyMatchesDijkstra: random graphs (zero, negative and parallel edges,
// self-loops, unreachable vertices); PHAST gives every vertex the same distance as Dijkstra, a
// consistent tree, and the same path weights.
func TestContractionHierarchyMatchesDijkstra(t *testing.T) {
	rnd := rand.New(rand.NewSource(7))
	for it := 0; it < 150; it++ {
		n := 1 + rnd.Intn(119)
		m := rnd.Intn(4 * n)
		plain, ch := NewGraph(), NewGraph()
		for v := range n {
			plain.AddVertex(fmt.Sprintf("V%d", v))
			ch.AddVertex(fmt.Sprintf("V%d", v))
		}
		for range m {
			a, b := rnd.Intn(n), rnd.Intn(n)
			var w int64
			switch kind := rnd.Intn(10); kind {
			case 0:
				w = 0
			case 1:
				w = -1
			default:
				w = 1 + rnd.Int63n(19)
			}
			plain.AddEdge(a, b, w)
			ch.AddEdge(a, b, w)
		}
		ch.EnableContractionHierarchy()
		if !ch.PrecomputeContractionHierarchy() {
			t.Fatal("hierarchy not computed")
		}
		for range 5 {
			s, target := rnd.Intn(n), rnd.Intn(n)
			if expected, actual := collect(plain, s, target, 20), collect(ch, s, target, 20); !equalInts(expected, actual) {
				t.Fatalf("weights %v, want %v", actual, expected)
			}
			if expected, actual := distances(plain), distances(ch); !slices.Equal(expected, actual) {
				t.Fatalf("distances %v, want %v", actual, expected)
			}
			for v := range ch.Vertices() {
				if e, ok := v.TreeEdge(); ok && v.Distance() != e.Weight()+e.Head().Distance() {
					t.Fatalf("tree edge %v not tight", e)
				}
			}
		}
	}
}

func TestContractionHierarchyFollowsChangesAndCorridorUsesDijkstra(t *testing.T) {
	g := eppsteinExample()
	if g.ContractionHierarchyEnabled() || g.PrecomputeContractionHierarchy() {
		t.Fatal("enabled by default")
	}
	g.EnableContractionHierarchy()
	if !g.ContractionHierarchyEnabled() || g.ContractionHierarchyShortcuts() != 0 {
		t.Fatal("not enabled, or computed too early")
	}
	same := func(h *Graph, s, target string) {
		t.Helper()
		if a, b := weights(take(g, s, target, 30)), weights(take(h, s, target, 30)); !equalInts(a, b) {
			t.Fatalf("weights %v, want %v", a, b)
		}
	}
	h := eppsteinExample()
	same(h, "S", "T")

	h.EdgeGroupWeights("beta", 1)
	g.EdgeGroupWeights("beta", 1) // hierarchy must be recomputed
	same(h, "S", "T")
	for _, x := range []*Graph{g, h} {
		x.AddVertex("X")
		x.AddEdge(x.VertexIndex("S"), x.VertexIndex("X"), 1)
	}
	same(h, "S", "X")

	h.SetMaxDetourRatio(0.5)
	g.SetMaxDetourRatio(0.5) // corridor: Dijkstra, same results
	same(h, "S", "T")
	for _, x := range []*Graph{g, h} {
		x.SetMaxDetourRatio(math.Inf(1))
		x.SetSimplePathsOnly(true)
	}
	same(h, "S", "T")

	g.DisableContractionHierarchy()
	if g.ContractionHierarchyEnabled() || g.ContractionHierarchyShortcuts() != 0 {
		t.Fatal("still enabled")
	}
	same(h, "S", "T")
}

func TestContractionHierarchyGridNeedsShortcuts(t *testing.T) {
	const size = 12
	grid := func() *Graph {
		g := NewGraph()
		for i := range size * size {
			g.AddVertex(fmt.Sprintf("V%d", i))
		}
		rnd := rand.New(rand.NewSource(3))
		for r := range size {
			for c := range size {
				v := r*size + c
				if c+1 < size {
					g.AddEdge(v, v+1, 1+rnd.Int63n(9))
					g.AddEdge(v+1, v, 1+rnd.Int63n(9))
				}
				if r+1 < size {
					g.AddEdge(v, v+size, 1+rnd.Int63n(9))
					g.AddEdge(v+size, v, 1+rnd.Int63n(9))
				}
			}
		}
		return g
	}
	plain, g := grid(), grid()
	g.EnableContractionHierarchy()
	if !g.PrecomputeContractionHierarchy() || g.ContractionHierarchyShortcuts() == 0 {
		t.Fatal("no shortcuts")
	}
	if a, b := collect(g, 0, size*size-1, 50), collect(plain, 0, size*size-1, 50); !equalInts(a, b) {
		t.Fatalf("weights %v, want %v", a, b)
	}
}
