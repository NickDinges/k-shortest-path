using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Eppstein
{
    #region HelperClasses
    /// <summary>
    /// Class for nodes in shortest path tree, with comparing capability
    /// </summary>
    public class SP_Node : IComparable
    {
        /// <summary>
        /// Contained edge
        /// </summary>
        public Edge Edge;
        /// <summary>
        /// Weight of node (edge)
        /// </summary>
        public long Weight;

        /// <summary>
        /// Public constructor
        /// </summary>
        /// <param name="_edge">Edge object</param>
        /// <param name="_weight">Weight of node</param>
        public SP_Node(Edge _edge, long _weight)
        {
            this.Edge = _edge;
            this.Weight = _weight;
        }

        /// <summary>
        /// Implements IComparable's CompareTo method
        /// Compare two SP_Node objects by it weight values
        /// </summary>
        /// <param name="_obj">Object to compare with</param>
        /// <returns>-1 if this is shorter than object, 1 if countersense, 0 if both are equal</returns>
        /// <exception cref="System.Exception">Thrown when obj is not SP_Node type</exception>
        int IComparable.CompareTo(object _obj)
        {
            return this.Weight.CompareTo(((SP_Node)_obj).Weight);
        }
        /// <summary>
        /// Returns node's descriptive string
        /// </summary>
        /// <returns>Edge and weight string</returns>
        public override string ToString()
        {
            return /*this.Edge+*/" ["+Weight+"]";
        }
    }

    /// <summary>
    /// Sidetracks collection, with comparing capability
    /// </summary>
    /// <remarks>
    /// Nodes created during path enumeration only store their parent and one additional
    /// sidetrack, so creating a candidate costs O(1) time and memory instead of copying the
    /// whole sidetrack list. The full list is built on demand by <see cref="Sidetracks"/>.
    /// </remarks>
    public class ST_Node : IComparable
    {
        /// <summary>
        /// Explicit sidetrack collection (root nodes only), null for chained nodes
        /// </summary>
        private readonly GraphPath explicitSidetracks;
        /// <summary>
        /// Parent node (same sidetracks without the last one), null for root nodes
        /// </summary>
        public readonly ST_Node Parent;
        /// <summary>
        /// Last sidetrack of this node, null for root nodes
        /// </summary>
        public readonly Edge LastSidetrack;
        /// <summary>
        /// Weight of node (sum of sidetrack deltas)
        /// </summary>
        public long Weight;
        /// <summary>
        /// Heap node of the last sidetrack, used to find alternative sidetracks (null if not created
        /// by the path enumeration)
        /// </summary>
        internal readonly SidetrackHeapNode HeapNode;

        /// <summary>
        /// Public constructor
        /// </summary>
        /// <param name="_sidetracks">Sidetrack collection</param>
        /// <remarks>Weight is calculated using path's weight</remarks>
        public ST_Node(GraphPath _sidetracks)
        {
            this.explicitSidetracks = _sidetracks;
            this.Weight = _sidetracks.DeltaWeight;
        }
        /// <summary>
        /// Creates a child node: parent's sidetracks plus one more sidetrack
        /// </summary>
        /// <param name="_parent">Parent node</param>
        /// <param name="_sidetrack">Additional sidetrack</param>
        /// <remarks>Weight is parent's weight plus the delta of the new sidetrack</remarks>
        public ST_Node(ST_Node _parent, Edge _sidetrack)
        {
            this.Parent = _parent;
            this.LastSidetrack = _sidetrack;
            this.Weight = _parent.Weight + _sidetrack.Delta;
        }
        /// <summary>
        /// Creates a child node from a sidetrack heap node (used by the path enumeration)
        /// </summary>
        internal ST_Node(ST_Node _parent, SidetrackHeapNode _heapNode)
        {
            this.Parent = _parent;
            this.LastSidetrack = _heapNode.Edge;
            this.HeapNode = _heapNode;
            this.Weight = _parent.Weight + _heapNode.Key;
        }

        /// <summary>
        /// Collection of edges (sidetracks), not exactly a full path
        /// </summary>
        /// <remarks>Built on demand from the chain of parent nodes</remarks>
        public GraphPath Sidetracks
        {
            get
            {
                List<Edge> reversed = new List<Edge>();
                ST_Node node = this;
                while (node.explicitSidetracks == null)
                {
                    reversed.Add(node.LastSidetrack);
                    node = node.Parent;
                }
                GraphPath result = new GraphPath();
                result.AddRange(node.explicitSidetracks);
                for (int i = reversed.Count - 1; i >= 0; i--)
                    result.Add(reversed[i]);
                return result;
            }
        }
        /// <summary>
        /// Head of the last sidetrack, null if there are no sidetracks
        /// </summary>
        public Vertex LastHead
        {
            get
            {
                if (this.LastSidetrack != null)
                    return this.LastSidetrack.Head;
                if (this.explicitSidetracks != null && this.explicitSidetracks.Count > 0)
                    return this.explicitSidetracks[this.explicitSidetracks.Count - 1].Head;
                return null;
            }
        }

        /// <summary>
        /// Implements IComparable's CompareTo method
        /// Compare two ST_Node objects by it weight values
        /// </summary>
        /// <param name="_obj">Object to compare with</param>
        /// <returns>-1 if this is shorter than object, 1 if countersense, 0 if both are equal</returns>
        /// <exception cref="System.Exception">Thrown when obj is not ST_Node type</exception>
        int IComparable.CompareTo(object _obj)
        {
            return this.Weight.CompareTo(((ST_Node)_obj).Weight);
        }
        /// <summary>
        /// Returns node's descriptive string
        /// </summary>
        /// <returns>Weight as string</returns>
        public override string ToString()
        {
            return /*this.Sidetracks+*/" ["+Weight+"]";
        }
    }
    #endregion

    /// <summary>
    /// Contains a directed graph, composed by vertices and directed edges collections
    /// </summary>
    public partial class Graph
    {
        #region Private fields
        /// <summary>
        /// Linear array of vertices
        /// </summary>
        private List<Vertex> Vertices;
        /// <summary>
        /// Vertices by upper case label, for constant time lookup (see GetVertex)
        /// </summary>
        private Dictionary<string, Vertex> VertexIndex;
        /// <summary>
        /// Collections of Sidetracks in graph, ordered by total delta
        /// </summary>
        private PriorityQueue<ST_Node> PathsHeap;
        /// <summary>
        /// Flag to indicate if shortest paths have been recalculated
        /// </summary>
        private bool Ready;
        /// <summary>
        /// Source vertex, is valid after main shortest path calculation
        /// </summary>
        private Vertex SourceVertex;
        /// <summary>
        /// Endpoint vertex, is valid after main shortest path calculation
        /// </summary>
        private Vertex EndVertex;
        /// <summary>
        /// Maximum sum of sidetrack deltas of returned paths (corridor), -1 if no path exists
        /// </summary>
        private long PathBound = long.MaxValue;
        /// <summary>
        /// Maximum absolute detour, see <see cref="MaxDetour"/>
        /// </summary>
        private long maxDetour = long.MaxValue;
        /// <summary>
        /// Maximum relative detour, see <see cref="MaxDetourRatio"/>
        /// </summary>
        private double maxDetourRatio = double.PositiveInfinity;
        /// <summary>
        /// Number of candidate paths created since the last FindShortestPath (for tests and tuning)
        /// </summary>
        internal long CandidatesCreated;
        /// <summary>
        /// True if the incoming/outgoing edge arrays of all vertices match RelatedEdges
        /// </summary>
        private bool AdjacencyValid = false;
        #endregion

        #region Corridor (maximum detour)
        /// <summary>
        /// Maximum detour: only paths at most this much longer than the shortest path are returned
        /// </summary>
        /// <remarks>
        /// Default <c>long.MaxValue</c> (no limit). With a limit, the shortest path tree is only computed
        /// for vertices whose distance to t is at most (shortest path + detour), which makes queries
        /// between nearby vertices of a large graph much faster. FindNextShortestPath returns an empty
        /// path once all paths within the limit have been returned.
        /// Changing it requires a new call of FindShortestPath.
        /// </remarks>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown when value is negative</exception>
        public long MaxDetour
        {
            get { return maxDetour; }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("value", "MaxDetour must not be negative");
                maxDetour = value;
                Ready = false;
            }
        }
        /// <summary>
        /// Maximum detour relative to the shortest path weight, e.g. 0.2 for paths at most 20% longer
        /// </summary>
        /// <remarks>
        /// Default <c>double.PositiveInfinity</c> (no limit). If both MaxDetour and MaxDetourRatio are set,
        /// the smaller limit applies. Changing it requires a new call of FindShortestPath.
        /// </remarks>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown when value is negative or NaN</exception>
        public double MaxDetourRatio
        {
            get { return maxDetourRatio; }
            set
            {
                if (double.IsNaN(value) || value < 0)
                    throw new ArgumentOutOfRangeException("value", "MaxDetourRatio must not be negative");
                maxDetourRatio = value;
                Ready = false;
            }
        }
        /// <summary>
        /// Allowed detour for a given shortest path weight
        /// </summary>
        private long DetourBound(long _shortest)
        {
            long bound = maxDetour;
            if (!double.IsPositiveInfinity(maxDetourRatio))
            {
                double relative = Math.Floor(maxDetourRatio * _shortest);
                if (relative < bound)
                    bound = (long)relative;
            }
            return bound;
        }
        /// <summary>
        /// Adds two non-negative values, saturating at long.MaxValue
        /// </summary>
        private static long SaturatingAdd(long _a, long _b)
        {
            return _a > long.MaxValue - _b ? long.MaxValue : _a + _b;
        }
        #endregion

        #region Public methods
        /// <summary>
        /// Default constructor
        /// </summary>
        public Graph()
        {
            Vertices = new List<Vertex>();
            VertexIndex = new Dictionary<string, Vertex>(StringComparer.Ordinal);
            Ready = false;
        }
        /// <summary>
        /// Creates all vertices in graph by parsing a string, remove all previous vertices and edges on lists
        /// </summary>
        /// <param name="_vertices">String containing a comma-separated list with all vertex labels</param>
        /// <returns>True if all labels well parsed, false if not</returns>
        /// <remarks>A label must start with a letter and have all remain characters letters or digits</remarks>
        /// <remarks>Repeated and empty labels with be ignored</remarks>
        public bool CreateVertices(string _vertices)
        {
            // Scans vertices' string, creates objects, and add to vertices' list
            Ready = false;  // must rebuild trees
            Vertices.Clear();  // Remove all previous vertices
            VertexIndex.Clear();
            AdjacencyValid = false;
            ResetArcFlags();   // Partition refers to removed vertices

            string[] vertexList = _vertices.ToUpper().Split(new char[] { ',' });
            foreach (string _label in vertexList)
            {
                if (_label.Length == 0)  // ignore empty labels
                    continue;
                if (!IsValidLabel(_label))
                    return false;
                if (GetVertex(_label)==null)
                    AppendVertex(_label);
            }

            return true;
        }
        /// <summary>
        /// Creates directional edges
        /// </summary>
        /// <param name="_tails">Comma-separated list of vertices for tails</param>
        /// <param name="_heads">Comma-separated list of vertices for heads</param>
        /// <param name="_weight">Weight for all edges</param>
        /// <param name="_group">Group name for all edges</param>
        /// <returns>True if vertices are valid</returns>
        /// <remarks>Count of tails must match count of heads, all vertices must exist</remarks>
        public bool CreateEdges(string _tails, string _heads, long _weight, string _group)
        {
            List<Vertex> tails = this.ParseVertices(_tails.ToUpper());
            List<Vertex> heads = this.ParseVertices(_heads.ToUpper());
            string group = _group.ToUpper();

            if (tails==null || heads==null)
                return false;
            if (tails.Count != heads.Count)
                return false;

            for (int i = 0; i < tails.Count; i++)
            {
                Edge e = new Edge(tails[i], heads[i], _weight, group);
                tails[i].RelatedEdges.Add(e);
                if (tails[i] != heads[i]) // Avoids duplicated reference for self-pointing edges
                    heads[i].RelatedEdges.Add(e);
            }
            Ready = false;
            AdjacencyValid = false;
            InvalidateArcFlags();  // New arcs need flags

            return true;
        }
        /// <summary>
        /// Change group weights
        /// </summary>
        /// <param name="_group">Group name of edges to be affected</param>
        /// <param name="_weight">New weights</param>
        public void EdgeGroupWeights(string _group, long _weight)
        {
            string group = _group.ToUpper();

            foreach (Vertex _v in Vertices)
                foreach (Edge _e in _v.RelatedEdges)
                    if (_e.Group == group)
                        _e.Weight = _weight;

            this.Ready = false;
            InvalidateArcFlags();  // Shortest paths may have changed
        }
        /// <summary>
        /// Calculates all shortest paths between s and t and returns the first one
        /// </summary>
        /// <param name="_s">Label of initial vertex</param>
        /// <param name="_t">Label of ending vertex</param>
        /// <returns>Directional path if found, empty path if not or labels not valid</returns>
        public GraphPath FindShortestPath(string _s, string _t)
        {
            // Parse start and end vertex strings
            return FindShortestPath(this.GetVertex(_s.ToUpper()), this.GetVertex(_t.ToUpper()));
        }
        /// <summary>
        /// Calculates all shortest paths between two vertices given by index and returns the first one
        /// </summary>
        /// <param name="_s">Index of initial vertex (as returned by AddVertex or GetVertexIndex)</param>
        /// <param name="_t">Index of ending vertex</param>
        /// <returns>Directional path if found, empty path if not or indices not valid</returns>
        public GraphPath FindShortestPath(int _s, int _t)
        {
            return FindShortestPath(VertexAt(_s), VertexAt(_t));
        }
        /// <summary>
        /// Calculates all shortest paths between s and t and returns the first one
        /// </summary>
        /// <param name="_s">Initial vertex, null if not found</param>
        /// <param name="_t">Ending vertex, null if not found</param>
        /// <returns>Directional path if found, empty path if not or vertices are null</returns>
        private GraphPath FindShortestPath(Vertex _s, Vertex _t)
        {
            Ready = false;

            this.SourceVertex = _s;
            this.EndVertex = _t;
            if (SourceVertex == null || EndVertex == null)
                return new GraphPath(); // Invalid path

            // Splits edges into incoming and outgoing arrays if the graph changed
            EnsureAdjacency();
            // Recomputes arc flags lazily if enabled and graph changed since last preprocessing
            EnsureArcFlags();

            // Builds shortest path tree for all vertices to t, according to Dijkstra,
            // storing distance to endpoint information on vertices, as described in Eppstein
            BuildShortestPathTree();
            // Maximum sum of sidetrack deltas (corridor), -1 if t cannot be reached
            this.PathBound = SourceVertex.Distance == Vertex.Unreached ? -1 : DetourBound(SourceVertex.Distance);
            // Initializes the heap of tracks from s to t, as described in Eppstein
            // Paths are defined uniquely by sidetrack collections (edges not in shortest paths)
            // The heap is expanded lazily, each time a path is extracted
            BuildSidetracksHeap();
            // Flag to indicate that shortest paths have been calculated
            Ready = true;

            return FindNextShortestPath();
        }
        /// <summary>
        /// Recover next shortest path
        /// </summary>
        /// <returns>Directional path if available, empty path if not remaining paths</returns>
        /// <remarks>Paths may repeat vertices and edges (as in Eppstein), so a graph with a cycle
        /// reachable between s and t has infinitely many paths: limit the number of calls</remarks>
        public GraphPath FindNextShortestPath()
        {
            if (!Ready)
                return new GraphPath();  // Invalid path

            // Pick next track from heap, it is ordered from shortest path to longest
            ST_Node node = this.PathsHeap.Dequeue();
            if (node == null)
                return new GraphPath(); // Invalid path

            // Lazy expansion: enqueues at most 3 successors of this track
            AddSidetracks(node);

            // Returns path reconstructed from sidetracks
            return RebuildPath(node.Sidetracks);
        }
        #endregion

        #region Private methods
        /// <summary>
        /// Parses a comma-separated list of vertices
        /// </summary>
        /// <param name="_labels">Comma-separated list of vertex labels</param>
        /// <returns>List of vertices, null if some vertex does not exist</returns>
        private List<Vertex> ParseVertices(string _labels)
        {
            string[] vertexList = _labels.ToUpper().Split(new char[] { ',' });
            if (vertexList.Length == 0)
                return null;

            List<Vertex> result = new List<Vertex>();
            foreach (string _label in vertexList)
            {
                Vertex v = this.GetVertex(_label);  // Check if label exists
                if (v==null)
                    return null;
                result.Add(v);
            }

            return result;
        }
        /// <summary>
        /// Tells if a label is valid: starts with a letter, then only letters or digits
        /// </summary>
        private static bool IsValidLabel(string _label)
        {
            if (string.IsNullOrEmpty(_label) || !char.IsLetter(_label, 0))
                return false;
            foreach (char c in _label)
                if (!char.IsLetterOrDigit(c))
                    return false;
            return true;
        }
        /// <summary>
        /// Adds a new vertex with an upper case label, which must be valid and not used yet
        /// </summary>
        /// <returns>The new vertex</returns>
        private Vertex AppendVertex(string _label)
        {
            Vertex v = new Vertex(_label);
            v.Index = Vertices.Count;
            Vertices.Add(v);
            VertexIndex.Add(_label, v);
            return v;
        }
        /// <summary>
        /// Returns the vertex at an index, null if the index is not valid
        /// </summary>
        private Vertex VertexAt(int _index)
        {
            return _index >= 0 && _index < Vertices.Count ? Vertices[_index] : null;
        }
        /// <summary>
        /// Returns a reference for existing vertex
        /// </summary>
        /// <param name="_label">Label of vertex to search for</param>
        /// <returns>Vertex reference, null if not fount or empty label</returns>
        /// <remarks>Labels are stored in upper case; other spellings are converted (case-insensitive)</remarks>
        private Vertex GetVertex(string _label)
        {
            if (string.IsNullOrEmpty(_label))
                return null;

            Vertex v;
            if (VertexIndex.TryGetValue(_label, out v))
                return v;
            string upper = _label.ToUpper();
            if (!ReferenceEquals(upper, _label) && upper != _label && VertexIndex.TryGetValue(upper, out v))
                return v;
            return null;
        }
        /// <summary>
        /// Builds the incoming and outgoing edge arrays of all vertices from RelatedEdges, if the
        /// graph changed since the last time
        /// </summary>
        /// <remarks>
        /// Done once per graph change instead of in CreateEdges, so building a graph stays cheap,
        /// while Dijkstra and the path enumeration only visit edges of the right direction
        /// </remarks>
        private void EnsureAdjacency()
        {
            if (AdjacencyValid)
                return;
            foreach (Vertex _v in Vertices)
            {
                int outCount = 0, inCount = 0;
                foreach (Edge _e in _v.RelatedEdges)
                {
                    if (_e.Tail == _v) outCount++;
                    if (_e.Head == _v) inCount++;
                }
                Edge[] outEdges = outCount == 0 ? Vertex.NoEdges : new Edge[outCount];
                Edge[] inEdges = inCount == 0 ? Vertex.NoEdges : new Edge[inCount];
                outCount = inCount = 0;
                foreach (Edge _e in _v.RelatedEdges)
                {
                    if (_e.Tail == _v) outEdges[outCount++] = _e;
                    if (_e.Head == _v) inEdges[inCount++] = _e;
                }
                _v.OutEdges = outEdges;
                _v.InEdges = inEdges;
            }
            AdjacencyValid = true;
        }
        /// <summary>
        /// Clears pointers to next edge in shortest path for all vertices
        /// Clears distances to endpoint in shortet path vor all vertices
        /// </summary>
        private void ResetGraphState()
        {
            foreach (Vertex _v in this.Vertices)
            {
                _v.EdgeToPath = null;
                _v.Distance = Vertex.Unreached;
                _v.SidetrackHeap = null;
                _v.SidetrackHeapBuilt = false;
            }
        }
        /// <summary>
        /// Builds the shortest path tree towards the endpoint (Dijkstra on reversed edges)
        /// </summary>
        /// <remarks>
        /// Negative edges are ignored. With a maximum detour, the search stops at distance
        /// (shortest path + detour): farther vertices cannot be part of any returned path.
        /// </remarks>
        private void BuildShortestPathTree()
        {
            ResetGraphState();  // Reset all distances to endpoint and previous shortest path

            Vertex v = this.EndVertex;
            v.Distance = 0;   // Set distance to 0 for endpoint vertex

            // Fringe of edges pending to be processed, by distance of their tail to t
            PriorityQueue<Edge, long> fringe = new PriorityQueue<Edge, long>();
            // Region of endpoint for arc-flag pruning (-1 = no pruning)
            int targetRegion = ArcFlagsRegionOf(this.EndVertex);
            // Search radius, reduced to (shortest path + detour) once s is settled
            long radius = this.SourceVertex == this.EndVertex ? DetourBound(0) : long.MaxValue;

            while (true)
            {
                if (v != null)
                    foreach (Edge _e in v.InEdges)
                        if (_e.Weight >= 0  // Ignore negative edges
                            && _e.Tail.Distance == Vertex.Unreached  // Tail already settled, cannot improve
                            && _e.HasArcFlag(targetRegion))  // Arc flags: skip arcs not on any shortest path into t's region
                            fringe.Enqueue(_e, _e.Weight + v.Distance);

                Edge e;
                long distance;
                if (!fringe.TryDequeue(out e, out distance) || distance > radius)
                    break;  // No pending edges, or outside of corridor

                v = e.Tail;
                if (v.Distance == Vertex.Unreached) // Vertex distance to endpoint not calculated yet
                {
                    v.Distance = distance;
                    v.EdgeToPath = e;
                    if (v == this.SourceVertex)
                        radius = SaturatingAdd(distance, DetourBound(distance));
                }
                else
                    v = null;
            }
        }
        /// <summary>
        /// Initializes the heap of tracks with the shortest path (empty sidetrack collection)
        /// </summary>
        private void BuildSidetracksHeap()
        {
            this.PathsHeap = new PriorityQueue<ST_Node>(64);
            this.PathsHeap.Enqueue(new ST_Node(new GraphPath()));
            this.CandidatesCreated = 1;
        }
        /// <summary>
        /// Enqueues the successors of an extracted track (Eppstein's path graph):
        /// the two heap children of its last sidetrack (same prefix, next best alternatives for the
        /// last sidetrack) and the best sidetrack after the last one (root of H(head))
        /// </summary>
        /// <param name="_node">Extracted track</param>
        /// <remarks>
        /// Every sidetrack collection is reached exactly once, keys in a heap never decrease from
        /// parent to child and deltas are non-negative, so tracks are extracted in order of weight.
        /// At most 3 candidates per extracted path; works with cycles.
        /// </remarks>
        private void AddSidetracks(ST_Node _node)
        {
            SidetrackHeapNode last = _node.HeapNode;
            if (last == null)  // Shortest path (no sidetracks): best first sidetrack
            {
                EnqueueCandidate(_node, GetSidetrackHeap(this.SourceVertex));
                return;
            }
            EnqueueCandidate(_node.Parent, last.Left);   // Alternatives for the last sidetrack
            EnqueueCandidate(_node.Parent, last.Right);
            EnqueueCandidate(_node, GetSidetrackHeap(last.Edge.Head));  // One more sidetrack
        }
        /// <summary>
        /// Enqueues a candidate track (parent's sidetracks plus the sidetrack of the heap node),
        /// unless it exceeds the maximum detour
        /// </summary>
        private void EnqueueCandidate(ST_Node _parent, SidetrackHeapNode _heapNode)
        {
            if (_heapNode == null || _parent.Weight + _heapNode.Key > this.PathBound)
                return;
            this.PathsHeap.Enqueue(new ST_Node(_parent, _heapNode));
            this.CandidatesCreated++;
        }
        /// <summary>
        /// Returns the heap of all sidetracks on the shortest path tree path from v to t,
        /// building it (and the heaps of the following vertices) if needed
        /// </summary>
        /// <param name="_v">Vertex</param>
        /// <returns>Heap root, null if there are no sidetracks or v cannot reach t</returns>
        private SidetrackHeapNode GetSidetrackHeap(Vertex _v)
        {
            if (_v.Distance == Vertex.Unreached)
                return null;

            // Collects vertices on the tree path whose heap is not built yet (iterative, no deep recursion)
            List<Vertex> pending = new List<Vertex>();
            for (Vertex x = _v; x != null && !x.SidetrackHeapBuilt; x = x.Next)
                pending.Add(x);

            // Builds heaps from t towards v: H(x) = H(next(x)) + sidetracks leaving x
            List<SidetrackHeapNode> own = new List<SidetrackHeapNode>();
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Vertex x = pending[i];
                own.Clear();
                foreach (Edge _e in x.OutEdges)
                    if (_e.IsSidetrackOf(x) && _e.Head.Distance != Vertex.Unreached)  // Head must reach t
                        own.Add(new SidetrackHeapNode(_e, _e.Delta));
                own.Sort((a, b) => a.Key.CompareTo(b.Key));

                SidetrackHeapNode next = x.Next == null ? null : x.Next.SidetrackHeap;
                x.SidetrackHeap = SidetrackHeapNode.Merge(next, SidetrackHeapNode.FromSorted(own));
                x.SidetrackHeapBuilt = true;
            }
            return _v.SidetrackHeap;
        }
        /// <summary>
        /// Reconstructs path from sidetracks
        /// </summary>
        /// <param name="_sidetracks">Sidetracks collections for this path, could be empty for shortest</param>
        /// <returns>Full path reconstructed from s to t, crossing sidetracks</returns>
        private GraphPath RebuildPath(GraphPath _sidetracks)
        {
            GraphPath path = new GraphPath();
            Vertex v = this.SourceVertex;
            int i = 0;

            // Start from s, following shortest path or sidetracks
            while (v != null)
            {
                // if current vertex is conected to next sidetrack, cross it
                if (i < _sidetracks.Count && _sidetracks[i].Tail == v)
                {
                    path.Add(_sidetracks[i]);
                    v = _sidetracks[i++].Head;
                }
                else // else continue walking on shortest path
                {
                    if (v.EdgeToPath == null)
                        break;
                    path.Add(v.EdgeToPath);
                    v = v.Next;
                }
            }
            return path;
        }
        #endregion
    }
}
