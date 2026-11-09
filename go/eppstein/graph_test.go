package eppstein

import (
	"fmt"
	"math"
	"math/rand"
	"sort"
	"strings"
	"testing"
)

func eppsteinExample() *Graph {
	g := NewGraph()
	g.CreateVertices("S,A,B,C,D,E,F,G,H,I,J,K,T")
	g.CreateEdges("S", "A", 2, "alpha")
	g.CreateEdges("A,E", "B,I", 20, "alpha")
	g.CreateEdges("B,B", "C,F", 14, "alpha")
	g.CreateEdges("D", "E", 9, "alpha")
	g.CreateEdges("E", "F", 10, "alpha")
	g.CreateEdges("F", "G", 25, "alpha")
	g.CreateEdges("H", "I", 18, "alpha")
	g.CreateEdges("I", "J", 8, "alpha")
	g.CreateEdges("J", "T", 11, "alpha")
	g.CreateEdges("S", "D", 13, "alpha")
	g.CreateEdges("A", "E", 27, "alpha")
	g.CreateEdges("C,D", "G,H", 15, "alpha")
	g.CreateEdges("F", "J", 12, "alpha")
	g.CreateEdges("G", "T", 7, "alpha")
	g.CreateEdges("E", "J", 30, "beta")
	g.CreateEdges("F", "T", 35, "beta")
	g.CreateEdges("J", "K", 5, "beta")
	g.CreateEdges("C", "C", 16, "beta")
	return g
}

func take(g *Graph, s, t string, k int) []Path {
	var result []Path
	for p := g.FindShortestPath(s, t); p.IsValid() && len(result) < k; p = g.FindNextShortestPath() {
		result = append(result, p)
	}
	return result
}

func weights(paths []Path) []int64 {
	w := make([]int64, len(paths))
	for i, p := range paths {
		w[i] = p.Weight()
	}
	return w
}

func equalInts(a, b []int64) bool {
	return fmt.Sprint(a) == fmt.Sprint(b)
}

type testEdge struct {
	from, to int
	weight   int64
}

// bruteForceWalkWeights returns the weights of all s-t walks (repeated vertices allowed)
// up to maxWeight, sorted.
func bruteForceWalkWeights(edges []testEdge, s, t int, maxWeight int64) []int64 {
	var result []int64
	type state struct {
		vertex int
		weight int64
	}
	stack := []state{{s, 0}}
	for len(stack) > 0 {
		cur := stack[len(stack)-1]
		stack = stack[:len(stack)-1]
		if cur.vertex == t && cur.weight > 0 {
			result = append(result, cur.weight)
		}
		for _, e := range edges {
			if e.from == cur.vertex && cur.weight+e.weight <= maxWeight {
				stack = append(stack, state{e.to, cur.weight + e.weight})
			}
		}
	}
	sort.Slice(result, func(i, j int) bool { return result[i] < result[j] })
	return result
}

// reachable tells if t can be reached from s.
func reachable(edges []testEdge, s, t int) bool {
	seen := map[int]bool{s: true}
	queue := []int{s}
	for len(queue) > 0 {
		v := queue[0]
		queue = queue[1:]
		for _, e := range edges {
			if e.from == v && !seen[e.to] {
				seen[e.to] = true
				queue = append(queue, e.to)
			}
		}
	}
	return seen[t]
}

func assertValidPath(t *testing.T, p Path, s, target string) {
	t.Helper()
	if !p[0].Tail.Equals(s) || !p[len(p)-1].Head.Equals(target) {
		t.Fatalf("path %s does not go from %s to %s", p.VertexNames(), s, target)
	}
	for i := 1; i < len(p); i++ {
		if p[i-1].Head != p[i].Tail {
			t.Fatalf("path %s is not connected", p.VertexNames())
		}
	}
}

func TestEppsteinExampleFirstPathsMatchReference(t *testing.T) {
	expected := []string{
		"S,D,E,F,J,T:55", "S,A,B,C,G,T:58", "S,A,B,F,J,T:59", "S,D,E,I,J,T:61",
		"S,A,E,F,J,T:62", "S,D,E,J,T:63", "S,D,E,F,G,T:64", "S,D,H,I,J,T:65",
		"S,D,E,F,T:67", "S,A,E,I,J,T:68", "S,A,B,F,G,T:68", "S,A,E,J,T:70",
		"S,A,E,F,G,T:71", "S,A,B,F,T:71", "S,A,E,F,T:74", "S,A,B,C,C,G,T:74",
		"S,A,B,C,C,C,G,T:90",
	}
	paths := take(eppsteinExample(), "S", "T", len(expected))

	// Order among equal weights is not specified: compare weights in order, paths as sets
	var expectedWeights []int64
	want := map[string]bool{}
	for _, x := range expected {
		var w int64
		fmt.Sscan(x[strings.Index(x, ":")+1:], &w)
		expectedWeights = append(expectedWeights, w)
		want[x] = true
	}
	if !equalInts(weights(paths), expectedWeights) {
		t.Fatalf("weights = %v, want %v", weights(paths), expectedWeights)
	}
	for _, p := range paths {
		if key := fmt.Sprintf("%s:%d", p.VertexNames(), p.Weight()); !want[key] {
			t.Errorf("unexpected path %s", key)
		}
	}
}

func TestCyclicGraphTerminatesAndRepeatsCycle(t *testing.T) {
	g := NewGraph()
	g.CreateVertices("S,A,B,T")
	g.CreateEdges("S,A,B,A", "A,B,A,T", 1, "g") // A <-> B cycle
	paths := take(g, "S", "T", 5)
	if !equalInts(weights(paths), []int64{2, 4, 6, 8, 10}) {
		t.Fatalf("weights = %v", weights(paths))
	}
	if paths[1].VertexNames() != "S,A,B,A,T" {
		t.Fatalf("second path = %s", paths[1].VertexNames())
	}
}

func TestUnreachableTargetReturnsEmptyPath(t *testing.T) {
	g := NewGraph()
	g.CreateVertices("S,A,T")
	g.CreateEdges("S,A", "A,S", 1, "g")
	if g.FindShortestPath("S", "T").IsValid() || g.FindNextShortestPath().IsValid() {
		t.Fatal("expected empty paths")
	}
	if g.FindShortestPath("S", "X").IsValid() {
		t.Fatal("expected empty path for unknown vertex")
	}
}

func TestInputValidation(t *testing.T) {
	g := NewGraph()
	if g.CreateVertices("A,1B") || g.CreateVertices("A,B-C") {
		t.Fatal("invalid labels accepted")
	}
	if !g.CreateVertices("home,Work2,,HOME") {
		t.Fatal("valid labels rejected")
	}
	if g.CreateEdges("HOME", "WORK2,HOME", 1, "x") || g.CreateEdges("HOME", "NOWHERE", 1, "x") {
		t.Fatal("invalid edges accepted")
	}
	if !g.CreateEdges("home", "work2", 3, "x") {
		t.Fatal("valid edge rejected")
	}
	if p := g.FindShortestPath("Home", "WORK2"); p.VertexNames() != "HOME,WORK2" || p.Weight() != 3 {
		t.Fatalf("path = %s (%d)", p.VertexNames(), p.Weight())
	}
	g.CreateEdges("HOME", "HOME", 1, "x")
	if g.FindNextShortestPath().IsValid() {
		t.Fatal("FindNextShortestPath must return empty path after graph change")
	}
}

func TestNegativeWeightsDisableEdges(t *testing.T) {
	g := eppsteinExample()
	g.EdgeGroupWeights("BETA", -1)
	for _, p := range take(g, "S", "T", 50) {
		for _, e := range p {
			if e.Group == "BETA" {
				t.Fatalf("path %s uses disabled edge %s", p.VertexNames(), e)
			}
		}
	}
}

func TestRandomCyclicGraphsMatchBruteForce(t *testing.T) {
	for _, arcFlags := range []bool{false, true} {
		rnd := rand.New(rand.NewSource(42))
		const maxWeight = 25
		for it := 0; it < 200; it++ {
			n := 2 + rnd.Intn(6)
			labels := make([]string, n)
			for i := range labels {
				labels[i] = fmt.Sprintf("V%d", i)
			}
			var edges []testEdge
			for i := 0; i < n; i++ {
				for j := 0; j < n; j++ {
					if rnd.Float64() < 0.3 {
						edges = append(edges, testEdge{i, j, int64(1 + rnd.Intn(9))})
					}
				}
			}

			g := NewGraph()
			g.CreateVertices(strings.Join(labels, ","))
			for _, e := range edges {
				g.CreateEdges(labels[e.from], labels[e.to], e.weight, "g")
			}
			if arcFlags && !g.EnableArcFlags(1+rnd.Intn(3)) {
				t.Fatal("EnableArcFlags failed")
			}

			s, target := rnd.Intn(n), rnd.Intn(n)
			if s == target {
				continue // empty path is reported as invalid
			}
			expected := bruteForceWalkWeights(edges, s, target, maxWeight)
			paths := take(g, labels[s], labels[target], len(expected)+1)
			var actual []int64
			for _, p := range paths {
				assertValidPath(t, p, labels[s], labels[target])
				if p.Weight() <= maxWeight {
					actual = append(actual, p.Weight())
				}
			}
			if !equalInts(actual, expected) {
				t.Fatalf("arcFlags=%v it=%d: weights %v, want %v", arcFlags, it, actual, expected)
			}
		}
	}
}

func TestArcFlagsGiveSameResultsAndFollowWeightChanges(t *testing.T) {
	plain := weights(take(eppsteinExample(), "S", "T", 30))
	g := eppsteinExample()
	if !g.EnableArcFlagsRegions("S,A,B,C;D,E,F;G,H,I,J,K,T") || !g.PrecomputeArcFlags() {
		t.Fatal("arc flags not enabled")
	}
	if g.ArcFlagsRegionCount() != 3 {
		t.Fatalf("regions = %d", g.ArcFlagsRegionCount())
	}
	if !equalInts(weights(take(g, "S", "T", 30)), plain) {
		t.Fatal("arc flags changed results")
	}

	h := eppsteinExample()
	h.EdgeGroupWeights("beta", 1)
	g.EdgeGroupWeights("beta", 1) // flags must be recomputed
	if !equalInts(weights(take(g, "S", "T", 30)), weights(take(h, "S", "T", 30))) {
		t.Fatal("results differ after weight change")
	}
}

func TestArcFlagsInvalidPartitionRejectedAndDisable(t *testing.T) {
	g := eppsteinExample()
	if g.EnableArcFlagsRegions("S,X") || g.EnableArcFlagsRegions("S,A;A") || g.EnableArcFlags(0) {
		t.Fatal("invalid partition accepted")
	}
	if g.ArcFlagsEnabled() {
		t.Fatal("arc flags enabled")
	}
	if !g.EnableArcFlags(2) {
		t.Fatal("EnableArcFlags(2) failed")
	}
	g.DisableArcFlags()
	if g.ArcFlagsRegionCount() != 0 {
		t.Fatal("regions after disable")
	}
}

func TestLargeWeightsBeyondInt32AreSupported(t *testing.T) {
	const big int64 = 3_000_000_000 // larger than math.MaxInt32
	g := NewGraph()
	g.CreateVertices("S,A,B,T")
	g.CreateEdges("S,A", "A,T", big, "far")
	g.CreateEdges("S,B", "B,T", big+1, "far2")
	if w := weights(take(g, "S", "T", 3)); !equalInts(w, []int64{2 * big, 2*big + 2}) {
		t.Fatalf("weights = %v", w)
	}
	if !strings.Contains(g.Serialize(), `label="3000000000"`) {
		t.Error("large weight missing in DOT output")
	}
	g.EdgeGroupWeights("far", 5_000_000_000)
	if w := g.FindShortestPath("S", "T").Weight(); w != 2*big+2 {
		t.Fatalf("weight after change = %d", w)
	}
}

func TestVertexLookupCaseInsensitiveAndRecreatedWithVertices(t *testing.T) {
	g := NewGraph()
	g.CreateVertices("Home,Work")
	if !g.CreateEdges("home", "WORK", 1, "x") {
		t.Fatal("edge rejected")
	}
	if p := g.FindShortestPath("hOmE", "work"); p.VertexNames() != "HOME,WORK" {
		t.Fatalf("path = %s", p.VertexNames())
	}
	g.CreateVertices("Other") // old vertices must no longer be found
	if g.CreateEdges("HOME", "OTHER", 1, "x") || g.FindShortestPath("HOME", "OTHER").IsValid() {
		t.Fatal("removed vertex still found")
	}
}

func TestZeroValueGraphIsUsable(t *testing.T) {
	var g Graph // without NewGraph
	g.CreateVertices("A,B")
	g.CreateEdges("A", "B", 1, "x")
	if !g.FindShortestPath("A", "B").IsValid() {
		t.Fatal("no path in zero value graph")
	}
	var h Graph
	if h.CreateEdges("A", "B", 1, "x") || h.FindShortestPath("A", "B").IsValid() {
		t.Fatal("empty zero value graph returned something")
	}
}

func TestPriorityQueueSortedAndStableForTies(t *testing.T) {
	rnd := rand.New(rand.NewSource(7))
	q := newPriorityQueue[int](0)
	type item struct {
		id     int
		weight int64
	}
	var items []item
	for i := 0; i < 2000; i++ {
		w := int64(rnd.Intn(50))
		items = append(items, item{i, w})
		q.Enqueue(i, w)
	}
	sort.SliceStable(items, func(a, b int) bool { return items[a].weight < items[b].weight })
	for _, want := range items {
		id, w, ok := q.Dequeue()
		if !ok || id != want.id || w != want.weight {
			t.Fatalf("got %d/%d, want %d/%d", id, w, want.id, want.weight)
		}
	}
	if _, _, ok := q.Dequeue(); ok {
		t.Fatal("queue not empty")
	}
}

func TestSidetrackChainRebuildsFullList(t *testing.T) {
	g := eppsteinExample()
	e := g.FindShortestPath("S", "T")[0]
	root := &stNode{}
	child := &stNode{parent: &stNode{parent: root, sidetrack: e}, sidetrack: e}
	if got := child.sidetracks(); len(got) != 2 || got[0] != e || got[1] != e {
		t.Fatalf("sidetracks = %v", got)
	}
	if len(root.sidetracks()) != 0 {
		t.Fatal("root has sidetracks")
	}
}

func TestMaxDetourReturnsExactlyThePathsWithinTheCorridor(t *testing.T) {
	for _, ratio := range []bool{false, true} {
		rnd := rand.New(rand.NewSource(11))
		checked := 0
		for it := 0; it < 200; it++ {
			n := 2 + rnd.Intn(6)
			labels := make([]string, n)
			for i := range labels {
				labels[i] = fmt.Sprintf("V%d", i)
			}
			var edges []testEdge
			for i := 0; i < n; i++ {
				for j := 0; j < n; j++ {
					if rnd.Float64() < 0.3 {
						edges = append(edges, testEdge{i, j, int64(1 + rnd.Intn(9))})
					}
				}
			}
			g := NewGraph()
			g.CreateVertices(strings.Join(labels, ","))
			for _, e := range edges {
				g.CreateEdges(labels[e.from], labels[e.to], e.weight, "g")
			}
			if rnd.Intn(2) == 0 {
				g.EnableArcFlags(1 + rnd.Intn(3))
			}

			s, target := rnd.Intn(n), rnd.Intn(n)
			if s == target {
				continue
			}
			if !reachable(edges, s, target) {
				if g.FindShortestPath(labels[s], labels[target]).IsValid() {
					t.Fatal("path found where none exists")
				}
				continue
			}
			const bruteForceLimit = 45
			all := bruteForceWalkWeights(edges, s, target, bruteForceLimit)
			if len(all) == 0 {
				continue // shortest path longer than the brute force limit
			}

			shortest := all[0]
			var limit int64
			if ratio {
				g.SetMaxDetourRatio(float64(rnd.Intn(6)) / 10)
				limit = shortest + int64(math.Floor(g.MaxDetourRatio()*float64(shortest)))
			} else {
				g.SetMaxDetour(int64(rnd.Intn(15)))
				limit = shortest + g.MaxDetour()
			}
			if limit > bruteForceLimit {
				continue // brute force would miss paths
			}
			var expected []int64
			for _, w := range all {
				if w <= limit {
					expected = append(expected, w)
				}
			}
			paths := take(g, labels[s], labels[target], len(expected)+5) // then an empty path
			for _, p := range paths {
				assertValidPath(t, p, labels[s], labels[target])
			}
			if !equalInts(weights(paths), expected) {
				t.Fatalf("ratio=%v it=%d: weights %v, want %v", ratio, it, weights(paths), expected)
			}
			checked++
		}
		t.Logf("ratio=%v: %d graphs checked", ratio, checked)
		if checked < 50 {
			t.Fatalf("only %d graphs checked", checked)
		}
	}
}

func TestMaxDetourValidatesValuesAndRequiresNewQuery(t *testing.T) {
	g := eppsteinExample()
	if g.MaxDetour() != math.MaxInt64 || !math.IsInf(g.MaxDetourRatio(), 1) {
		t.Fatal("limits set by default")
	}
	if g.SetMaxDetour(-1) || g.SetMaxDetourRatio(-0.1) || g.SetMaxDetourRatio(math.NaN()) {
		t.Fatal("invalid limit accepted")
	}
	if w := g.FindShortestPath("S", "T").Weight(); w != 55 {
		t.Fatalf("shortest = %d", w)
	}
	g.SetMaxDetour(4) // paths 55, 58, 59
	if g.FindNextShortestPath().IsValid() {
		t.Fatal("changing the limit must require a new query")
	}
	check := func(want []int64) {
		t.Helper()
		if got := weights(take(g, "S", "T", 10)); !equalInts(got, want) {
			t.Fatalf("weights = %v, want %v", got, want)
		}
	}
	check([]int64{55, 58, 59})
	g.SetMaxDetour(math.MaxInt64)
	g.SetMaxDetourRatio(0.1) // floor(5.5) = 5: paths up to 60
	check([]int64{55, 58, 59})
	g.SetMaxDetour(3) // the smaller limit applies
	check([]int64{55, 58})
}

func TestMaxDetourStopsShortestPathTreeAtTheCorridor(t *testing.T) {
	// Long chain behind the start: S <- X1 <- X2 <- X3 is far away from T
	g := NewGraph()
	g.CreateVertices("S,T,X1,X2,X3")
	g.CreateEdges("S,X1,X2,X3", "T,S,X1,X2", 10, "g")
	g.SetMaxDetour(5)
	if p := g.FindShortestPath("S", "T"); p.VertexNames() != "S,T" {
		t.Fatalf("path = %s", p.VertexNames())
	}
	dot := g.Serialize()
	if !strings.Contains(dot, `"S" [label="S\n10"`) || !strings.Contains(dot, `"X1" [label="X1"]`) {
		t.Fatalf("distances beyond the corridor computed:\n%s", dot)
	}
}

func TestPathEnumerationCreatesAtMostThreeCandidatesPerPath(t *testing.T) {
	g := gridGraph(30)
	paths := take(g, "V0X0", "V29X29", 2000)
	if len(paths) != 2000 {
		t.Fatalf("paths = %d", len(paths))
	}
	if g.candidatesCreated > int64(3*len(paths)+1) {
		t.Fatalf("candidates = %d", g.candidatesCreated)
	}
	for i := 1; i < len(paths); i++ {
		if paths[i-1].Weight() > paths[i].Weight() {
			t.Fatalf("paths out of order at %d", i)
		}
	}
}

func TestSerializeWritesAllVerticesAndEdges(t *testing.T) {
	dot := eppsteinExample().Serialize()
	if !strings.HasPrefix(dot, "digraph Eppstein {") || !strings.HasSuffix(strings.TrimSpace(dot), "}") {
		t.Fatal("not a DOT digraph")
	}
	for _, v := range strings.Split("S,A,B,C,D,E,F,G,H,I,J,K,T", ",") {
		if !strings.Contains(dot, `"`+v+`" [`) {
			t.Errorf("vertex %s missing", v)
		}
	}
	if n := strings.Count(dot, " -> "); n != 21 {
		t.Errorf("edges = %d, want 21", n)
	}
	if !strings.Contains(dot, `"C" -> "C" [label="16", tooltip="group BETA"]`) {
		t.Error("self loop missing")
	}
	if strings.Contains(dot, "doublecircle") {
		t.Error("query state shown before query")
	}
}

func TestSerializeHighlightsPathsAndQueryState(t *testing.T) {
	g := eppsteinExample()
	paths := take(g, "S", "T", 2)
	dot := g.SerializePaths(paths)
	for _, want := range []string{
		`"S" [label="S\n55", shape=doublecircle`,
		`"S" -> "D" [label="13", tooltip="group ALPHA", color="red"`,
		`"S" -> "A" [label="2", tooltip="group ALPHA", color="blue"`,
		`style="bold"`,
	} {
		if !strings.Contains(dot, want) {
			t.Errorf("missing %s", want)
		}
	}
	if g.SerializePath(paths[0]) != g.SerializePaths(paths[:1]) {
		t.Error("SerializePath differs from SerializePaths")
	}
}

func TestSerializeNegativeEdgesDashedAndRegionsAsClusters(t *testing.T) {
	g := eppsteinExample()
	g.EdgeGroupWeights("beta", -1)
	g.EnableArcFlagsRegions("S,A,B,C;D,E,F")
	g.PrecomputeArcFlags()
	dot := g.Serialize()
	for _, want := range []string{
		`"J" -> "K" [label="-1", tooltip="group BETA", style="dashed"]`,
		"subgraph cluster_0 {",
		"subgraph cluster_2 {", // unlisted vertices
	} {
		if !strings.Contains(dot, want) {
			t.Errorf("missing %s", want)
		}
	}
	if g.SerializeTo(nil, nil) == nil {
		t.Error("nil writer accepted")
	}
}
