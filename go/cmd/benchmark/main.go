// Command benchmark times the library on grid graphs, identical to the C# (csharp/Eppstein.Benchmarks)
// and Rust (rust/src/bin/benchmark.rs) programs: same generator (SplitMix64, seed 1), same options,
// same output, so the last column (weight of the last path) must match across the languages.
//
//	go run ./cmd/benchmark [size...] [--k N] [--local] [--ratio R] [--build strings|index|dimacs] [--two-way] [--simple] [--ch] [--arc-flags N] [--reps N] [--weights]
//
// --reps N repeats the first-path query N more times in the same process (steady state), --weights
// enumerates only the weights of the next paths, --arc-flags N enables arc flags with N regions.
// A "stats:" line on standard error reports the steady-state times and the garbage collections,
// their pause time and the bytes allocated while the next paths were enumerated.
//
// Memory is the live heap after a garbage collection, after the first query.
package main

import (
	"fmt"
	"math"
	"os"
	"runtime"
	"slices"
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
	arcFlags, reps            int
	weights                   bool
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

	arcFlagsTime := 0.0
	if o.arcFlags > 0 {
		g.EnableArcFlags(o.arcFlags)
		start = time.Now()
		g.PrecomputeArcFlags()
		arcFlagsTime = ms(start)
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

	// Steady state: the same query again, reps times
	warm := []float64{repeated}
	for i := 0; i < o.reps; i++ {
		start = time.Now()
		p = g.FindShortestPathByIndex(from*size+from, to*size+to)
		if i == 0 {
			warm = warm[:0]
		}
		warm = append(warm, ms(start))
	}
	slices.Sort(warm)

	var before, after runtime.MemStats
	runtime.ReadMemStats(&before)
	last, found, next := p.Weight(), 0, 0.0
	if o.weights {
		// Weights without path objects: a new enumeration; the clock starts at its first result
		for weight := range g.PathWeightsByIndex(from*size+from, to*size+to) {
			if found == 0 {
				start = time.Now()
			}
			last = weight
			if found++; found >= o.k {
				break
			}
		}
		if found > 0 {
			next = ms(start)
		}
	} else {
		start = time.Now()
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
		next = ms(start)
	}
	runtime.ReadMemStats(&after)
	fmt.Fprintf(os.Stderr, "stats: warm_median_ms=%.3f warm_min_ms=%.3f gc_count=%d gc_pause_ms=%.2f alloc_mb=%.1f arcflags_ms=%.0f\n",
		warm[len(warm)/2], warm[0], after.NumGC-before.NumGC, float64(after.PauseTotalNs-before.PauseTotalNs)/1e6,
		float64(after.TotalAlloc-before.TotalAlloc)/1e6, arcFlagsTime)
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
		case "--weights":
			o.weights = true
		case "--arc-flags":
			i++
			o.arcFlags, _ = strconv.Atoi(args[i])
		case "--reps":
			i++
			o.reps, _ = strconv.Atoi(args[i])
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
	if o.arcFlags > 0 {
		header += fmt.Sprintf(", arc flags (%d regions)", o.arcFlags)
	}
	if o.weights {
		header += ", weights only"
	}
	fmt.Println(header)
	fmt.Println("| grid | vertices | edges | build graph | memory | first path | first path (repeated query) | next paths | paths found | weight of last path |")
	fmt.Println("|---|---|---|---|---|---|---|---|---|---|")
	for _, size := range sizes {
		run(size, o)
	}
}
