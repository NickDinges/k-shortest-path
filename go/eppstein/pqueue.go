package eppstein

// queueItem is an element of the priority queue: a value with its weight.
// seq keeps insertion order among equal weights (deterministic results).
type queueItem[T any] struct {
	value  T
	weight int64
	seq    uint64
}

// priorityQueue is a min-priority queue (binary heap): lower weights first.
// It is implemented directly instead of with container/heap to avoid an allocation
// per element (container/heap boxes every element into an interface).
type priorityQueue[T any] struct {
	items []queueItem[T]
	seq   uint64
}

func newPriorityQueue[T any](capacity int) *priorityQueue[T] {
	return &priorityQueue[T]{items: make([]queueItem[T], 0, capacity)}
}

// Len returns the number of elements in the queue.
func (q *priorityQueue[T]) Len() int { return len(q.items) }

func (q *priorityQueue[T]) less(i, j int) bool {
	a, b := &q.items[i], &q.items[j]
	if a.weight != b.weight {
		return a.weight < b.weight
	}
	return a.seq < b.seq
}

// Enqueue inserts a value with the given weight.
func (q *priorityQueue[T]) Enqueue(value T, weight int64) {
	q.seq++
	q.items = append(q.items, queueItem[T]{value: value, weight: weight, seq: q.seq})
	for i := len(q.items) - 1; i > 0; { // sift up
		parent := (i - 1) / 2
		if !q.less(i, parent) {
			break
		}
		q.items[i], q.items[parent] = q.items[parent], q.items[i]
		i = parent
	}
}

// Dequeue extracts the value with the lowest weight, ok is false if the queue is empty.
func (q *priorityQueue[T]) Dequeue() (value T, weight int64, ok bool) {
	n := len(q.items)
	if n == 0 {
		return value, 0, false
	}
	top := q.items[0]
	q.items[0] = q.items[n-1]
	q.items[n-1] = queueItem[T]{} // release references for the garbage collector
	q.items = q.items[:n-1]
	n--
	for i := 0; ; { // sift down
		left := 2*i + 1
		if left >= n {
			break
		}
		smallest := left
		if right := left + 1; right < n && q.less(right, left) {
			smallest = right
		}
		if !q.less(smallest, i) {
			break
		}
		q.items[i], q.items[smallest] = q.items[smallest], q.items[i]
		i = smallest
	}
	return top.value, top.weight, true
}

// Peek returns the weight of the lowest element without removing it, ok is false if the queue is
// empty.
func (q *priorityQueue[T]) Peek() (weight int64, ok bool) {
	if len(q.items) == 0 {
		return 0, false
	}
	return q.items[0].weight, true
}
