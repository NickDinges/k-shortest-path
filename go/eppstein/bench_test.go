package eppstein

import (
	"fmt"
	"math"
	"math/rand"
	"strings"
	"testing"
)

// gridGraph builds a size x size grid: edges to the right and lower neighbor
// (random weight 1..100) and every 10th vertex an edge back to its left neighbor.
func gridGraph(size int) *Graph {
	rnd := rand.New(rand.NewSource(1))
	name := func(x, y int) string { return fmt.Sprintf("V%dX%d", x, y) }
	var vertices strings.Builder
	for y := 0; y < size; y++ {
		for x := 0; x < size; x++ {
			vertices.WriteString(name(x, y) + ",")
		}
	}
	g := NewGraph()
	g.CreateVertices(vertices.String())
	for y := 0; y < size; y++ {
		for x := 0; x < size; x++ {
			if x+1 < size {
				g.CreateEdges(name(x, y), name(x+1, y), int64(1+rnd.Intn(100)), "g")
			}
			if y+1 < size {
				g.CreateEdges(name(x, y), name(x, y+1), int64(1+rnd.Intn(100)), "g")
			}
			if x > 0 && (x+y*size)%10 == 0 {
				g.CreateEdges(name(x, y), name(x-1, y), int64(1+rnd.Intn(100)), "g")
			}
		}
	}
	return g
}

func benchSizes() []int { return []int{50, 100, 200} }

// BenchmarkBuildGraph measures building a grid graph in three ways: CreateVertices and
// CreateEdges with labels, AddVertex and AddEdge by index, and ImportDimacs.
func BenchmarkBuildGraph(b *testing.B) {
	for _, size := range []int{200, 1000} {
		labels, edges := gridEdges(size)
		b.Run(fmt.Sprintf("grid%d_strings", size), func(b *testing.B) {
			for i := 0; i < b.N; i++ {
				g := NewGraph()
				g.CreateVertices(strings.Join(labels, ","))
				for _, e := range edges {
					g.CreateEdges(labels[e.from], labels[e.to], e.weight, "g")
				}
			}
		})
		b.Run(fmt.Sprintf("grid%d_index", size), func(b *testing.B) {
			for i := 0; i < b.N; i++ {
				g := NewGraph()
				for _, l := range labels {
					g.AddVertex(l)
				}
				for _, e := range edges {
					g.AddEdgeGroup(e.from, e.to, e.weight, "g")
				}
			}
		})
		var dimacs strings.Builder
		fmt.Fprintf(&dimacs, "p sp %d %d\n", len(labels), len(edges))
		for _, e := range edges {
			fmt.Fprintf(&dimacs, "a %d %d %d\n", e.from+1, e.to+1, e.weight)
		}
		text := dimacs.String()
		b.Run(fmt.Sprintf("grid%d_dimacs", size), func(b *testing.B) {
			for i := 0; i < b.N; i++ {
				if _, err := NewGraph().ImportDimacs(strings.NewReader(text)); err != nil {
					b.Fatal(err)
				}
			}
		})
	}
}

// gridEdges returns the labels and edges (by vertex index y*size+x) of the benchmark grid.
func gridEdges(size int) ([]string, []testEdge) {
	rnd := rand.New(rand.NewSource(1))
	labels := make([]string, size*size)
	var edges []testEdge
	for y := 0; y < size; y++ {
		for x := 0; x < size; x++ {
			v := y*size + x
			labels[v] = fmt.Sprintf("V%dX%d", x, y)
			if x+1 < size {
				edges = append(edges, testEdge{v, v + 1, int64(1 + rnd.Intn(100))})
			}
			if y+1 < size {
				edges = append(edges, testEdge{v, v + size, int64(1 + rnd.Intn(100))})
			}
			if x > 0 && v%10 == 0 {
				edges = append(edges, testEdge{v, v - 1, int64(1 + rnd.Intn(100))})
			}
		}
	}
	return labels, edges
}

// BenchmarkKShortestPaths measures FindShortestPath plus 10000 FindNextShortestPath calls
// from corner to corner.
func BenchmarkKShortestPaths(b *testing.B) {
	for _, size := range benchSizes() {
		g := gridGraph(size)
		target := fmt.Sprintf("V%dX%d", size-1, size-1)
		b.Run(fmt.Sprintf("grid%d_k10000", size), func(b *testing.B) {
			for i := 0; i < b.N; i++ {
				kPaths(g, "V0X0", target, 10000)
			}
		})
	}
}

// BenchmarkLocalQuery measures 10000 paths between two nearby vertices in the middle of a
// 1000x1000 grid (1 million vertices).
func BenchmarkLocalQuery(b *testing.B) {
	const size = 1000
	g := gridGraph(size)
	from := fmt.Sprintf("V%dX%d", size/2, size/2)
	to := fmt.Sprintf("V%dX%d", size/2+size/20, size/2+size/20)
	b.Run("grid1000_k10000", func(b *testing.B) {
		for i := 0; i < b.N; i++ {
			kPaths(g, from, to, 10000)
		}
	})
	// Same query with paths at most 10% longer than the shortest one (corridor)
	b.Run("grid1000_k10000_ratio0.1", func(b *testing.B) {
		if !setRatio(g, 0.1) {
			b.Skip("corridor not supported")
		}
		defer setRatio(g, math.Inf(1))
		for i := 0; i < b.N; i++ {
			kPaths(g, from, to, 10000)
		}
	})
}

// setRatio sets MaxDetourRatio if the graph supports it (benchmarks also run on older versions).
func setRatio(g interface{}, ratio float64) bool {
	if s, ok := g.(interface{ SetMaxDetourRatio(float64) bool }); ok {
		return s.SetMaxDetourRatio(ratio)
	}
	return false
}

func kPaths(g *Graph, s, t string, k int) {
	p := g.FindShortestPath(s, t)
	for j := 1; j < k && p.IsValid(); j++ {
		p = g.FindNextShortestPath()
	}
}
