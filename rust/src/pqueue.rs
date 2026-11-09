//! Min-priority queue (binary heap). Ties are broken by insertion order, so results are
//! deterministic and equal to the C# and Go versions.

pub(crate) struct PriorityQueue<T> {
    items: Vec<(i64, u64, T)>,
    seq: u64,
}

impl<T> PriorityQueue<T> {
    pub(crate) fn new() -> Self {
        PriorityQueue {
            items: Vec::new(),
            seq: 0,
        }
    }

    pub(crate) fn clear(&mut self) {
        self.items.clear();
    }

    #[inline]
    fn less(&self, i: usize, j: usize) -> bool {
        let (a, b) = (&self.items[i], &self.items[j]);
        a.0 < b.0 || (a.0 == b.0 && a.1 < b.1)
    }

    /// Inserts a value with the given weight.
    pub(crate) fn push(&mut self, value: T, weight: i64) {
        self.seq += 1;
        self.items.push((weight, self.seq, value));
        let mut i = self.items.len() - 1;
        while i > 0 {
            let parent = (i - 1) / 2;
            if !self.less(i, parent) {
                break;
            }
            self.items.swap(i, parent);
            i = parent;
        }
    }

    /// Removes the value with the lowest weight.
    pub(crate) fn pop(&mut self) -> Option<(T, i64)> {
        if self.items.is_empty() {
            return None;
        }
        let top = self.items.swap_remove(0);
        let n = self.items.len();
        let mut i = 0;
        loop {
            let left = 2 * i + 1;
            if left >= n {
                break;
            }
            let mut smallest = left;
            if left + 1 < n && self.less(left + 1, left) {
                smallest = left + 1;
            }
            if !self.less(smallest, i) {
                break;
            }
            self.items.swap(i, smallest);
            i = smallest;
        }
        Some((top.2, top.0))
    }

    /// Weight of the lowest element.
    pub(crate) fn peek_weight(&self) -> Option<i64> {
        self.items.first().map(|item| item.0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sorted_and_stable_for_ties() {
        let mut q = PriorityQueue::new();
        for (i, w) in [5, 1, 3, 1, 5, 0].into_iter().enumerate() {
            q.push(i, w);
        }
        let mut order = Vec::new();
        while let Some((v, _)) = q.pop() {
            order.push(v);
        }
        assert_eq!(order, vec![5, 1, 3, 2, 0, 4]);
        assert!(q.peek_weight().is_none());
    }
}
