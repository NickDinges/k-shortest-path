// Command benchmark times the library on grid graphs, identical to the C# (Eppstein.Benchmarks)
// and Rust (rust/src/bin/benchmark.rs) programs: same generator (SplitMix64, seed 1), same options,
// same output, so the last column (weight of the last path) must match across the languages.
//
//	go run ./cmd/benchmark [size...] [--k N] [--local] [--ratio R] [--build strings|index|dimacs] [--two-way] [--simple] [--ch]
//
// Memory is the live heap after a garbage collection, after the first query.
package main

import (
	"fmt"
	"math"
	"os"
	"runtime"
	"strconv"
	"strings"
	"time"

	"github.com/nickdinges/k-shortest-path/go/eppstein"
)

// splitMix is SplitMix64, implemented identically in C#, Go and Rust.
type splitMix uint64

func (s *splitMix) next() uint64 {
	*s += 0x9E3779B97F4A7C15
	z := uint64(*s)
	z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9
	z = (z ^ (z >> 27)) * 0x94D049BB133111EB
	return z ^ (z >> 31)
}

func (s *splitMix) weight() int64 { return 1 + int64(s.next()%100) }

type edge struct {
	tail, head int
	weight     int64
}

func gridEdges(size int, twoWay bool) []edge {
	rnd := splitMix(1)
	var edges []edge
	for y := 0; y < size; y++ {
		for x := 0; x < size; x++ {
			v := y*size + x
			if x+1 < size {
				edges = append(edges, edge{v, v + 1, rnd.weight()})
			}
			if y+1 < size {
				edges = append(edges, edge{v, v + size, rnd.weight()})
			}
			if x > 0 && v%10 == 0 {
				edges = append(edges, edge{v, v - 1, rnd.weight()})
			}
		}
	}
	if twoWay { // every street in both directions (the back edges are dropped)
		var oneWay []edge
		for _, e := range edges {
			if e.head > e.tail {
				oneWay = append(oneWay, e)
			}
		}
		edges = append([]edge(nil), oneWay...)
		for _, e := range oneWay {
			edges = append(edges, edge{e.head, e.tail, e.weight})
		}
	}
	return edges
}

type options struct {
	k                         int
	local, twoWay, simple, ch bool
	ratio                     float64
	build                     string
}

func ms(start time.Time) float64 { return float64(time.Since(start).Nanoseconds()) / 1e6 }

func liveHeap() uint64 {
	runtime.GC()
	var m runtime.MemStats
	runtime.ReadMemStats(&m)
	return m.HeapAlloc
}

func run(size int, o options) {
	edges := gridEdges(size, o.twoWay)
	labels := make([]string, size*size)
	for v := range labels {
		labels[v] = fmt.Sprintf("V%dX%d", v%size, v/size)
	}
	vertexList := strings.Join(labels, ",")
	var dimacs string
	if o.build == "dimacs" {
		var b strings.Builder
		fmt.Fprintf(&b, "p sp %d %d\n", len(labels), len(edges))
		for _, e := range edges {
			fmt.Fprintf(&b, "a %d %d %d\n", e.tail+1, e.head+1, e.weight)
		}
		dimacs = b.String()
	}

	memoryBefore := liveHeap()
	start := time.Now()
	g := eppstein.NewGraph()
	switch o.build {
	case "strings":
		g.CreateVertices(vertexList)
		for _, e := range edges {
			g.CreateEdges(labels[e.tail], labels[e.head], e.weight, "g")
		}
	case "index":
		for _, l := range labels {
			g.AddVertex(l)
		}
		for _, e := range edges {
			g.AddEdgeGroup(e.tail, e.head, e.weight, "g")
		}
	case "dimacs":
		if _, err := g.ImportDimacs(strings.NewReader(dimacs)); err != nil {
			panic(err)
		}
	default:
		panic("unknown --build " + o.build)
	}
	buildTime := ms(start)

	if o.ch {
		g.EnableContractionHierarchy()
		start = time.Now()
		g.PrecomputeContractionHierarchy()
		fmt.Fprintf(os.Stderr, "%dx%d: contraction %.0f ms, %d shortcuts\n", size, size, ms(start), g.ContractionHierarchyShortcuts())
	}

	start = time.Now()
	g.SetMaxDetourRatio(o.ratio)
	g.SetSimplePathsOnly(o.simple)
	from, to := 0, size-1
	if o.local {
		from, to = size/2, size/2+max(size/20, 1)
	}
	g.FindShortestPathByIndex(from*size+from, to*size+to)
	first := ms(start)
	memory := float64(liveHeap()-min(liveHeap(), memoryBefore)) / 1e6

	start = time.Now()
	p := g.FindShortestPathByIndex(from*size+from, to*size+to)
	repeated := ms(start)

	start = time.Now()
	last, found := p.Weight(), 0
	if p.IsValid() {
		found = 1
	}
	for i := 1; i < o.k && p.IsValid(); i++ {
		p = g.FindNextShortestPath()
		if p.IsValid() {
			last = p.Weight()
			found++
		}
	}
	next := ms(start)
	fmt.Printf("| %dx%d | %d | %d | %.1f ms | %.1f MB | %.1f ms | %.1f ms | %.1f ms | %d | %d |\n",
		size, size, size*size, len(edges), buildTime, memory, first, repeated, next, found, last)
	runtime.KeepAlive(g)
}

func main() {
	o := options{k: 10000, ratio: math.Inf(1), build: "strings"}
	var sizes []int
	args := os.Args[1:]
	for i := 0; i < len(args); i++ {
		switch args[i] {
		case "--k":
			i++
			o.k, _ = strconv.Atoi(args[i])
		case "--local":
			o.local = true
		case "--two-way":
			o.twoWay = true
		case "--simple":
			o.simple = true
		case "--ch":
			o.ch = true
		case "--build":
			i++
			o.build = args[i]
		case "--ratio":
			i++
			o.ratio, _ = strconv.ParseFloat(args[i], 64)
		default:
			size, err := strconv.Atoi(args[i])
			if err != nil {
				panic("unknown option " + args[i])
			}
			sizes = append(sizes, size)
		}
	}
	if len(sizes) == 0 {
		sizes = []int{50, 100, 200}
	}
	header := "Corner to corner"
	if o.local {
		header = "Local query (nearby vertices in the middle of the grid)"
	}
	if !math.IsInf(o.ratio, 1) {
		header += ", MaxDetourRatio = " + strconv.FormatFloat(o.ratio, 'g', -1, 64)
	}
	header += ", build: " + o.build
	if o.twoWay {
		header += ", two-way streets"
	}
	if o.simple {
		header += ", SimplePathsOnly"
	}
	if o.ch {
		header += ", contraction hierarchy"
	}
	fmt.Println(header)
	fmt.Println("| grid | vertices | edges | build graph | memory | first path | first path (repeated query) | next paths | paths found | weight of last path |")
	fmt.Println("|---|---|---|---|---|---|---|---|---|---|")
	for _, size := range sizes {
		run(size, o)
	}
}
