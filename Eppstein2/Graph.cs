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
        private readonly Path explicitSidetracks;
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
        /// Public constructor
        /// </summary>
        /// <param name="_sidetracks">Sidetrack collection</param>
        /// <remarks>Weight is calculated using path's weight</remarks>
        public ST_Node(Path _sidetracks)
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
        /// Collection of edges (sidetracks), not exactly a full path
        /// </summary>
        /// <remarks>Built on demand from the chain of parent nodes</remarks>
        public Path Sidetracks
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
                Path result = new Path();
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
        /// Vertices by label (case-insensitive), for constant time lookup
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
        #endregion

        #region Public methods
        /// <summary>
        /// Default constructor
        /// </summary>
        public Graph()
        {
            Vertices = new List<Vertex>();
            VertexIndex = new Dictionary<string, Vertex>(StringComparer.InvariantCultureIgnoreCase);
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
            ResetArcFlags();   // Partition refers to removed vertices

            string[] vertexList = _vertices.ToUpper().Split(new char[] { ',' });
            foreach (string _label in vertexList)
            {
                if (_label.Length == 0)  // ignore empty labels
                    continue;
                if (!char.IsLetter(_label, 0))  // label must start with a letter
                    return false;

                foreach (char c in _label)
                {
                    if (!char.IsLetterOrDigit(c))  // label just can contain letters and digits
                        return false;
                }
                if (GetVertex(_label)==null)
                {
                    Vertex v = new Vertex(_label);
                    Vertices.Add(v);
                    VertexIndex.Add(_label, v);
                }
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
        public Path FindShortestPath(string _s, string _t)
        {
            Ready = false;

            // Parse start and end vertex strings
            this.SourceVertex = this.GetVertex(_s.ToUpper());
            this.EndVertex = this.GetVertex(_t.ToUpper());
            if (SourceVertex == null || EndVertex == null)
                return new Path(); // Invalid path

            // Recomputes arc flags lazily if enabled and graph changed since last preprocessing
            EnsureArcFlags();

            // Builds shortest path tree for all vertices to t, according to Dijkstra,
            // storing distance to endpoint information on vertices, as described in Eppstein
            BuildShortestPathTree();
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
        public Path FindNextShortestPath()
        {
            if (!Ready)
                return new Path();  // Invalid path

            // Pick next track from heap, it is ordered from shortest path to longest
            ST_Node node = this.PathsHeap.Dequeue();
            if (node == null)
                return new Path(); // Invalid path

            // Lazy expansion: enqueues all tracks having this one as prefix plus one sidetrack
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
        /// Returns a reference for existing vertex
        /// </summary>
        /// <param name="_label">Label of vertex to search for</param>
        /// <returns>Vertex reference, null if not fount or empty label</returns>
        private Vertex GetVertex(string _label)
        {
            if (string.IsNullOrEmpty(_label))
                return null;

            Vertex v;
            return VertexIndex.TryGetValue(_label, out v) ? v : null;
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
            }
        }
        /// <summary>
        /// Builds the shortest path tree using a priority queue for given vertex
        /// </summary>
        /// <remarks>Negative edges are ignored</remarks>
        private void BuildShortestPathTree()
        {
            ResetGraphState();  // Reset all distances to endpoint and previous shortest path

            Vertex v = this.EndVertex;
            v.Distance = 0;   // Set distance to 0 for endpoint vertex

            // Creates a fringe (queue) for storing edge pending to be processed
            PriorityQueue<SP_Node> fringe = new PriorityQueue<SP_Node>(this.Vertices.Count);
            // Region of endpoint for arc-flag pruning (-1 = no pruning)
            int targetRegion = ArcFlagsRegionOf(this.EndVertex);

            // Main loop
            do
            {
                if (v!=null)
                    foreach (Edge _e in v.RelatedEdges) // Evaluate all incoming edges
                        if (_e.Head==v && _e.Weight>=0  // Ignore negative edges
                            && _e.Tail.Distance == Vertex.Unreached  // Tail already settled, cannot improve
                            && _e.HasArcFlag(targetRegion))  // Arc flags: skip arcs not on any shortest path into t's region
                            fringe.Enqueue(new SP_Node(_e, _e.Weight + _e.Head.Distance));

                SP_Node node = fringe.Dequeue();  // Extracts next element in queue
                if (node == null)  // No pending edges to evaluate, finished
                    break;

                Edge e = node.Edge;
                v = e.Tail;
                if (v.Distance == Vertex.Unreached) // Vertex distance to endpoint not calculated yet
                {
                    v.Distance = e.Weight + e.Head.Distance;
                    v.EdgeToPath = e;
                }
                else
                    v = null;
            } while (true);
        }
        /// <summary>
        /// Initializes the heap of tracks with the shortest path (empty sidetrack collection)
        /// </summary>
        private void BuildSidetracksHeap()
        {
            this.PathsHeap = new PriorityQueue<ST_Node>(Vertices.Count);
            this.PathsHeap.Enqueue(new ST_Node(new Path()));
        }
        /// <summary>
        /// Enqueues all children of a track: the same sidetracks plus one more sidetrack, taken from
        /// a vertex of the shortest path tree between the head of the last sidetrack (or s) and t
        /// </summary>
        /// <param name="_node">Extracted track</param>
        /// <remarks>
        /// Every sidetrack collection has a unique parent (itself without its last sidetrack), so each
        /// path is generated exactly once. Deltas are non-negative, so children never are shorter than
        /// their parent and extraction order stays correct. Iterative and lazy: works with cycles.
        /// </remarks>
        private void AddSidetracks(ST_Node _node)
        {
            Vertex v = _node.LastHead ?? this.SourceVertex;

            // Walks shortest path tree from v to t (tree paths are simple, so this terminates)
            while (v != null)
            {
                foreach (Edge _e in v.RelatedEdges)
                {
                    if (_e.IsSidetrackOf(v) && (_e.Head.EdgeToPath != null || _e.Head == this.EndVertex))
                        this.PathsHeap.Enqueue(new ST_Node(_node, _e));  // O(1): no copy of sidetracks
                }
                v = v.Next;
            }
        }
        /// <summary>
        /// Reconstructs path from sidetracks
        /// </summary>
        /// <param name="_sidetracks">Sidetracks collections for this path, could be empty for shortest</param>
        /// <returns>Full path reconstructed from s to t, crossing sidetracks</returns>
        private Path RebuildPath(Path _sidetracks)
        {
            Path path = new Path();
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
