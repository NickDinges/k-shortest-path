using System.Text;

namespace System.Collections.Generic
{
    /// <summary>
    /// Generic priority queue, based on a binary min-heap
    /// </summary>
    /// <typeparam name="TObj">Type of data object, must implement IComparable interface</typeparam>
    /// <remarks>
    /// Lower weights in queue have higher priority. Enqueue and Dequeue take O(log n).
    /// Elements comparing as equal are dequeued in insertion order.
    /// </remarks>
    class PriorityQueue<TObj>
    {
        /// <summary>
        /// Heap entry: object plus insertion number, used to keep insertion order among equal objects
        /// </summary>
        private struct Entry
        {
            public TObj Obj;
            public long Sequence;
        }

        /// <summary>
        /// Heap array: element i has children 2i+1 and 2i+2, root (minimum) at index 0
        /// </summary>
        private List<Entry> Heap = null;
        /// <summary>
        /// Comparer for TObj, uses its IComparable implementation
        /// </summary>
        private readonly IComparer<TObj> Comparer = Comparer<TObj>.Default;
        /// <summary>
        /// Insertion counter
        /// </summary>
        private long NextSequence = 0;

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
        /// <param name="_queueSize">Initial capacity of queue</param>
        /// <exception cref="System.Exception">Throws when TObj does not implement IComparable interface</exception>
        public PriorityQueue(int _queueSize)
        {
            if (typeof(TObj).GetInterface("IComparable") == null)
                throw new Exception("PriorityQueue: Templated class " + typeof(TObj) + " does not implement IComparable.");

            Heap = new List<Entry>(Math.Max(_queueSize, 0));
        }

        /// <summary>
        /// Returns the elements in priority order, separated by commas (lowest weight last)
        /// </summary>
        public override string ToString()
        {
            List<Entry> sorted = new List<Entry>(Heap);
            sorted.Sort(Compare);
            StringBuilder result = new StringBuilder();
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                if (i < sorted.Count - 1)
                    result.Append(',');
                result.Append(sorted[i].Obj);
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
        /// Inserts an element in the queue, according with its weight
        /// </summary>
        /// <param name="_obj">Object to enqueue, ignores if it is null</param>
        public void Enqueue(TObj _obj)
        {
            if (_obj == null)
                return;

            Heap.Add(new Entry { Obj = _obj, Sequence = NextSequence++ });

            // Sift up
            int i = Heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (Compare(Heap[i], Heap[parent]) >= 0)
                    break;
                Swap(i, parent);
                i = parent;
            }
        }
        /// <summary>
        /// Extracts element from start of the queue
        /// </summary>
        /// <returns>Reference to enqueued object, null if queue is empty</returns>
        public TObj Dequeue()
        {
            if (Heap.Count == 0)
                return default(TObj);

            TObj result = Heap[0].Obj;
            int last = Heap.Count - 1;
            Heap[0] = Heap[last];
            Heap.RemoveAt(last);

            // Sift down
            int i = 0;
            int count = Heap.Count;
            while (true)
            {
                int left = 2 * i + 1;
                if (left >= count)
                    break;
                int smallest = left;
                int right = left + 1;
                if (right < count && Compare(Heap[right], Heap[left]) < 0)
                    smallest = right;
                if (Compare(Heap[smallest], Heap[i]) >= 0)
                    break;
                Swap(i, smallest);
                i = smallest;
            }
            return result;
        }
        /// <summary>
        /// Returns a reference to element from start of queue, without removing it
        /// </summary>
        /// <returns>Reference to enqueued object, null if queue is empty</returns>
        public TObj Root()
        {
            if (Heap.Count == 0)
                return default(TObj);

            return Heap[0].Obj;
        }

        /// <summary>
        /// Compares two entries by object, then by insertion order
        /// </summary>
        private int Compare(Entry _a, Entry _b)
        {
            int result = Comparer.Compare(_a.Obj, _b.Obj);
            return result != 0 ? result : _a.Sequence.CompareTo(_b.Sequence);
        }
        /// <summary>
        /// Swaps two heap entries
        /// </summary>
        private void Swap(int _i, int _j)
        {
            Entry tmp = Heap[_i];
            Heap[_i] = Heap[_j];
            Heap[_j] = tmp;
        }
    }
}
