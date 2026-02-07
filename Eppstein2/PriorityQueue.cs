using System.Text;

namespace System.Collections.Generic
{
    /// <summary>
    /// Generic binary min-heap priority queue
    /// </summary>
    /// <typeparam name="TObj">Type of data object, must implement IComparable interface</typeparam>
    /// <remarks>Lower weights in queue has higher priority.
    /// Enqueue and Dequeue are O(log n); Root (peek) is O(1).</remarks>
    class PriorityQueue<TObj>
    {
        /// <summary>
        /// Array-backed binary min-heap
        /// </summary>
        private List<TObj> Heap;

        /// <summary>
        /// Returns count of elements in queue
        /// </summary>
        public int Count
        {
            get { return Heap.Count; }
        }

        /// <summary>
        /// Public constructor
        /// </summary>
        /// <param name="_queueSize">Initial capacity hint</param>
        /// <exception cref="System.Exception">Throws when TObj does not implement IComparable interface</exception>
        public PriorityQueue(int _queueSize)
        {
            if (typeof(TObj).GetInterface("IComparable") == null)
                throw new Exception("PriorityQueue: Templated class " + typeof(TObj) + " does not implement IComparable.");

            Heap = new List<TObj>(_queueSize);
        }

        public override string ToString()
        {
            StringBuilder result = new StringBuilder();
            for (int i = this.Count - 1; i >= 0; i--)
            {
                if (i < this.Count - 1)
                    result.Append(',');
                result.Append(Heap[i]);
            }
            return result.ToString();
        }

        /// <summary>
        /// Clears all elements in the queue
        /// </summary>
        public void Clear()
        {
            Heap.Clear();
        }

        /// <summary>
        /// Inserts an element into the heap — O(log n)
        /// </summary>
        /// <param name="_obj">Object to enqueue, ignores if it is null</param>
        public void Enqueue(TObj _obj)
        {
            if (_obj == null)
                return;

            // Add to end of heap, then sift up
            Heap.Add(_obj);
            SiftUp(Heap.Count - 1);
        }

        /// <summary>
        /// Extracts the minimum element from the heap — O(log n)
        /// </summary>
        /// <returns>Reference to enqueued object, null if queue is empty</returns>
        public TObj Dequeue()
        {
            if (Heap.Count == 0)
                return default(TObj);

            TObj min = Heap[0];
            int last = Heap.Count - 1;

            if (last == 0)
            {
                Heap.RemoveAt(0);
                return min;
            }

            // Move last element to root and sift down
            Heap[0] = Heap[last];
            Heap.RemoveAt(last);
            SiftDown(0);

            return min;
        }

        /// <summary>
        /// Returns a reference to the minimum element without removing it — O(1)
        /// </summary>
        /// <returns>Reference to enqueued object, null if queue is empty</returns>
        public TObj Root()
        {
            if (Heap.Count == 0)
                return default(TObj);

            return Heap[0];
        }

        /// <summary>
        /// Restores heap property by moving an element up toward the root
        /// </summary>
        private void SiftUp(int index)
        {
            TObj item = Heap[index];
            IComparable comparable = (IComparable)item;

            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (comparable.CompareTo(Heap[parent]) >= 0)
                    break;

                Heap[index] = Heap[parent];
                index = parent;
            }

            Heap[index] = item;
        }

        /// <summary>
        /// Restores heap property by moving an element down from the root
        /// </summary>
        private void SiftDown(int index)
        {
            int count = Heap.Count;
            TObj item = Heap[index];
            IComparable comparable = (IComparable)item;

            while (true)
            {
                int left = (index << 1) + 1;
                if (left >= count)
                    break;

                int right = left + 1;
                int smallest = left;

                if (right < count && ((IComparable)Heap[left]).CompareTo(Heap[right]) > 0)
                    smallest = right;

                if (comparable.CompareTo(Heap[smallest]) <= 0)
                    break;

                Heap[index] = Heap[smallest];
                index = smallest;
            }

            Heap[index] = item;
        }
    }
}
