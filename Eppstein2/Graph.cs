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
        public int Weight;

        /// <summary>
        /// Public constructor
        /// </summary>
        /// <param name="_edge">Edge object</param>
        /// <param name="_weight">Weight of node</param>
        public SP_Node(Edge _edge, int _weight)
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
            return Math.Sign(this.Weight - ((SP_Node)_obj).Weight);
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
    public class ST_Node : IComparable
    {
        /// <summary>
        /// Collection of edges (sidetracks), not exactly a full path
        /// </summary>
        public Path Sidetracks;
        /// <summary>
        /// Weight of node (sum of sidetracks)
        /// </summary>
        public int Weight;

        /// <summary>
        /// Public constructor
        /// </summary>
        /// <param name="_sidetracks">Sidetrack collection</param>
        /// <remarks>Weight is calculated using path's weight</remarks>
        public ST_Node(Path _sidetracks)
        {
            this.Sidetracks = _sidetracks;
            this.Weight = _sidetracks.DeltaWeight;
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
            return Math.Sign(this.Weight - ((ST_Node)_obj).Weight);
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
    public class Graph
    {
        #region Private fields
        /// <summary>
        /// Linear array of vertices
        /// </summary>
        private List<Vertex> Vertices;
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
                    Vertices.Add(new Vertex(_label));
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
        public bool CreateEdges(string _tails, string _heads, int _weight, string _group)
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

            return true;
        }
        /// <summary>
        /// Change group weights
        /// </summary>
        /// <param name="_group">Group name of edges to be affected</param>
        /// <param name="_weight">New weights</param>
        public void EdgeGroupWeights(string _group, int _weight)
        {
            string group = _group.ToUpper();

            foreach (Vertex _v in Vertices)
                foreach (Edge _e in _v.RelatedEdges)
                    if (_e.Group == group)
                        _e.Weight = _weight;

            this.Ready = false;
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

            // Builds shortest path tree for all vertices to t, according to Dijkstra,
            // storing distance to endpoint information on vertices, as described in Eppstein
            BuildShortestPathTree();
            // Fills a heap with all possible tracks from s to t, as described in Eppstein
            // Paths are defined uniquely by sidetrack collections (edges not in shortest paths) 
            BuildSidetracksHeap();
            // Flag to indicate that shortest paths have been calculated
            Ready = true;

            return FindNextShortestPath();
        }
        /// <summary>
        /// Recover next pre-calculated shortest path
        /// </summary>
        /// <returns>Directional path if available, empty path if not remaining paths</returns>
        public Path FindNextShortestPath()
        {
            if (!Ready)
                return new Path();  // Invalid path

            // Pick next track from heap, it is ordered from shortest path to longest
            ST_Node node = this.PathsHeap.Dequeue();
            if (node == null)
                return new Path(); // Invalid path

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

            foreach (Vertex _v in Vertices)
            {
                if (_v.Equals(_label))
                    return _v;
            }
            return null;
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
                _v.Distance = int.MinValue;
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

            // Main loop
            do
            {
                if (v!=null)
                    foreach (Edge _e in v.RelatedEdges) // Evaluate all incoming edges
                        if (_e.Head==v && _e.Weight>=0)  // Ignore negative edges
                            fringe.Enqueue(new SP_Node(_e, _e.Weight + _e.Head.Distance));

                SP_Node node = fringe.Dequeue();  // Extracts next element in queue
                if (node == null)  // No pending edges to evaluate, finished
                    break;

                Edge e = node.Edge;
                v = e.Tail;
                if (v.Distance == int.MinValue) // Vertex distance to endpoint not calculated yet
                {
                    v.Distance = e.Weight + e.Head.Distance;
                    v.EdgeToPath = e;
                }
                else
                    v = null;
            } while (true);
        }
        /// <summary>
        /// Creates all posible paths by describing only the sidetracks for each path
        /// </summary>
        /// <returns></returns>
        private void BuildSidetracksHeap()
        {
            this.PathsHeap = new PriorityQueue<ST_Node>(Vertices.Count);
            Path empty = new Path();
            this.PathsHeap.Enqueue(new ST_Node(empty));
            AddSidetracks(empty, this.SourceVertex);
        }
        /// <summary>
        /// Adds sidetracks recursively for specified vertex and new vertices in shortest path
        /// </summary>
        /// <param name="_p">Previous sidetrack collection</param>
        /// <param name="_v">Vertex to evalueate</param>
        private void AddSidetracks(Path _p, Vertex _v)
        {
            foreach (Edge _e in _v.RelatedEdges)
            {
                if (_e.IsSidetrackOf(_v) && (_e.Head.EdgeToPath != null || _e.Head == this.EndVertex))
                {
                    Path p = new Path();
                    p.AddRange(_p);
                    p.Add(_e);
                    this.PathsHeap.Enqueue(new ST_Node(p));

                    if (_e.Head != _v)  // This avoids infinite cycling
                        AddSidetracks(p, _e.Head);
                }
            }
            if (_v.Next != null)
                AddSidetracks(_p, _v.Next);
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
