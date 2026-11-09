using System;
using System.Collections.Generic;

namespace Eppstein
{
    /// <summary>
    /// Arc-flags extension for the Eppstein graph (optional speed-up technique)
    /// </summary>
    /// <remarks>
    /// The vertices are partitioned into regions. During preprocessing every arc (u,v) gets one flag
    /// per region r, which is set if the arc lies on a shortest path from u to some vertex of r.
    /// The backward Dijkstra in <see cref="FindShortestPath"/> then only relaxes arcs whose flag for
    /// the region of the endpoint t is set.
    ///
    /// Every shortest path u->t only uses arcs flagged for region(t), so all distances to t
    /// (and therefore all sidetrack deltas) stay exact. The k shortest paths returned are the same
    /// as without arc flags; only the shortest path tree computation touches fewer arcs.
    ///
    /// The feature is disabled by default, the existing public interface is unchanged.
    /// Flags are recomputed lazily on the next query after the graph has been modified.
    /// </remarks>
    public partial class Graph
    {
        #region Arc-flags fields
        /// <summary>
        /// True if arc flags are used for shortest path tree computation
        /// </summary>
        private bool arcFlagsEnabled = false;
        /// <summary>
        /// True if current flags match current graph (structure, weights and partition)
        /// </summary>
        private bool arcFlagsValid = false;
        /// <summary>
        /// Number of regions in current partition
        /// </summary>
        private int regionCount = 0;
        /// <summary>
        /// Requested number of regions for automatic partition, 0 if partition is given by user
        /// </summary>
        private int autoRegionCount = 0;
        /// <summary>
        /// User-defined partition string, null for automatic partition
        /// </summary>
        private string manualRegions = null;
        #endregion

        #region Arc-flags public interface
        /// <summary>
        /// Tells if arc-flags optimization is enabled
        /// </summary>
        public bool ArcFlagsEnabled
        {
            get { return arcFlagsEnabled; }
        }
        /// <summary>
        /// Number of regions used by arc-flags optimization, 0 if disabled
        /// </summary>
        /// <remarks>Runs pending preprocessing if needed</remarks>
        public int ArcFlagsRegionCount
        {
            get
            {
                EnsureArcFlags();
                return arcFlagsEnabled ? regionCount : 0;
            }
        }
        /// <summary>
        /// Enables arc flags with an automatic partition of vertices into regions
        /// </summary>
        /// <param name="_regionCount">Desired number of regions (at least 1)</param>
        /// <returns>True if enabled, false if parameter is invalid</returns>
        /// <remarks>Partition is made of connected chunks of a breadth-first ordering of the vertices</remarks>
        public bool EnableArcFlags(int _regionCount)
        {
            if (_regionCount < 1)
                return false;

            autoRegionCount = _regionCount;
            manualRegions = null;
            arcFlagsEnabled = true;
            InvalidateArcFlags();
            return true;
        }
        /// <summary>
        /// Enables arc flags with a user-defined partition of vertices into regions
        /// </summary>
        /// <param name="_regions">Semicolon-separated regions, each one a comma-separated list of vertex labels,
        /// e.g. "S,A,B,C;D,E,F;G,H,I,J,K,T"</param>
        /// <returns>True if enabled, false if some label does not exist or is assigned twice</returns>
        /// <remarks>Vertices not listed are put together into one additional region</remarks>
        public bool EnableArcFlags(string _regions)
        {
            if (string.IsNullOrEmpty(_regions) || !ApplyManualPartition(_regions))
                return false;

            manualRegions = _regions;
            autoRegionCount = 0;
            arcFlagsEnabled = true;
            InvalidateArcFlags();
            return true;
        }
        /// <summary>
        /// Disables arc-flags optimization and releases all flags
        /// </summary>
        public void DisableArcFlags()
        {
            arcFlagsEnabled = false;
            autoRegionCount = 0;
            manualRegions = null;
            ClearArcFlagData();
        }
        /// <summary>
        /// Runs the arc-flags preprocessing immediately (otherwise done lazily by next query)
        /// </summary>
        /// <returns>True if flags are available, false if arc flags are disabled</returns>
        public bool PrecomputeArcFlags()
        {
            EnsureArcFlags();
            return arcFlagsEnabled && arcFlagsValid;
        }
        #endregion

        #region Arc-flags private methods
        /// <summary>
        /// Disables arc flags completely, used when all vertices are recreated
        /// </summary>
        private void ResetArcFlags()
        {
            DisableArcFlags();
        }
        /// <summary>
        /// Marks flags as outdated, they are recomputed on next query
        /// </summary>
        private void InvalidateArcFlags()
        {
            arcFlagsValid = false;
        }
        /// <summary>
        /// Removes regions and flags from all vertices and edges
        /// </summary>
        private void ClearArcFlagData()
        {
            arcFlagsValid = false;
            regionCount = 0;
            foreach (Vertex _v in Vertices)
            {
                _v.Region = -1;
                foreach (Edge _e in _v.RelatedEdges)
                    _e.ArcFlags = null;
            }
        }
        /// <summary>
        /// Region used for pruning arcs during shortest path tree computation
        /// </summary>
        /// <param name="_v">Endpoint vertex</param>
        /// <returns>Region index, -1 if arc flags shall not be used</returns>
        private int ArcFlagsRegionOf(Vertex _v)
        {
            if (!arcFlagsEnabled || !arcFlagsValid || _v == null)
                return -1;
            return _v.Region;
        }
        /// <summary>
        /// Recomputes partition and flags if enabled and outdated
        /// </summary>
        private void EnsureArcFlags()
        {
            if (!arcFlagsEnabled || arcFlagsValid)
                return;

            ClearArcFlagData();
            if (manualRegions != null)
            {
                if (!ApplyManualPartition(manualRegions))
                {
                    DisableArcFlags();  // Partition no longer valid, fall back to plain Dijkstra
                    return;
                }
            }
            else
                ApplyAutomaticPartition(autoRegionCount);

            ComputeArcFlags();
            arcFlagsValid = true;
        }
        /// <summary>
        /// Assigns regions from a user-defined partition string
        /// </summary>
        /// <param name="_regions">Semicolon-separated groups of comma-separated labels</param>
        /// <returns>True if all labels exist and none is repeated</returns>
        private bool ApplyManualPartition(string _regions)
        {
            foreach (Vertex _v in Vertices)
                _v.Region = -1;

            int region = 0;
            foreach (string _group in _regions.ToUpper().Split(new char[] { ';' }))
            {
                bool any = false;
                foreach (string _label in _group.Split(new char[] { ',' }))
                {
                    string label = _label.Trim();
                    if (label.Length == 0)
                        continue;
                    Vertex v = GetVertex(label);
                    if (v == null || v.Region >= 0)  // Unknown or already assigned
                    {
                        foreach (Vertex _w in Vertices)
                            _w.Region = -1;
                        return false;
                    }
                    v.Region = region;
                    any = true;
                }
                if (any)
                    region++;
            }

            // Collects vertices not listed into one additional region
            bool unassigned = false;
            foreach (Vertex _v in Vertices)
            {
                if (_v.Region < 0)
                {
                    _v.Region = region;
                    unassigned = true;
                }
            }
            regionCount = unassigned ? region + 1 : region;
            return true;
        }
        /// <summary>
        /// Splits the vertices into connected chunks of similar size, using a breadth-first
        /// ordering over the underlying undirected graph
        /// </summary>
        /// <param name="_count">Desired number of regions</param>
        private void ApplyAutomaticPartition(int _count)
        {
            int n = Vertices.Count;
            if (n == 0)
            {
                regionCount = 0;
                return;
            }
            int count = Math.Min(Math.Max(_count, 1), n);
            int chunk = (n + count - 1) / count;

            HashSet<Vertex> visited = new HashSet<Vertex>();
            Queue<Vertex> queue = new Queue<Vertex>();
            int index = 0;
            foreach (Vertex _start in Vertices)
            {
                if (!visited.Add(_start))
                    continue;
                queue.Enqueue(_start);
                while (queue.Count > 0)
                {
                    Vertex v = queue.Dequeue();
                    v.Region = index++ / chunk;
                    foreach (Edge _e in v.RelatedEdges)  // Contains incoming and outgoing edges
                    {
                        Vertex w = _e.Tail == v ? _e.Head : _e.Tail;
                        if (visited.Add(w))
                            queue.Enqueue(w);
                    }
                }
            }
            regionCount = (n + chunk - 1) / chunk;
        }
        /// <summary>
        /// Computes arc flags for current partition
        /// </summary>
        /// <remarks>
        /// Arcs inside a region get that region's flag. For every boundary vertex b of region r
        /// (a vertex of r with an incoming arc from another region) a backward Dijkstra to b is run,
        /// and every arc that is tight for it (d(u,b) = w(u,v) + d(v,b)) gets flag r.
        /// Negative arcs are ignored, as in the main shortest path computation.
        /// </remarks>
        private void ComputeArcFlags()
        {
            EnsureAdjacency();  // Backward Dijkstra uses incoming edge arrays
            List<Edge> arcs = new List<Edge>();
            HashSet<Vertex> boundary = new HashSet<Vertex>();
            foreach (Vertex _v in Vertices)
            {
                foreach (Edge _e in _v.RelatedEdges)
                {
                    if (_e.Tail != _v)  // Each edge is visited once, from its tail
                        continue;
                    arcs.Add(_e);
                    _e.ArcFlags = new bool[regionCount];
                    if (_e.Weight < 0)
                        continue;
                    if (_e.Tail.Region == _e.Head.Region)
                        _e.ArcFlags[_e.Head.Region] = true;
                    else
                        boundary.Add(_e.Head);
                }
            }

            foreach (Vertex _b in boundary)
            {
                Dictionary<Vertex, long> dist = BackwardDistances(_b);
                int region = _b.Region;
                foreach (Edge _e in arcs)
                {
                    long dTail, dHead;
                    if (_e.Weight >= 0 &&
                        dist.TryGetValue(_e.Tail, out dTail) &&
                        dist.TryGetValue(_e.Head, out dHead) &&
                        dTail == _e.Weight + dHead)
                        _e.ArcFlags[region] = true;
                }
            }
        }
        /// <summary>
        /// Computes distances from all vertices to given target (Dijkstra on reversed arcs)
        /// </summary>
        /// <param name="_target">Target vertex</param>
        /// <returns>Distance to target for every vertex that can reach it</returns>
        private Dictionary<Vertex, long> BackwardDistances(Vertex _target)
        {
            Dictionary<Vertex, long> dist = new Dictionary<Vertex, long>();
            PriorityQueue<Edge, long> fringe = new PriorityQueue<Edge, long>();
            dist[_target] = 0;
            Vertex v = _target;

            while (true)
            {
                if (v != null)
                {
                    long dv = dist[v];
                    foreach (Edge _e in v.InEdges)
                        if (_e.Weight >= 0 && !dist.ContainsKey(_e.Tail))
                            fringe.Enqueue(_e, _e.Weight + dv);
                }

                Edge e;
                long distance;
                if (!fringe.TryDequeue(out e, out distance))
                    break;

                v = e.Tail;
                if (!dist.ContainsKey(v))
                    dist[v] = distance;
                else
                    v = null;
            }

            return dist;
        }
        #endregion
    }
}
