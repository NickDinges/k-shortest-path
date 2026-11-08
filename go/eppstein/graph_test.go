package eppstein

import (
	"fmt"
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

func weights(paths []Path) []int {
	w := make([]int, len(paths))
	for i, p := range paths {
		w[i] = p.Weight()
	}
	return w
}

func equalInts(a, b []int) bool {
	return fmt.Sprint(a) == fmt.Sprint(b)
}

type testEdge struct{ from, to, weight int }

// bruteForceWalkWeights returns the weights of all s-t walks (repeated vertices allowed)
// up to maxWeight, sorted.
func bruteForceWalkWeights(edges []testEdge, s, t, maxWeight int) []int {
	var result []int
	type state struct{ vertex, weight int }
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
	sort.Ints(result)
	return result
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
	var expectedWeights []int
	want := map[string]bool{}
	for _, x := range expected {
		var w int
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
	if !equalInts(weights(paths), []int{2, 4, 6, 8, 10}) {
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
						edges = append(edges, testEdge{i, j, 1 + rnd.Intn(9)})
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
			var actual []int
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
