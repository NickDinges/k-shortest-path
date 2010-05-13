using System;
using System.Text;
using System.Collections.Generic;

namespace Eppstein
{
    /// <summary>
    /// Contains a simple vertex with label
    /// </summary>
    public class Vertex
    {
        /// <summary>
        /// Vertex label
        /// </summary>
        private string Label;
        /// <summary>
        /// Reference for a user object, for future use
        /// </summary>
        public object Object = null;

        #region Calculated fields
        /// <summary>
        /// Points to edge in shortest path tree
        /// </summary>
        public Edge EdgeToPath = null;
        /// <summary>
        /// Distance to endpoint in shortest path calculation
        /// </summary>
        public int Distance = int.MinValue;
        /// <summary>
        /// Collection of out edges not part of shortest path
        /// </summary>
        public List<Edge> RelatedEdges = new List<Edge>();
        #endregion

        #region Properties
        /// <summary>
        /// Next vertex in shortest path
        /// </summary>
        public Vertex Next
        {
            get { return EdgeToPath == null ? null : EdgeToPath.Head; }
        }
        #endregion

        /// <summary>
        /// Public constructor
        /// </summary>
        /// <param name="_label">Label of new vertex</param>
        public Vertex(string _label)
        {
            Label = _label;
        }

        #region Overrided functions of Object class
        /// <summary>
        /// Converts this object into a string where required
        /// </summary>
        /// <returns>Vertex label</returns>
        public override string ToString()
        {
            return Label;
        }
        /// <summary>
        /// Allows to compare a vertex with another one by label, or with a string containin label
        /// </summary>
        /// <param name="_obj">Second object to compare with</param>
        /// <returns>True if considered to be equal, false if not</returns>
        public override bool Equals(object _obj)
        {
            if (_obj == null)
                return false;
            if (_obj.GetType() == typeof(Vertex))
                return (string.Compare(this.Label, ((Vertex)_obj).Label, StringComparison.InvariantCultureIgnoreCase) == 0);
            if (_obj.GetType() == typeof(string))
                return (string.Compare(this.Label, (string)_obj, StringComparison.InvariantCultureIgnoreCase) == 0);
            return false;
        }
        /// <summary>
        /// Returns a hash for this object, it uses label's hash
        /// </summary>
        /// <returns>Hash calculated for this object</returns>
        public override int GetHashCode()
        {
            return Label.GetHashCode();
        }
        #endregion
    }

    /// <summary>
    /// Contains a directional edge
    /// </summary>
    public class Edge
    {
        /// <summary>
        /// Index of tail vertex
        /// </summary>
        public readonly Vertex Tail;
        /// <summary>
        /// Index of head vertex
        /// </summary>
        public readonly Vertex Head;
        /// <summary>
        /// Weight of edge
        /// </summary>
        public int Weight;
        /// <summary>
        /// Group label of edge
        /// </summary>
        public readonly string Group;

        #region Properties
        /// <summary>
        /// Returns the sidetrack delta (deviation from shortest path)
        /// Only is valid when shortest path tree has been calculated
        /// </summary>
        public int Delta
        {
            get { return this.Weight + this.Head.Distance - this.Tail.Distance; }
        }
        #endregion

        /// <summary>
        /// Constructor, generates a valid Edge
        /// </summary>
        /// <param name="_tail">Vertex object of tail endpoint</param>
        /// <param name="_head">Vertex object of head endpoint</param>
        /// <param name="_weight">Weight of edge</param>
        /// <param name="_group">Label of group where edge belongs to</param>
        /// <remarks>Edge is directional, goes from tail to head</remarks>
        public Edge(Vertex _tail, Vertex _head, int _weight, string _group)
        {
            Tail = _tail;
            Head = _head;
            Weight = _weight;
            Group = _group;
        }
        /// <summary>
        /// Tells if the edge is a possible sidetrack of specified vertex
        /// </summary>
        /// <param name="_v">Vertex to evaluate</param>
        /// <returns>True if edge is sidetrack of vertex, false if not</returns>
        public bool IsSidetrackOf(Vertex _v)
        {
            return (this.Tail == _v && this != _v.EdgeToPath && this.Weight >= 0);
        }
        /// <summary>
        /// Converts this object into a string where required
        /// </summary>
        /// <returns>Tail and head labels, and weight inside parenthesis</returns>
        public override string ToString()
        {
            return string.Concat(this.Tail, "--" + this.Weight + "-->", this.Head);
        }
    }

    /// <summary>
    /// Contains a path composed by a edges collection
    /// </summary>
    public class Path : List<Edge>
    {
        #region Properties
        /// <summary>
        /// Returns false if path is empty, true if not
        /// </summary>
        public bool IsValid
        {
            get { return (this.Count > 0); }
        }
        /// <summary>
        /// Returns string with comma-separated list containing all vertices in a path
        /// </summary>
        public string VertexNames
        {
            get
            {
                if (!IsValid)
                    return "(empty)";

                StringBuilder builder = new StringBuilder(this[0].Tail.ToString());
                foreach (Edge e in this)
                {
                    builder.Append("," + e.Head);
                }
                return builder.ToString();
            }
        }
        /// <summary>
        /// Returns sum of all weights of edges in the path
        /// </summary>
        public int Weight
        {
            get
            {
                int total = 0;
                foreach (Edge e in this)
                    total += e.Weight;
                return total;
            }
        }
        /// <summary>
        /// Returns sum of all deltas of edges in the path
        /// </summary>
        public int DeltaWeight
        {
            get
            {
                int total = 0;
                foreach (Edge e in this)
                    total += e.Delta;
                return total;
            }
        }
        #endregion

        /// <summary>
        /// Returns path's decriptive string
        /// </summary>
        /// <returns>List of delta for path</returns>
        public override string ToString()
        {
            if (!IsValid)
                return "(empty)";

            StringBuilder builder = new StringBuilder();
            foreach (Edge _e in this)
            {
                builder.Append(_e.Delta);
                builder.Append(',');
            }
            if (builder.Length > 0)
                builder.Remove(builder.Length - 1, 1);

            return builder.ToString();
         }
    }
}
