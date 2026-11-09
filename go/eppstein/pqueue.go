package eppstein

import "container/heap"

// queueItem is an element of the priority queue: a value with its weight.
// seq keeps insertion order among equal weights (deterministic results).
type queueItem[T any] struct {
	value  T
	weight int64
	seq    uint64
}

type itemHeap[T any] []queueItem[T]

func (h itemHeap[T]) Len() int { return len(h) }
func (h itemHeap[T]) Less(i, j int) bool {
	if h[i].weight != h[j].weight {
		return h[i].weight < h[j].weight
	}
	return h[i].seq < h[j].seq
}
func (h itemHeap[T]) Swap(i, j int) { h[i], h[j] = h[j], h[i] }
func (h *itemHeap[T]) Push(x any)   { *h = append(*h, x.(queueItem[T])) }
func (h *itemHeap[T]) Pop() any {
	old := *h
	n := len(old)
	item := old[n-1]
	*h = old[:n-1]
	return item
}

// priorityQueue is a min-priority queue (binary heap): lower weights first.
type priorityQueue[T any] struct {
	items itemHeap[T]
	seq   uint64
}

func newPriorityQueue[T any](capacity int) *priorityQueue[T] {
	return &priorityQueue[T]{items: make(itemHeap[T], 0, capacity)}
}

// Len returns the number of elements in the queue.
func (q *priorityQueue[T]) Len() int { return len(q.items) }

// Enqueue inserts a value with the given weight.
func (q *priorityQueue[T]) Enqueue(value T, weight int64) {
	q.seq++
	heap.Push(&q.items, queueItem[T]{value: value, weight: weight, seq: q.seq})
}

// Dequeue extracts the value with the lowest weight, ok is false if the queue is empty.
func (q *priorityQueue[T]) Dequeue() (value T, weight int64, ok bool) {
	if len(q.items) == 0 {
		return value, 0, false
	}
	item := heap.Pop(&q.items).(queueItem[T])
	return item.value, item.weight, true
}
