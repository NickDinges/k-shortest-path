using System.Collections.Generic;

namespace Eppstein;

/// <summary>
/// Node of a persistent leftist heap of sidetracks, ordered by delta (Eppstein's H_T).
/// </summary>
/// <remarks>
/// For every vertex v, the heap H(v) contains all sidetracks whose tail lies on the shortest path
/// tree path from v to the goal: H(v) is H(next(v)) plus the sidetracks leaving v. Merging is
/// persistent and copies only the O(log n) nodes on the merge path, so H(v) shares almost all nodes
/// with H(next(v)).
/// </remarks>
internal sealed class SidetrackHeapNode
{
    /// <summary>Sidetrack edge number.</summary>
    public readonly int Edge;

    /// <summary>Delta of the sidetrack (extra weight compared to the shortest path), heap key.</summary>
    public readonly long Key;

    /// <summary>Length of the right spine (leftist property: rank(left) &gt;= rank(right)).</summary>
    public int Rank;

    /// <summary>Children; their keys are greater or equal than this node's key.</summary>
    public SidetrackHeapNode? Left, Right;

    public SidetrackHeapNode(int edge, long key)
    {
        Edge = edge;
        Key = key;
        Rank = 1;
    }

    private SidetrackHeapNode(SidetrackHeapNode other)
    {
        Edge = other.Edge;
        Key = other.Key;
        Rank = other.Rank;
        Left = other.Left;
        Right = other.Right;
    }

    private static int RankOf(SidetrackHeapNode? h) => h == null ? 0 : h.Rank;

    /// <summary>
    /// Merges two heaps without modifying them (persistent). Recursion depth is at most
    /// rank(a) + rank(b), i.e. O(log n).
    /// </summary>
    public static SidetrackHeapNode? Merge(SidetrackHeapNode? a, SidetrackHeapNode? b)
    {
        if (a == null)
            return b;
        if (b == null)
            return a;
        if (b.Key < a.Key)
            (a, b) = (b, a);

        SidetrackHeapNode result = new(a);
        result.Right = Merge(a.Right, b);
        if (RankOf(result.Left) < RankOf(result.Right))
            (result.Left, result.Right) = (result.Right, result.Left);
        result.Rank = RankOf(result.Right) + 1;
        return result;
    }

    /// <summary>
    /// Builds a heap from new nodes sorted by ascending key, as a chain of left children (a valid
    /// leftist heap of rank 1, built in O(1) per node).
    /// </summary>
    public static SidetrackHeapNode? FromSorted(List<SidetrackHeapNode> sorted)
    {
        SidetrackHeapNode? root = null;
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            sorted[i].Left = root;
            root = sorted[i];
        }
        return root;
    }
}

/// <summary>
/// A candidate path, described by its sidetracks: the sidetracks of its parent plus the sidetrack of
/// one heap node. Creating one costs O(1) time and memory.
/// </summary>
internal sealed class Candidate
{
    /// <summary>The shortest path (no sidetracks).</summary>
    public static readonly Candidate Root = new();

    /// <summary>Same sidetracks without the last one; null for the root.</summary>
    public readonly Candidate? Parent;

    /// <summary>Heap node of the last sidetrack; null for the root.</summary>
    public readonly SidetrackHeapNode? Heap;

    /// <summary>Sum of the sidetrack deltas (extra weight compared to the shortest path).</summary>
    public readonly long Weight;

    private Candidate()
    {
    }

    public Candidate(Candidate parent, SidetrackHeapNode heap)
    {
        Parent = parent;
        Heap = heap;
        Weight = parent.Weight + heap.Key;
    }
}
