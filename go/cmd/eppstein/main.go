// Command eppstein runs Eppstein's k shortest paths algorithm on the example graph of
// the original paper and prints the k shortest paths from S to T.
//
// Usage:
//
//	eppstein [k] [--dot file]
//
// k is the number of paths to print (default 20); file receives a Graphviz drawing with
// the first (up to 3) paths highlighted.
package main

import (
	"fmt"
	"os"
	"strconv"
	"time"

	"github.com/nickdinges/k-shortest-path/go/eppstein"
)

func usage() {
	fmt.Println("Usage: eppstein [k] [--dot file]")
	fmt.Println("  k     number of paths to print (default 20)")
	fmt.Println("  file  Graphviz DOT output, render with: dot -Tsvg file -o graph.svg")
}

func main() {
	// Paths may repeat vertices (cycle C->C below), so there are infinitely many: limit them
	k := 20
	dotFile := ""
	args := os.Args[1:]
	for i := 0; i < len(args); i++ {
		if (args[i] == "--dot" || args[i] == "-dot") && i+1 < len(args) {
			i++
			dotFile = args[i]
			continue
		}
		n, err := strconv.Atoi(args[i])
		if err != nil || n < 1 {
			usage()
			os.Exit(2)
		}
		k = n
	}

	g := eppstein.NewGraph()
	g.CreateVertices("S,A,B,C,D,E,F,G,H,I,J,K,T")

	// Edges in original Eppstein example (1997)
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

	// Additional edges for special cases testing
	g.CreateEdges("E", "J", 30, "beta") // diagonal edge
	g.CreateEdges("F", "T", 35, "beta") // diagonal edge
	g.CreateEdges("J", "K", 5, "beta")  // edge not pointing to shortest path
	g.CreateEdges("C", "C", 16, "beta") // cycling edge

	// g.EdgeGroupWeights("beta", -1)
	// Optional arc-flags speed-up (results are identical, preprocessing is done lazily)
	// g.EnableArcFlagsRegions("S,A,B,C;D,E,F;G,H,I,J,K,T") // or: g.EnableArcFlags(3)

	fmt.Println("EPPSTEIN ALGORITHM TEST")

	start := time.Now()
	p := g.FindShortestPath("S", "T")
	fmt.Printf("Calculation time: %d ms\n", time.Since(start).Milliseconds())

	var found []eppstein.Path
	for i := 0; i < k && p.IsValid(); i++ {
		fmt.Printf("%s (%d)\n", p.VertexNames(), p.Weight())
		found = append(found, p)
		p = g.FindNextShortestPath()
	}

	if dotFile != "" {
		if err := os.WriteFile(dotFile, []byte(g.SerializePaths(found[:min(3, len(found))])), 0o644); err != nil {
			fmt.Fprintln(os.Stderr, "error:", err)
			os.Exit(1)
		}
		fmt.Println("Graphviz output written to " + dotFile)
	}

	fmt.Println("End.")
}
