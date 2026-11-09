namespace Eppstein
{
    /// <summary>
    /// Node of a persistent leftist heap of sidetracks, ordered by delta (Eppstein's H_T)
    /// </summary>
    /// <remarks>
    /// For every vertex v, the heap H(v) contains all sidetracks whose tail lies on the shortest
    /// path tree path from v to t. H(v) is H(next(v)) plus the sidetracks leaving v. Merging is
    /// persistent: it copies only the O(log n) nodes on the merge path, so H(v) shares almost all
    /// nodes with H(next(v)).
    ///
    /// Path enumeration then needs at most 3 new candidates per extracted path: the two heap
    /// children of the last sidetrack (alternatives to it) and the root of H(head of the last
    /// sidetrack) (one more sidetrack after it).
    /// </remarks>
    internal sealed class SidetrackHeapNode
    {
        /// <summary>
        /// Sidetrack edge
        /// </summary>
        public readonly Edge Edge;
        /// <summary>
        /// Delta of the sidetrack (extra weight compared to the shortest path), heap key
        /// </summary>
        public readonly long Key;
        /// <summary>
        /// Length of the right spine (leftist property: rank(left) >= rank(right))
        /// </summary>
        public int Rank;
        /// <summary>
        /// Children, keys are greater or equal than this node's key
        /// </summary>
        public SidetrackHeapNode Left, Right;

        /// <summary>
        /// Creates a single node heap
        /// </summary>
        public SidetrackHeapNode(Edge _edge, long _key)
        {
            this.Edge = _edge;
            this.Key = _key;
            this.Rank = 1;
        }
        /// <summary>
        /// Copy constructor (for persistent merge)
        /// </summary>
        private SidetrackHeapNode(SidetrackHeapNode _other)
        {
            this.Edge = _other.Edge;
            this.Key = _other.Key;
            this.Rank = _other.Rank;
            this.Left = _other.Left;
            this.Right = _other.Right;
        }

        /// <summary>
        /// Rank of a heap, 0 for an empty heap
        /// </summary>
        private static int RankOf(SidetrackHeapNode _h)
        {
            return _h == null ? 0 : _h.Rank;
        }
        /// <summary>
        /// Merges two heaps without modifying them (persistent)
        /// </summary>
        /// <returns>New heap containing all nodes of both heaps</returns>
        /// <remarks>Recursion depth is at most rank(a) + rank(b), i.e. O(log n)</remarks>
        public static SidetrackHeapNode Merge(SidetrackHeapNode _a, SidetrackHeapNode _b)
        {
            if (_a == null)
                return _b;
            if (_b == null)
                return _a;
            if (_b.Key < _a.Key)
            {
                SidetrackHeapNode tmp = _a;
                _a = _b;
                _b = tmp;
            }

            SidetrackHeapNode result = new SidetrackHeapNode(_a);
            result.Right = Merge(_a.Right, _b);
            if (RankOf(result.Left) < RankOf(result.Right))
            {
                SidetrackHeapNode tmp = result.Left;
                result.Left = result.Right;
                result.Right = tmp;
            }
            result.Rank = RankOf(result.Right) + 1;
            return result;
        }
        /// <summary>
        /// Builds a heap from nodes sorted by ascending key, as a chain of left children
        /// </summary>
        /// <remarks>A left-only chain is a valid leftist heap with rank 1 and costs O(1) per node</remarks>
        public static SidetrackHeapNode FromSorted(System.Collections.Generic.List<SidetrackHeapNode> _sorted)
        {
            SidetrackHeapNode root = null;
            for (int i = _sorted.Count - 1; i >= 0; i--)
            {
                _sorted[i].Left = root;
                root = _sorted[i];
            }
            return root;
        }
    }
}
