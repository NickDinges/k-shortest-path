package eppstein

import (
	"encoding/json"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"testing"
)

// Shared golden test data in testdata/golden, checked by the C# and the Go test suites, so both
// implementations return the same paths. The order among paths of equal weight is not compared.

type goldenPath struct {
	Weight   int64  `json:"weight"`
	Vertices string `json:"vertices"`
}

type goldenCase struct {
	Name           string       `json:"name"`
	Graph          string       `json:"graph"`
	From           string       `json:"from"`
	To             string       `json:"to"`
	MaxDetour      *int64       `json:"maxDetour"`
	MaxDetourRatio *float64     `json:"maxDetourRatio"`
	Complete       bool         `json:"complete"`
	Paths          []goldenPath `json:"paths"`
}

const goldenDir = "../../testdata/golden"

func TestGoldenCases(t *testing.T) {
	data, err := os.ReadFile(filepath.Join(goldenDir, "cases.json"))
	if err != nil {
		t.Fatal(err)
	}
	var cases []goldenCase
	if err := json.Unmarshal(data, &cases); err != nil {
		t.Fatal(err)
	}
	for _, c := range cases {
		t.Run(c.Name, func(t *testing.T) {
			g := NewGraph()
			f, err := os.Open(filepath.Join(goldenDir, c.Graph))
			if err != nil {
				t.Fatal(err)
			}
			defer f.Close()
			if strings.HasSuffix(c.Graph, ".gr") {
				_, err = g.ImportDimacs(f)
			} else {
				_, err = g.ImportEdges(f)
			}
			if err != nil {
				t.Fatal(err)
			}
			if c.MaxDetour != nil {
				g.SetMaxDetour(*c.MaxDetour)
			}
			if c.MaxDetourRatio != nil {
				g.SetMaxDetourRatio(*c.MaxDetourRatio)
			}

			// Complete: all paths must come and then an empty path. Otherwise: all paths up to
			// the weight of the last expected one.
			var lastWeight int64
			if len(c.Paths) > 0 {
				lastWeight = c.Paths[len(c.Paths)-1].Weight
			}
			var actual []goldenPath
			for p := g.FindShortestPath(c.From, c.To); p.IsValid(); p = g.FindNextShortestPath() {
				if !c.Complete && p.Weight() > lastWeight {
					break
				}
				actual = append(actual, goldenPath{p.Weight(), p.VertexNames()})
				if len(actual) > len(c.Paths) {
					t.Fatalf("more paths than expected (%d)", len(c.Paths))
				}
			}
			compareGolden(t, c.Paths, actual)
		})
	}
}

func compareGolden(t *testing.T, expected, actual []goldenPath) {
	t.Helper()
	if len(expected) != len(actual) {
		t.Fatalf("got %d paths, want %d", len(actual), len(expected))
	}
	byWeight := func(paths []goldenPath) map[int64][]string {
		m := map[int64][]string{}
		for _, p := range paths {
			m[p.Weight] = append(m[p.Weight], p.Vertices)
		}
		for _, v := range m {
			sort.Strings(v)
		}
		return m
	}
	for i := range expected {
		if expected[i].Weight != actual[i].Weight {
			t.Fatalf("path %d: weight %d, want %d", i, actual[i].Weight, expected[i].Weight)
		}
	}
	e, a := byWeight(expected), byWeight(actual)
	for w, paths := range e {
		if strings.Join(paths, " ") != strings.Join(a[w], " ") {
			t.Fatalf("weight %d: paths %v, want %v", w, a[w], paths)
		}
	}
}
