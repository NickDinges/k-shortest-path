package eppstein_test

import (
	"fmt"

	"github.com/nickdinges/k-shortest-path/go/eppstein"
)

// Five places connected by one-way roads, weights are minutes.
func Example() {
	g := eppstein.NewGraph()

	// 1. Create the places (vertices)
	g.CreateVertices("HOME,PARK,BAKERY,STATION,OFFICE")

	// 2. Create one-way roads (edges): from, to, minutes, group name
	g.CreateEdges("HOME", "PARK", 2, "walk")
	g.CreateEdges("HOME", "BAKERY", 4, "walk")
	g.CreateEdges("PARK", "BAKERY", 1, "walk")
	g.CreateEdges("PARK", "STATION", 7, "walk")
	g.CreateEdges("BAKERY", "STATION", 2, "walk")
	g.CreateEdges("BAKERY", "OFFICE", 6, "walk")
	g.CreateEdges("STATION", "OFFICE", 1, "train")

	// 3. Ask for the shortest route, then for the next ones
	//    (paths of equal weight come in insertion order)
	p := g.FindShortestPath("HOME", "OFFICE")
	for i := 1; i <= 5 && p.IsValid(); i++ {
		fmt.Printf("%d. %s  (%d minutes)\n", i, p.VertexNames(), p.Weight())
		p = g.FindNextShortestPath()
	}
	// Output:
	// 1. HOME,PARK,BAKERY,STATION,OFFICE  (6 minutes)
	// 2. HOME,BAKERY,STATION,OFFICE  (7 minutes)
	// 3. HOME,PARK,BAKERY,OFFICE  (9 minutes)
	// 4. HOME,BAKERY,OFFICE  (10 minutes)
	// 5. HOME,PARK,STATION,OFFICE  (10 minutes)
}

// Draw the graph with Graphviz: write the output to a file and run "dot -Tsvg".
func ExampleGraph_SerializePath() {
	g := eppstein.NewGraph()
	g.CreateVertices("A,B")
	g.CreateEdges("A", "B", 3, "road")
	fmt.Print(g.SerializePath(g.FindShortestPath("A", "B")))
	// Output:
	// digraph Eppstein {
	//     rankdir=LR;
	//     node [shape=circle];
	//     "A" [label="A\n3", shape=doublecircle, penwidth=2];
	//     "B" [label="B\n0", shape=doublecircle, penwidth=2];
	//     "A" -> "B" [label="3", tooltip="group ROAD", color="red", fontcolor="red", penwidth=2, style="bold"];
	// }
}
