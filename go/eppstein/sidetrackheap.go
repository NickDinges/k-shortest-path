package eppstein

import "sort"

// heapNode is a node of a persistent leftist heap of sidetracks, ordered by delta
// (Eppstein's H_T).
//
// For every vertex v, the heap H(v) contains all sidetracks whose tail lies on the shortest
// path tree path from v to t: H(v) is H(next(v)) plus the sidetracks leaving v. Merging is
// persistent and copies only the O(log n) nodes on the merge path, so H(v) shares almost all
// nodes with H(next(v)).
//
// Path enumeration then needs at most 3 new candidates per extracted path: the two heap
// children of the last sidetrack (alternatives to it) and the root of H(head of the last
// sidetrack) (one more sidetrack after it).
type heapNode struct {
	edge        *Edge
	key         int64 // delta of the sidetrack
	rank        int   // length of the right spine
	left, right *heapNode
}

func rankOf(h *heapNode) int {
	if h == nil {
		return 0
	}
	return h.rank
}

// mergeHeaps merges two heaps without modifying them (persistent).
// The recursion depth is at most rank(a)+rank(b), i.e. O(log n).
func mergeHeaps(a, b *heapNode) *heapNode {
	if a == nil {
		return b
	}
	if b == nil {
		return a
	}
	if b.key < a.key {
		a, b = b, a
	}
	result := *a // copy
	result.right = mergeHeaps(a.right, b)
	if rankOf(result.left) < rankOf(result.right) {
		result.left, result.right = result.right, result.left
	}
	result.rank = rankOf(result.right) + 1
	return &result
}

// heapFromNodes builds a heap from new nodes: sorted by key and linked as a chain of left
// children, which is a valid leftist heap of rank 1.
func heapFromNodes(nodes []*heapNode) *heapNode {
	sort.SliceStable(nodes, func(i, j int) bool { return nodes[i].key < nodes[j].key })
	var root *heapNode
	for i := len(nodes) - 1; i >= 0; i-- {
		nodes[i].left = root
		root = nodes[i]
	}
	return root
}
