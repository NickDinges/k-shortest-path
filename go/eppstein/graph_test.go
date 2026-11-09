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
	if p.Edge(0).Tail().Label() != s || p.Edge(p.Len()-1).Head().Label() != target {
		t.Fatalf("path %s does not go from %s to %s", p.VertexNames(), s, target)
	}
	for i := 1; i < p.Len(); i++ {
		if p.Edge(i-1).Head() != p.Edge(i).Tail() {
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
		for e := range p.Edges() {
			if e.Group() == "BETA" {
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

func TestSidetrackHeapMergeIsPersistentAndHeapOrdered(t *testing.T) {
	rnd := rand.New(rand.NewSource(7))
	var heap *heapNode
	var versions []*heapNode
	var contents [][]int64
	var keys []int64
	for i := 0; i < 300; i++ {
		key := int64(rnd.Intn(50))
		heap = mergeHeaps(heap, &heapNode{edge: int32(i), key: key, rank: 1})
		keys = append(keys, key)
		versions = append(versions, heap)
		sorted := append([]int64(nil), keys...)
		sort.Slice(sorted, func(a, b int) bool { return sorted[a] < sorted[b] })
		contents = append(contents, sorted)
	}
	// every version still contains exactly its keys, in heap order (persistence)
	for i := 0; i < len(versions); i += 37 {
		var got []int64
		var walk func(n *heapNode)
		walk = func(n *heapNode) {
			if n == nil {
				return
			}
			for _, child := range []*heapNode{n.left, n.right} {
				if child != nil && child.key < n.key {
					t.Fatal("heap order violated")
				}
			}
			got = append(got, n.key)
			walk(n.left)
			walk(n.right)
		}
		walk(versions[i])
		sort.Slice(got, func(a, b int) bool { return got[a] < got[b] })
		if !equalInts(got, contents[i]) {
			t.Fatalf("version %d: keys %v, want %v", i, got, contents[i])
		}
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

func TestAddVertexReturnsIndicesAndMixesWithCreateVertices(t *testing.T) {
	g := NewGraph()
	g.CreateVertices("A,B")
	check := func(got, want int, what string) {
		t.Helper()
		if got != want {
			t.Fatalf("%s = %d, want %d", what, got, want)
		}
	}
	check(g.VertexCount(), 2, "VertexCount")
	check(g.VertexIndex("a"), 0, "VertexIndex(a)")
	check(g.VertexIndex("B"), 1, "VertexIndex(B)")
	check(g.AddVertex("Home"), 2, "AddVertex(Home)")
	check(g.AddVertex("HOME"), 2, "AddVertex(HOME)") // existing label, case-insensitive
	for _, invalid := range []string{"1A", "A-B", ""} {
		check(g.AddVertex(invalid), -1, "AddVertex("+invalid+")")
	}
	check(g.VertexIndex("X"), -1, "VertexIndex(X)")
	check(g.VertexCount(), 3, "VertexCount")

	if g.AddEdge(0, 2, 4) != 0 || g.AddEdgeGroup(0, 1, 1, "fast") != 1 || g.AddEdge(1, 2, 1) != 2 {
		t.Fatal("AddEdge must return the edge numbers 0, 1, 2")
	}
	if g.AddEdge(0, 3, 1) != -1 || g.AddEdge(-1, 0, 1) != -1 {
		t.Fatal("invalid index accepted")
	}
	if !g.CreateEdges("HOME", "a", 2, "x") { // string and index methods mix
		t.Fatal("CreateEdges failed")
	}
	if p := g.FindShortestPathByIndex(0, 2); p.VertexNames() != "A,B,HOME" {
		t.Fatalf("path = %s", p.VertexNames())
	}
	if g.FindShortestPathByIndex(0, 3).IsValid() || g.FindShortestPathByIndex(-1, 0).IsValid() {
		t.Fatal("path for invalid index")
	}
	g.EdgeGroupWeights("FAST", 10) // group given to AddEdgeGroup, upper case
	if p := g.FindShortestPath("A", "HOME"); p.VertexNames() != "A,HOME" {
		t.Fatalf("path after group change = %s", p.VertexNames())
	}
}

func TestBuildingByIndexAndImportsGiveSameResultsAsCreateEdges(t *testing.T) {
	rnd := rand.New(rand.NewSource(5))
	for it := 0; it < 100; it++ {
		n := 2 + rnd.Intn(7)
		var edges []testEdge
		for i := 0; i < n; i++ {
			for j := 0; j < n; j++ {
				if rnd.Float64() < 0.3 {
					edges = append(edges, testEdge{i, j, int64(1 + rnd.Intn(9))})
				}
			}
		}
		labels := make([]string, n) // DIMACS numbers vertices from 1 and labels them V1..Vn
		for i := range labels {
			labels[i] = fmt.Sprintf("V%d", i+1)
		}

		byLabels := NewGraph()
		byLabels.CreateVertices(strings.Join(labels, ","))
		byIndex := NewGraph()
		for _, l := range labels {
			byIndex.AddVertex(l)
		}
		var csv, dimacs strings.Builder
		fmt.Fprintf(&dimacs, "c random graph\np sp %d %d\n", n, len(edges))
		for _, e := range edges {
			byLabels.CreateEdges(labels[e.from], labels[e.to], e.weight, "g")
			byIndex.AddEdgeGroup(e.from, e.to, e.weight, "g")
			fmt.Fprintf(&csv, "%s,%s,%d,g\n", labels[e.from], labels[e.to], e.weight)
			fmt.Fprintf(&dimacs, "a %d %d %d\n", e.from+1, e.to+1, e.weight)
		}
		fromCsv := NewGraph()
		fromCsv.CreateVertices(strings.Join(labels, ",")) // keeps isolated vertices
		if count, err := fromCsv.ImportEdges(strings.NewReader(csv.String())); err != nil || count != len(edges) {
			t.Fatalf("ImportEdges = %d, %v", count, err)
		}
		fromDimacs := NewGraph()
		if count, err := fromDimacs.ImportDimacs(strings.NewReader(dimacs.String())); err != nil || count != len(edges) {
			t.Fatalf("ImportDimacs = %d, %v", count, err)
		}

		s, target := rnd.Intn(n), rnd.Intn(n)
		expected := weights(take(byLabels, labels[s], labels[target], 30))
		for _, g := range []*Graph{byIndex, fromCsv, fromDimacs} {
			if g.VertexCount() != n {
				t.Fatalf("VertexCount = %d, want %d", g.VertexCount(), n)
			}
			if got := weights(take(g, labels[s], labels[target], 30)); !equalInts(got, expected) {
				t.Fatalf("it=%d: weights %v, want %v", it, got, expected)
			}
		}
	}
}

func TestImportEdgesReadsCsvAndReportsErrorsWithLineNumber(t *testing.T) {
	g := NewGraph()
	count, err := g.ImportEdges(strings.NewReader(
		"# tail,head,weight,group\nhome, park, 2\n\n  PARK ,office,7,Train  \nHOME,OFFICE,10\n"))
	if err != nil || count != 3 || g.VertexCount() != 3 {
		t.Fatalf("ImportEdges = %d, %v, vertices %d", count, err, g.VertexCount())
	}
	if p := g.FindShortestPath("HOME", "OFFICE"); p.VertexNames() != "HOME,PARK,OFFICE" {
		t.Fatalf("path = %s", p.VertexNames())
	}
	g.EdgeGroupWeights("train", 100)
	if p := g.FindShortestPath("HOME", "OFFICE"); p.VertexNames() != "HOME,OFFICE" {
		t.Fatalf("path after group change = %s", p.VertexNames())
	}

	if _, err := g.ImportEdges(strings.NewReader("A,B,1\nA,B\n")); err == nil || !strings.HasPrefix(err.Error(), "line 2:") {
		t.Fatalf("error = %v", err)
	}
	if w := g.FindShortestPath("A", "B").Weight(); w != 1 {
		t.Fatalf("line 1 not imported, weight %d", w)
	}
	for _, text := range []string{"A,1B,1", "A,B,x", "A,B,1,g,h"} {
		if _, err := g.ImportEdges(strings.NewReader(text)); err == nil || !strings.HasPrefix(err.Error(), "line 1:") {
			t.Errorf("%q: error = %v", text, err)
		}
	}
	if _, err := g.ImportEdges(nil); err == nil {
		t.Error("nil reader accepted")
	}
}

func TestImportDimacsReadsGraphAndReportsErrorsWithLineNumber(t *testing.T) {
	g := NewGraph()
	count, err := g.ImportDimacs(strings.NewReader(
		"c 9th DIMACS challenge format\np sp 3 3\na 1 2 5\na\t2  3 5\na 1 3 20\n"))
	if err != nil || count != 3 {
		t.Fatalf("ImportDimacs = %d, %v", count, err)
	}
	if w := weights(take(g, "V1", "V3", 5)); !equalInts(w, []int64{10, 20}) {
		t.Fatalf("weights = %v", w)
	}
	if g.VertexIndex("V1") != 0 {
		t.Fatal("V1 is not vertex 0")
	}
	for text, line := range map[string]string{
		"a 1 2 3":             "line 1:", // edge before problem line
		"p sp 2 1\na 1 3 1":   "line 2:", // vertex out of range
		"p sp 2 1\na 1 2":     "line 2:", // missing weight
		"p sp 2 1\na 1 2 3 4": "line 2:", // too many values
		"p sp 2 1\np sp 2 1":  "line 2:", // two problem lines
		"p max 2 1":           "line 1:", // other problem type
		"x 1 2":               "line 1:", // unknown line type
		// absurd vertex counts: a few bytes must not overflow or allocate all memory
		"p sp 2147483647 1\na 1 2 3":  "line 1:",
		"p sp 400000000 1\na 1 2 3":   "line 1:",
		"p sp 9223372036854775807 1":  "line 1:",
		"p sp 18446744073709551616 1": "line 1:", // beyond uint64
		"p sp -5 1":                   "line 1:",
	} {
		if _, err := NewGraph().ImportDimacs(strings.NewReader(text)); err == nil || !strings.HasPrefix(err.Error(), line) {
			t.Errorf("%q: error = %v, want %s", text, err, line)
		}
	}
	if _, err := g.ImportDimacs(nil); err == nil {
		t.Error("nil reader accepted")
	}
	if _, err := NewGraph().ImportDimacsLimit(strings.NewReader("p sp 3 0"), 2); err == nil || !strings.Contains(err.Error(), "limit of 2") {
		t.Errorf("limit 2: error = %v", err)
	}
	if count, err := NewGraph().ImportDimacsLimit(strings.NewReader("p sp 3 0"), 3); err != nil || count != 0 {
		t.Errorf("raised limit: %d, %v", count, err)
	}
	assertPanics(t, func() { NewGraph().ImportDimacsLimit(strings.NewReader(""), -1) })
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

func TestSetEdgeWeightInvalidatesArcFlagsAndQuery(t *testing.T) {
	g := NewGraph()
	g.CreateVertices("S,A,B,T")
	g.CreateEdges("S,A", "A,T", 1, "x")
	g.CreateEdges("S,B", "B,T", 5, "y")
	if !g.EnableArcFlagsRegions("S,A,B;T") {
		t.Fatal("arc flags not enabled")
	}
	before := g.FindShortestPath("S", "T")
	if before.VertexNames() != "S,A,T" {
		t.Fatalf("path = %s", before.VertexNames())
	}
	for e := range before.Edges() {
		g.SetEdgeWeight(e, 100)
	}
	if g.FindNextShortestPath().IsValid() {
		t.Fatal("query not invalidated by the change")
	}
	if after := g.FindShortestPath("S", "T"); after.String() != "S,B,T (10)" {
		t.Fatalf("path after change = %s", after)
	}
	if before.Weight() != 2 {
		t.Fatal("a path must keep the weight it had when it was found")
	}

	other := NewGraph()
	other.CreateVertices("A,B")
	other.AddEdge(0, 1, 1)
	assertPanics(t, func() { g.SetEdgeWeight(other.Edge(0), 1) })
	assertPanics(t, func() { g.Edge(99) })
	assertPanics(t, func() { g.Vertex(-1) })
}

func assertPanics(t *testing.T, f func()) {
	t.Helper()
	defer func() {
		if recover() == nil {
			t.Fatal("expected a panic")
		}
	}()
	f()
}

func TestVertexAndEdgeAreValues(t *testing.T) {
	g := eppsteinExample()
	p := g.FindShortestPath("S", "T")
	s, ok := g.FindVertex("s")
	if !ok || s != p.Edge(0).Tail() || s != g.Vertex(s.Index()) || p.Edge(0) != g.Edge(p.Edge(0).ID()) {
		t.Fatal("vertex or edge values are not equal")
	}
	if eppsteinExample().Vertex(s.Index()) == s {
		t.Fatal("vertices of different graphs must not be equal")
	}
	if _, ok := g.FindVertex("X"); ok {
		t.Fatal("unknown vertex found")
	}
	if s.String() != "S" || p.Edge(0).String() != "S--13-->D" {
		t.Fatalf("strings %q %q", s, p.Edge(0))
	}
	if s.Distance() != 55 {
		t.Fatalf("distance = %d", s.Distance())
	}
	if e, ok := s.TreeEdge(); !ok || e != p.Edge(0) {
		t.Fatal("tree edge")
	}
	if next, ok := s.Next(); !ok || next.Label() != "D" {
		t.Fatal("next")
	}
	if tv, _ := g.FindVertex("T"); func() bool { _, ok := tv.Next(); return ok }() {
		t.Fatal("goal has a next vertex")
	}
	var heads, tails []string
	for e := range s.OutEdges() {
		heads = append(heads, e.Head().Label())
	}
	tv, _ := g.FindVertex("T")
	for e := range tv.InEdges() {
		tails = append(tails, e.Tail().Label())
	}
	sort.Strings(heads)
	sort.Strings(tails)
	if strings.Join(heads, ",") != "A,D" || strings.Join(tails, ",") != "F,G,J" {
		t.Fatalf("out %v, in %v", heads, tails)
	}
	var names []string
	for v := range p.Vertices() {
		names = append(names, v.Label())
	}
	if strings.Join(names, ",") != p.VertexNames() {
		t.Fatal("Vertices and VertexNames differ")
	}

	s.SetTag("start")
	if g.Vertex(s.Index()).Tag() != "start" {
		t.Fatal("tag not stored")
	}
	if a, _ := g.FindVertex("A"); a.Tag() != nil {
		t.Fatal("tag of another vertex")
	}
	count := 0
	for range g.Vertices() {
		count++
	}
	for range g.Edges() {
		count++
	}
	if count != 13+21 || g.EdgeCount() != 21 {
		t.Fatalf("vertices + edges = %d", count)
	}
}

func TestNewQueryDoesNotShowDistancesOfThePreviousQuery(t *testing.T) {
	// distances are only valid for the current query (no reset of all vertices any more)
	g := NewGraph()
	g.CreateVertices("S,T,X1,X2")
	g.CreateEdges("S,X1,X2", "T,S,X1", 10, "g")
	g.FindShortestPath("X2", "T")
	x2, _ := g.FindVertex("X2")
	if x2.Distance() != 30 {
		t.Fatalf("distance = %d", x2.Distance())
	}
	g.SetMaxDetour(5)
	g.FindShortestPath("S", "T") // corridor: X1 and X2 are not reached
	if s, _ := g.FindVertex("S"); s.Distance() != 10 || x2.Distance() != Unreached {
		t.Fatalf("distances %d, %d", s.Distance(), x2.Distance())
	}
	if _, ok := x2.TreeEdge(); ok {
		t.Fatal("tree edge of the previous query")
	}
}

func TestPathsIteratorsAndChanges(t *testing.T) {
	g := eppsteinExample()
	expected := weights(take(eppsteinExample(), "S", "T", 20))
	var fromPaths, fromWeights []int64
	for p := range g.Paths("S", "T") {
		if fromPaths = append(fromPaths, p.Weight()); len(fromPaths) == 20 {
			break
		}
	}
	for w := range g.PathWeights("S", "T") {
		if fromWeights = append(fromWeights, w); len(fromWeights) == 20 {
			break
		}
	}
	if !equalInts(fromPaths, expected) || !equalInts(fromWeights, expected) {
		t.Fatalf("paths %v, weights %v, want %v", fromPaths, fromWeights, expected)
	}
	for range g.Paths("S", "X") {
		t.Fatal("path to an unknown vertex")
	}
	for range g.PathsByIndex(g.VertexIndex("T"), g.VertexIndex("S")) {
		t.Fatal("path to an unreachable vertex")
	}

	g.SetMaxDetour(4)
	var all []int64
	for w := range g.PathWeightsByIndex(g.VertexIndex("S"), g.VertexIndex("T")) {
		all = append(all, w)
	}
	if !equalInts(all, []int64{55, 58, 59}) {
		t.Fatalf("corridor weights %v", all)
	}

	assertPanics(t, func() {
		for p := range g.Paths("S", "T") {
			g.SetEdgeWeight(p.Edge(0), 3) // change during the iteration
		}
	})
	assertPanics(t, func() {
		for range g.PathWeights("S", "T") {
			g.FindShortestPath("A", "T") // another query during the iteration
		}
	})
}

func TestPathValues(t *testing.T) {
	g := eppsteinExample()
	p := g.FindShortestPath("S", "T")
	var sum int64
	for e := range p.Edges() {
		sum += e.Weight()
	}
	if sum != p.Weight() || p.String() != "S,D,E,F,J,T (55)" {
		t.Fatalf("weight %d, string %q", sum, p)
	}
	var empty Path
	if empty.IsValid() || empty.VertexNames() != "(empty)" || empty.String() != "(empty)" {
		t.Fatal("zero value is not the empty path")
	}
	for range empty.Vertices() {
		t.Fatal("empty path has vertices")
	}
}
