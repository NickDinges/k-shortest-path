using System.Text;

namespace System.Collections.Generic
{
    /// <summary>
    /// Generic class for priority queue, it is based on a limited sorted array
    /// </summary>
    /// <typeparam name="TObj">Type of data object, must implement IComparable interface</typeparam>
    /// <remarks>Lower weights in queue has higher priority</remarks>
    class PriorityQueue<TObj>
    {
        /// <summary>
        /// Sorted list of objects, acts like a binary tree
        /// </summary>
        private List<TObj> Queue = null;

        /// <summary>
        /// Returns count of elements in queue
        /// </summary>
        public int Count
        {
            get { return Queue.Count; }
        }

        /// <summary>
        /// Public constructor
        /// </summary>
        /// <param name="_queueSize">Maximum size of queue</param>
        /// <exception cref="System.Exception">Throws when TObj does not implement IComparable interface</exception>
        public PriorityQueue(int _queueSize)
        {
            if (typeof(TObj).GetInterface("IComparable") == null)
                throw new Exception("PriorityQueue: Templated class " + typeof(TObj) + " does not implement IComparable.");
            
            Queue = new List<TObj>(_queueSize);
        }

        public override string ToString()
        {
            StringBuilder result = new StringBuilder();
            for (int i = this.Count-1; i >= 0; i--)
            {
                if (i < this.Count-1)
                    result.Append(',');
                result.Append(Queue[i]);
            }
            return result.ToString();
        }

        /// <summary>
        /// Clears all elements in the queue
        /// </summary>
        public void Clear()
        {
            Queue.Clear();
        }
        /// <summary>
        /// Inserts an element in the queue, in the proper position according with weight
        /// </summary>
        /// <param name="_obj">Object to enqueue, ignores if it is null</param>
        public void Enqueue(TObj _obj)
        {
            if (_obj == null)
                return;

            if (Queue.Count == 0) // Fast special case for empty queue
            {
                Queue.Add(_obj);
                return;
            }

            // BinarySearch for element or best position
            int posNew = Queue.BinarySearch(_obj);

            // Inserts element in proper sorted position
            if (posNew >= 0)   // Similar element exists on queue, inserts new before
                Queue.Insert(posNew, _obj);
            else
            {
                posNew = ~posNew;  // Binary invertion, as specified in BinarySearch() method help
                if (posNew == Queue.Count)
                    Queue.Add(_obj);
                else
                    Queue.Insert(posNew, _obj);
            }
        }
        /// <summary>
        /// Extracts element from start of the queue
        /// </summary>
        /// <returns>Reference to enqueued object, null if queue is empty</returns>
        public TObj Dequeue()
        {
            if (Queue.Count == 0)
                return default(TObj);

            TObj _obj = Queue[0];
            Queue.RemoveAt(0);

            return _obj;
        }
        /// <summary>
        /// Returns a reference to element from start of queue, without removing it
        /// </summary>
        /// <returns>Reference to enqueued object, null if queue is empty</returns>
        public TObj Root()
        {
            if (Queue.Count == 0)
                return default(TObj);

            return Queue[0];
        }
    }
}
