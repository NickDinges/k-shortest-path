package eppstein

import "math"

// Contraction hierarchy (CH) with PHAST: an optional speed-up of the shortest-path tree
// computation.
//
// Preprocessing contracts the vertices one after the other, from unimportant to important. When a
// vertex v is removed, a shortcut u->x with weight w(u,v) + w(v,x) is added for each pair of
// neighbors whose shortest connection ran through v (a local "witness search" checks this). The
// position in this order is the vertex's rank. Every shortest path then has an equivalent path in
// the graph with shortcuts that first only goes up in rank and then only down.
//
// PHAST (Delling, Goldberg, Nowatzyk, Werneck) computes the distances of all vertices to the goal
// from that: a small search from the goal over edges to higher-ranked vertices, followed by one
// linear sweep over all vertices from the highest to the lowest rank, without a priority queue.
// The shortest-path tree is then built from the original edges whose weight matches the
// distances. Results are exactly the same as with Dijkstra (paths of equal weight may come in a
// different order).

const (
	witnessLimitOrdering    = 50  // settled-vertex limit of a witness search while choosing the order
	witnessLimitContraction = 500 // settled-vertex limit of a witness search while contracting
)

// chState holds the contraction hierarchy. Vertices are stored by sweep position
// p = n - 1 - rank, so the sweep runs through the slices in order.
type chState struct {
	chEnabled       bool
	chValid         bool
	chShortcutCount int
	chPosition      []int32 // vertex -> sweep position
	chUpStart       []int32 // edges to higher-ranked vertices, by position
	chUpTarget      []int32 // ... as sweep position of the head
	chUpWeight      []int64
	chDownStart     []int32 // edges from higher-ranked vertices, by vertex
	chDownSource    []int32 // ... their tails (vertex indices)
	chDownWeight    []int64
	chDistance      []int64 // by sweep position, reused per query
	chQueue         []int32 // breadth-first queue of the tree construction, reused
}

// ContractionHierarchyEnabled tells if the contraction hierarchy is enabled.
func (g *Graph) ContractionHierarchyEnabled() bool { return g.chEnabled }

// ContractionHierarchyShortcuts returns the number of shortcut edges of the contraction hierarchy
// (0 if it is not computed).
func (g *Graph) ContractionHierarchyShortcuts() int {
	if !g.chValid {
		return 0
	}
	return g.chShortcutCount
}

// EnableContractionHierarchy enables the contraction hierarchy: the shortest-path tree of a query
// (all distances to the goal) is computed with PHAST instead of Dijkstra.
//
// Worthwhile for many queries without a corridor on large graphs, where computing the distances of
// all vertices dominates. With a corridor (SetMaxDetour, SetMaxDetourRatio) the library keeps
// using Dijkstra, because it then only visits the vertices inside the corridor, while PHAST always
// visits all vertices. The preprocessing runs on the next query (or with
// PrecomputeContractionHierarchy) and again after every change of the graph, including weight
// changes. Results are exactly the same as without it. Arc flags are not used while the
// contraction hierarchy is enabled.
func (g *Graph) EnableContractionHierarchy() {
	g.chEnabled = true
	g.ready = false
}

// DisableContractionHierarchy disables the contraction hierarchy and releases its memory.
func (g *Graph) DisableContractionHierarchy() {
	g.chState = chState{}
	g.ready = false
}

// PrecomputeContractionHierarchy runs the contraction now (otherwise it runs on the next query).
// It returns false if the contraction hierarchy is disabled.
func (g *Graph) PrecomputeContractionHierarchy() bool {
	g.ensureContractionHierarchy()
	return g.chEnabled && g.chValid
}

// usePhast tells if the next shortest-path tree is computed with PHAST.
func (g *Graph) usePhast() bool {
	return g.chEnabled && !g.hasMaxDetour && !g.hasMaxDetourRatio
}

func (g *Graph) ensureContractionHierarchy() {
	if !g.chEnabled || g.chValid {
		return
	}
	g.contract()
	g.chValid = true
}

type chEdge struct {
	other  int32 // head of an outgoing, tail of an incoming edge
	weight int64
}

// contract builds the contraction hierarchy: it chooses the order lazily by priority (edge
// difference plus contracted neighbors) and adds the shortcuts.
func (g *Graph) contract() {
	n := len(g.labels)
	state := newContraction(g, n)
	queue := newPriorityQueue[int32](n)
	key := func(priority int64, v int32) int64 { return priority*int64(n) + int64(v) } // by priority, then vertex
	for v := range int32(n) {
		queue.Enqueue(v, key(state.priority(v), v))
	}

	rank := make([]int32, n)
	up := make([][]chEdge, n)
	down := make([][]chEdge, n)
	next := int32(0)
	g.chShortcutCount = 0
	var shortcuts []shortcut
	for {
		v, _, ok := queue.Dequeue()
		if !ok {
			break
		}
		if state.contracted[v] {
			continue
		}
		k := key(state.priority(v), v) // lazy update: re-check before contracting
		if top, ok := queue.Peek(); ok && k > top {
			queue.Enqueue(v, k)
			continue
		}

		shortcuts = state.findShortcuts(v, witnessLimitContraction, shortcuts[:0])
		for _, s := range shortcuts {
			state.addEdge(s.tail, s.head, s.weight)
		}
		g.chShortcutCount += len(shortcuts)

		// the remaining neighbors all get a higher rank: these are v's upward edges
		up[v] = state.live(state.out[v])
		down[v] = state.live(state.in[v])
		state.contracted[v] = true
		rank[v] = next
		next++
		for _, e := range up[v] {
			state.contractedNeighbors[e.other]++
		}
		for _, e := range down[v] {
			state.contractedNeighbors[e.other]++
		}
	}

	// store by sweep position: highest rank first
	g.chPosition = make([]int32, n)
	vertexAt := make([]int32, n)
	for v := range n {
		g.chPosition[v] = int32(n-1) - rank[v]
		vertexAt[g.chPosition[v]] = int32(v)
	}
	g.chUpStart = make([]int32, n+1)
	g.chDownStart = make([]int32, n+1)
	for p := range n {
		g.chUpStart[p+1] = g.chUpStart[p] + int32(len(up[vertexAt[p]]))
	}
	for v := range n {
		g.chDownStart[v+1] = g.chDownStart[v] + int32(len(down[v]))
	}
	g.chUpTarget = make([]int32, 0, g.chUpStart[n])
	g.chUpWeight = make([]int64, 0, g.chUpStart[n])
	for p := range n {
		for _, e := range up[vertexAt[p]] {
			g.chUpTarget = append(g.chUpTarget, g.chPosition[e.other])
			g.chUpWeight = append(g.chUpWeight, e.weight)
		}
	}
	g.chDownSource = make([]int32, 0, g.chDownStart[n])
	g.chDownWeight = make([]int64, 0, g.chDownStart[n])
	for v := range n {
		for _, e := range down[v] {
			g.chDownSource = append(g.chDownSource, e.other)
			g.chDownWeight = append(g.chDownWeight, e.weight)
		}
	}
	g.chDistance = make([]int64, n)
}

type shortcut struct {
	tail, head int32
	weight     int64
}

// contraction is the graph during contraction: adjacency lists over the vertices not yet
// contracted (lists may contain contracted vertices, which are skipped), and witness searches.
type contraction struct {
	contracted          []bool
	contractedNeighbors []int64
	out, in             [][]chEdge

	// witness search
	distance []int64
	seen     []int32
	search   int32
	queue    *priorityQueue[int32]
	scratch  []shortcut
}

func newContraction(g *Graph, n int) *contraction {
	c := &contraction{
		contracted:          make([]bool, n),
		contractedNeighbors: make([]int64, n),
		out:                 make([][]chEdge, n),
		in:                  make([][]chEdge, n),
		distance:            make([]int64, n),
		seen:                make([]int32, n),
		queue:               newPriorityQueue[int32](64),
	}
	for e := range g.edgeTail {
		// negative: ignored; self-loops: never needed
		if g.edgeWeight[e] >= 0 && g.edgeTail[e] != g.edgeHead[e] {
			c.addEdge(g.edgeTail[e], g.edgeHead[e], g.edgeWeight[e])
		}
	}
	return c
}

// addEdge adds an edge, or lowers the weight of an existing parallel edge.
func (c *contraction) addEdge(tail, head int32, weight int64) {
	for i, e := range c.out[tail] {
		if e.other == head {
			if weight < e.weight {
				c.out[tail][i].weight = weight
				for j, f := range c.in[head] {
					if f.other == tail {
						c.in[head][j].weight = weight
					}
				}
			}
			return
		}
	}
	c.out[tail] = append(c.out[tail], chEdge{head, weight})
	c.in[head] = append(c.in[head], chEdge{tail, weight})
}

// live returns a copy of the edges whose other end is not contracted.
func (c *contraction) live(edges []chEdge) []chEdge {
	var result []chEdge
	for _, e := range edges {
		if !c.contracted[e.other] {
			result = append(result, e)
		}
	}
	return result
}

// priority for the order: shortcuts needed minus edges removed (edge difference), plus the number
// of contracted neighbors (spreads the contraction evenly).
func (c *contraction) priority(v int32) int64 {
	c.scratch = c.findShortcuts(v, witnessLimitOrdering, c.scratch[:0])
	removed := int64(len(c.live(c.out[v])) + len(c.live(c.in[v])))
	return 2*int64(len(c.scratch)) - removed + c.contractedNeighbors[v]
}

// findShortcuts appends the shortcuts needed when v is contracted: for every live in-neighbor u
// and out-neighbor x, a shortcut u->x unless a witness search from u (avoiding v) finds a path of
// at most w(u,v) + w(v,x). An incomplete search (settled limit) adds the shortcut, which is always
// correct, only not minimal.
func (c *contraction) findShortcuts(v int32, settledLimit int, result []shortcut) []shortcut {
	for _, in := range c.in[v] {
		u := in.other
		if c.contracted[u] {
			continue
		}
		maxTotal, anyTarget := int64(0), false
		for _, out := range c.out[v] {
			if !c.contracted[out.other] && out.other != u {
				maxTotal = max(maxTotal, saturatingAdd(in.weight, out.weight))
				anyTarget = true
			}
		}
		if !anyTarget {
			continue
		}
		c.witnessSearch(u, v, maxTotal, settledLimit)
		for _, out := range c.out[v] {
			x := out.other
			if c.contracted[x] || x == u {
				continue
			}
			viaV := saturatingAdd(in.weight, out.weight)
			if c.seen[x] != c.search || c.distance[x] > viaV {
				result = append(result, shortcut{u, x, viaV})
			}
		}
	}
	return result
}

// witnessSearch runs Dijkstra from source over live vertices except the one being contracted.
func (c *contraction) witnessSearch(source, avoid int32, maxDistance int64, settledLimit int) {
	c.search++
	if c.search == math.MaxInt32 {
		clear(c.seen)
		c.search = 1
	}
	c.queue.items = c.queue.items[:0]
	c.seen[source] = c.search
	c.distance[source] = 0
	c.queue.Enqueue(source, 0)
	settled := 0
	for {
		v, d, ok := c.queue.Dequeue()
		if !ok {
			break
		}
		if d > c.distance[v] {
			continue // outdated entry
		}
		settled++
		if d > maxDistance || settled > settledLimit {
			break
		}
		for _, e := range c.out[v] {
			x := e.other
			if c.contracted[x] || x == avoid {
				continue
			}
			nd := saturatingAdd(d, e.weight)
			if c.seen[x] != c.search || nd < c.distance[x] {
				c.seen[x] = c.search
				c.distance[x] = nd
				c.queue.Enqueue(x, nd)
			}
		}
	}
}

// buildShortestPathTreePhast runs PHAST towards the goal: an upward search from the goal over edges
// coming from higher-ranked vertices, then one sweep from the highest to the lowest rank.
// Afterwards every vertex that can reach the goal has its exact distance; the tree is built from
// tight original edges.
func (g *Graph) buildShortestPathTreePhast() {
	n := len(g.labels)
	query := g.queryStamp
	d := g.chDistance
	for i := range d {
		d[i] = math.MaxInt64
	}

	// 1. upward search from the goal (in reverse direction: edges x->y with rank(x) > rank(y))
	fringe := newPriorityQueue[int32](64)
	d[g.chPosition[g.target]] = 0
	fringe.Enqueue(int32(g.target), 0)
	for {
		y, dy, ok := fringe.Dequeue()
		if !ok {
			break
		}
		if dy > d[g.chPosition[y]] {
			continue
		}
		for i := g.chDownStart[y]; i < g.chDownStart[y+1]; i++ {
			x := g.chDownSource[i]
			if nd := saturatingAdd(dy, g.chDownWeight[i]); nd < d[g.chPosition[x]] {
				d[g.chPosition[x]] = nd
				fringe.Enqueue(x, nd)
			}
		}
	}

	// 2. sweep: highest rank first; the heads of upward edges are already final
	for p := range n {
		best := d[p]
		for i := g.chUpStart[p]; i < g.chUpStart[p+1]; i++ {
			if dh := d[g.chUpTarget[i]]; dh != math.MaxInt64 {
				best = min(best, saturatingAdd(dh, g.chUpWeight[i]))
			}
		}
		d[p] = best
	}

	// 3. tree from tight original edges, breadth-first from the goal (so zero-weight cycles cannot
	//    make it cyclic). Every vertex with a finite distance has a tight path to the goal.
	g.stamp[g.target] = query
	g.distance[g.target] = 0
	g.treeEdge[g.target] = -1
	bfs := append(g.chQueue[:0], int32(g.target))
	for head := 0; head < len(bfs); head++ {
		y := bfs[head]
		dy := g.distance[y]
		for _, e := range g.inEdges[g.inStart[y]:g.inStart[y+1]] {
			x := g.edgeTail[e]
			if g.stamp[x] == query || g.edgeWeight[e] < 0 {
				continue
			}
			if dx := d[g.chPosition[x]]; dx != math.MaxInt64 && dx == saturatingAdd(g.edgeWeight[e], dy) {
				g.stamp[x] = query
				g.distance[x] = dx
				g.treeEdge[x] = e
				bfs = append(bfs, x)
			}
		}
	}
	g.chQueue = bfs
}
