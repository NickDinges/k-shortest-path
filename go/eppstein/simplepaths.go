package eppstein

import (
	"encoding/binary"
	"slices"
)

// k shortest simple paths (no repeated vertices): Yen's algorithm with Lawler's improvement, A*
// searches guided by the exact distances to the goal, and the detour corridor.
//
// The paths are found one after the other. Every new path is the best one among the candidates;
// a candidate is a path found so far up to some vertex (the root), followed by the best way from
// there to the goal that leaves the root along an edge no found path with the same root uses (the
// spur). Lawler: a path only needs spurs from the vertex where it deviated from the path it was
// derived from; the spurs before that vertex were already tried for that earlier path.
//
// Each spur is a shortest-path search with some vertices and edges blocked. Blocking only makes
// distances longer, so the distances to the goal from the shortest path tree are exact lower
// bounds: the searches run as A*, which goes straight towards the goal. With a corridor, a search
// stops as soon as no path within the limit is possible.

// simpleState holds the setting and the state of a query in simple mode.
type simpleState struct {
	simpleOnly       bool
	simpleCandidates *priorityQueue[*simpleCandidate]
	simpleSeen       map[string]struct{} // edge sequences of all candidates (to drop duplicates)
	simpleTrie       *trieNode
	simplePending    *simpleCandidate // last returned path whose spurs are not generated yet
	simpleMaxWeight  int64            // largest allowed path weight (corridor)
	spurSearches     int64            // since the last FindShortestPath (for tests)

	// A* state, valid where the stamp matches the current search
	spurStamp                      int32
	spurOpenStamp, spurClosedStamp []int32
	spurCost                       []int64
	spurParent                     []int32
	pathStamp                      int32
	onPathStamp, onPathPosition    []int32
	spurOpen                       *priorityQueue[int32]
}

// simpleCandidate is a candidate simple path and the position where it deviated from its parent.
type simpleCandidate struct {
	edges     []int32
	weight    int64
	deviation int
}

// trieNode is a node of the trie of the found paths by edge; its children are the edges to block.
type trieNode struct {
	children map[int32]*trieNode
}

func (t *trieNode) child(e int32) *trieNode {
	if t.children == nil {
		t.children = map[int32]*trieNode{}
	}
	c, ok := t.children[e]
	if !ok {
		c = &trieNode{}
		t.children[e] = c
	}
	return c
}

// SetSimplePathsOnly sets whether only simple paths are returned, i.e. paths that visit no vertex
// twice.
//
// By default (false) the k shortest paths may repeat vertices and edges (Eppstein's algorithm).
// In networks with edges in both directions, such as road networks, most of these are the
// shortest path with a loop added (drive A->B->A), not real alternatives. With true, the paths are
// the k shortest simple paths (Yen's algorithm with Lawler's improvement).
//
// Simple paths cost more: every returned path needs a few shortest-path searches (guided by the
// distances to the goal, and limited by the corridor), while Eppstein needs one search in total.
// The number of simple paths is finite, so an iteration always ends. PathWeights is not faster
// than Paths in this mode. Arc flags only speed up the first search. Changing it requires a new
// query.
func (g *Graph) SetSimplePathsOnly(simple bool) {
	g.simpleOnly = simple
	g.settingsChanged()
}

// SimplePathsOnly tells if only simple paths are returned (see SetSimplePathsOnly).
func (g *Graph) SimplePathsOnly() bool { return g.simpleOnly }

// startSimplePaths prepares a simple-paths query after the shortest path tree has been built: the
// first candidate is the shortest path itself.
func (g *Graph) startSimplePaths(anyPath bool) {
	g.simpleCandidates = newPriorityQueue[*simpleCandidate](64)
	g.simpleSeen = map[string]struct{}{}
	g.simpleTrie = &trieNode{}
	g.simplePending = nil
	g.spurSearches = 0
	if g.spurOpen == nil {
		g.spurOpen = newPriorityQueue[int32](256)
	}
	if !anyPath {
		return
	}
	if n := len(g.labels); len(g.spurOpenStamp) < n {
		g.spurOpenStamp = make([]int32, n)
		g.spurClosedStamp = make([]int32, n)
		g.spurCost = make([]int64, n)
		g.spurParent = make([]int32, n)
		g.onPathStamp = make([]int32, n)
		g.onPathPosition = make([]int32, n)
		g.spurStamp, g.pathStamp = 0, 0
	}

	// the shortest path follows the tree; it is simple because tree paths are
	var edges []int32
	for v := int32(g.source); int(v) != g.target; v = g.edgeHead[g.treeEdge[v]] {
		edges = append(edges, g.treeEdge[v])
	}
	g.simpleMaxWeight = saturatingAdd(g.distance[g.source], g.pathBound)
	g.addSimpleCandidate(edges, g.distance[g.source], 0)
}

// nextSimplePath returns the next simple path: first generates the spurs of the path returned
// before (lazily, so asking for one path costs no spur searches), then takes the best candidate.
func (g *Graph) nextSimplePath() (Path, int64, bool) {
	if !g.ready {
		return Path{}, 0, false
	}
	if g.simplePending != nil {
		g.generateSpurs(g.simplePending)
		g.simplePending = nil
	}
	next, _, ok := g.simpleCandidates.Dequeue()
	if !ok {
		return Path{}, 0, false
	}
	// found paths form a trie: the children of a root are the edges to block at its end
	node := g.simpleTrie
	for _, e := range next.edges {
		node = node.child(e)
	}
	g.simplePending = next
	return Path{g: g, edges: next.edges, weight: next.weight}, next.weight, true
}

// generateSpurs is Yen with Lawler's improvement: for every vertex of the path from its deviation
// vertex on, it finds the best way to the goal that keeps the path up to that vertex (the root),
// avoids the root's vertices and leaves along an edge no found path with the same root uses.
func (g *Graph) generateSpurs(path *simpleCandidate) {
	edges := path.edges

	// positions of the path's vertices: vertex i is blocked for spurs at positions after i
	g.pathStamp++
	if g.pathStamp == 1<<31-1 {
		clear(g.onPathStamp)
		g.pathStamp = 1
	}
	v := int32(g.source)
	for i := 0; ; i++ {
		g.onPathStamp[v] = g.pathStamp
		g.onPathPosition[v] = int32(i)
		if i == len(edges) {
			break
		}
		v = g.edgeHead[edges[i]]
	}

	node := g.simpleTrie
	var rootWeight int64
	v = int32(g.source)
	for i, e := range edges {
		if i >= path.deviation {
			if spur, spurWeight, ok := g.spurSearch(v, int32(i), node, g.simpleMaxWeight-rootWeight); ok {
				candidate := make([]int32, 0, i+len(spur))
				candidate = append(append(candidate, edges[:i]...), spur...)
				g.addSimpleCandidate(candidate, rootWeight+spurWeight, i)
			}
		}
		rootWeight += g.edgeWeight[e]
		node = node.child(e)
		v = g.edgeHead[e]
	}
}

func (g *Graph) addSimpleCandidate(edges []int32, weight int64, deviation int) {
	key := make([]byte, 4*len(edges))
	for i, e := range edges {
		binary.LittleEndian.PutUint32(key[4*i:], uint32(e))
	}
	if _, seen := g.simpleSeen[string(key)]; seen {
		return // same path reached from another root
	}
	g.simpleSeen[string(key)] = struct{}{}
	g.simpleCandidates.Enqueue(&simpleCandidate{edges: edges, weight: weight, deviation: deviation}, weight)
}

// spurSearch is an A* search from the spur vertex (at position of the path) to the goal, with the
// root's vertices and the trie children's edges blocked, guided by the exact distances to the goal
// (a consistent lower bound). It returns the edges and weight of the spur; ok is false if there is
// none within maxCost (corridor).
func (g *Graph) spurSearch(spurVertex, position int32, root *trieNode, maxCost int64) (spur []int32, cost int64, ok bool) {
	query := g.queryStamp
	if maxCost < 0 || g.stamp[spurVertex] != query || g.distance[spurVertex] > maxCost {
		return nil, 0, false
	}
	g.spurSearches++
	g.spurStamp++
	if g.spurStamp == 1<<31-1 {
		clear(g.spurOpenStamp)
		clear(g.spurClosedStamp)
		g.spurStamp = 1
	}
	search := g.spurStamp

	open := g.spurOpen
	for open.Len() > 0 { // reuse the queue
		open.Dequeue()
	}
	g.spurCost[spurVertex] = 0
	g.spurParent[spurVertex] = -1
	g.spurOpenStamp[spurVertex] = search
	open.Enqueue(spurVertex, g.distance[spurVertex])
	for {
		v, estimate, more := open.Dequeue()
		if !more {
			return nil, 0, false
		}
		if g.spurClosedStamp[v] == search {
			continue
		}
		if estimate > maxCost {
			return nil, 0, false // consistent bound: every remaining path is longer
		}
		g.spurClosedStamp[v] = search
		if int(v) == g.target {
			for x := v; g.spurParent[x] >= 0; x = g.edgeTail[g.spurParent[x]] {
				spur = append(spur, g.spurParent[x])
			}
			slices.Reverse(spur)
			return spur, g.spurCost[v], true
		}
		for _, e := range g.outEdges[g.outStart[v]:g.outStart[v+1]] {
			head := g.edgeHead[e]
			if g.edgeWeight[e] < 0 || g.stamp[head] != query || g.spurClosedStamp[head] == search {
				continue // negative, cannot reach the goal (or outside the corridor), or done
			}
			if v == spurVertex && root.children[e] != nil {
				continue // a found path with the same root leaves along this edge
			}
			if g.onPathStamp[head] == g.pathStamp && g.onPathPosition[head] < position {
				continue // vertex of the root: the path would not be simple
			}
			newCost := g.spurCost[v] + g.edgeWeight[e]
			if g.spurOpenStamp[head] == search && g.spurCost[head] <= newCost {
				continue
			}
			g.spurOpenStamp[head] = search
			g.spurCost[head] = newCost
			g.spurParent[head] = e
			open.Enqueue(head, newCost+g.distance[head])
		}
	}
}
