package eppstein

import (
	"fmt"
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

// BenchmarkBuildGraph measures CreateVertices and CreateEdges.
func BenchmarkBuildGraph(b *testing.B) {
	for _, size := range benchSizes() {
		b.Run(fmt.Sprintf("grid%d", size), func(b *testing.B) {
			for i := 0; i < b.N; i++ {
				gridGraph(size)
			}
		})
	}
}

// BenchmarkKShortestPaths measures FindShortestPath plus 10000 FindNextShortestPath calls.
func BenchmarkKShortestPaths(b *testing.B) {
	for _, size := range benchSizes() {
		g := gridGraph(size)
		target := fmt.Sprintf("V%dX%d", size-1, size-1)
		b.Run(fmt.Sprintf("grid%d_k10000", size), func(b *testing.B) {
			for i := 0; i < b.N; i++ {
				p := g.FindShortestPath("V0X0", target)
				for j := 1; j < 10000 && p.IsValid(); j++ {
					p = g.FindNextShortestPath()
				}
			}
		})
	}
}
