using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple fixed-depth 2D (XZ) quadtree. Items can be inserted either at a point
/// or by bounds, in which case they are added to every leaf they overlap.
/// </summary>
public class Quadtree<T>
{
    public class PartitioningInfo<V>
    {
        /// <summary>
        /// The FOUR contained partitions, or null if this is a leaf.
        /// </summary>
        public PartitioningInfo<V>[] SubPartitions = null;
        public Bounds PartitionBounds;
        public List<V> ContainedItems = new List<V>();

        public bool IsLeaf => SubPartitions == null;

        public void CreateSubPartitions(bool splitSubPartitions)
        {
            var size = PartitionBounds.size / 2f;
            size.y = 999f;

            float x25 = PartitionBounds.min.x + ((PartitionBounds.max.x - PartitionBounds.min.x) * 0.25f);
            float x75 = PartitionBounds.min.x + ((PartitionBounds.max.x - PartitionBounds.min.x) * 0.75f);
            float z25 = PartitionBounds.min.z + ((PartitionBounds.max.z - PartitionBounds.min.z) * 0.25f);
            float z75 = PartitionBounds.min.z + ((PartitionBounds.max.z - PartitionBounds.min.z) * 0.75f);

            SubPartitions = new PartitioningInfo<V>[4]
            {
                new PartitioningInfo<V>() { PartitionBounds = new Bounds(new Vector3(x25, 0f, z25), size) },
                new PartitioningInfo<V>() { PartitionBounds = new Bounds(new Vector3(x75, 0f, z25), size) },
                new PartitioningInfo<V>() { PartitionBounds = new Bounds(new Vector3(x25, 0f, z75), size) },
                new PartitioningInfo<V>() { PartitionBounds = new Bounds(new Vector3(x75, 0f, z75), size) },
            };

            if (splitSubPartitions)
            {
                foreach (var subPartition in SubPartitions)
                    subPartition.CreateSubPartitions(false);
            }
        }

        public bool Contains2D(Vector2 location)
        {
            var center = PartitionBounds.center;
            var extents = PartitionBounds.extents;
            return Mathf.Abs(location.x - center.x) <= extents.x
                && Mathf.Abs(location.y - center.z) <= extents.z;
        }

        public bool Intersects2D(Bounds other)
        {
            return PartitionBounds.min.x <= other.max.x && PartitionBounds.max.x >= other.min.x
                && PartitionBounds.min.z <= other.max.z && PartitionBounds.max.z >= other.min.z;
        }

        /// <summary>
        /// Adds the item to every leaf that overlaps the given bounds (XZ only).
        /// </summary>
        public void Insert(Bounds itemBounds, V item)
        {
            if (!Intersects2D(itemBounds))
                return;

            if (IsLeaf)
            {
                ContainedItems.Add(item);
                return;
            }

            foreach (var subPartition in SubPartitions)
                subPartition.Insert(itemBounds, item);
        }

        /// <summary>
        /// Collapses subtrees that hold no items at all.
        /// Returns true if this subtree ended up empty.
        /// </summary>
        public bool Prune()
        {
            if (IsLeaf)
                return ContainedItems.Count == 0;

            bool allEmpty = true;
            foreach (var subPartition in SubPartitions)
            {
                if (!subPartition.Prune())
                    allEmpty = false;
            }

            if (allEmpty && ContainedItems.Count == 0)
            {
                SubPartitions = null;
                return true;
            }

            return false;
        }

        public void CollectLeaves(List<PartitioningInfo<V>> outLeaves)
        {
            if (IsLeaf)
            {
                outLeaves.Add(this);
                return;
            }

            foreach (var subPartition in SubPartitions)
                subPartition.CollectLeaves(outLeaves);
        }

        public void DebugDraw(Color color, float duration = 1000f)
        {
            var min = PartitionBounds.min;
            var max = PartitionBounds.max;

            Debug.DrawLine(new Vector3(min.x, 0f, min.z), new Vector3(max.x, 0f, min.z), color, duration);
            Debug.DrawLine(new Vector3(min.x, 0f, max.z), new Vector3(max.x, 0f, max.z), color, duration);
            Debug.DrawLine(new Vector3(min.x, 0f, min.z), new Vector3(min.x, 0f, max.z), color, duration);
            Debug.DrawLine(new Vector3(max.x, 0f, min.z), new Vector3(max.x, 0f, max.z), color, duration);

            if (IsLeaf)
                return;

            foreach (var subPartition in SubPartitions)
                subPartition.DebugDraw(color, duration);
        }
    }

    public int TotalPartitionCount { get; private set; }
    public Bounds RootBounds => mainQuad.PartitionBounds;
    public IReadOnlyList<PartitioningInfo<T>> LeafPartitions => leafPartitions;

    private readonly List<PartitioningInfo<T>> leafPartitions = new List<PartitioningInfo<T>>();
    private readonly PartitioningInfo<T> mainQuad;

    /// <param name="depth">Number of subdivision levels. 5 gives 4^5 = 1024 leaves.</param>
    public Quadtree(Vector2 boundsMin, Vector2 boundsMax, int depth)
    {
        var size = new Vector3(boundsMax.x - boundsMin.x, 999f, boundsMax.y - boundsMin.y);
        var center = new Vector3((boundsMin.x + boundsMax.x) / 2f, 0f, (boundsMin.y + boundsMax.y) / 2f);

        mainQuad = new PartitioningInfo<T>() { PartitionBounds = new Bounds(center, size) };
        TotalPartitionCount = 1;
        Subdivide(mainQuad, depth);
    }

    public Quadtree(Bounds bounds, int depth)
        : this(new Vector2(bounds.min.x, bounds.min.z), new Vector2(bounds.max.x, bounds.max.z), depth)
    {
    }

    private void Subdivide(PartitioningInfo<T> partition, int remainingDepth)
    {
        if (remainingDepth <= 0)
        {
            leafPartitions.Add(partition);
            return;
        }

        partition.CreateSubPartitions(false);
        TotalPartitionCount += 4;

        foreach (var subPartition in partition.SubPartitions)
            Subdivide(subPartition, remainingDepth - 1);
    }

    public PartitioningInfo<T> FindPartitioningInfo(Vector2 location)
    {
        var partition = mainQuad;
        if (!partition.Contains2D(location))
            return null;

        while (!partition.IsLeaf)
        {
            PartitioningInfo<T> next = null;
            foreach (var subPartition in partition.SubPartitions)
            {
                if (subPartition.Contains2D(location))
                {
                    next = subPartition;
                    break;
                }
            }

            //shouldn't happen, but bail rather than loop forever
            if (next == null)
                return null;

            partition = next;
        }

        return partition;
    }

    /// <summary>
    /// Adds an item to every leaf overlapping <paramref name="itemBounds"/>.
    /// </summary>
    public void AddItem(Bounds itemBounds, T item)
    {
        mainQuad.Insert(itemBounds, item);
    }

    public void AddItem(Vector2 location, T item)
    {
        var info = FindPartitioningInfo(location);
        if (info == null)
        {
            Debug.LogError($"AddItem({location}) failed.");
            return;
        }
        info.ContainedItems.Add(item);
    }

    public void AddItem(Transform transform, T item)
    {
        AddItem(transform.position.ToVec2XZ(), item);
    }

    public void AddItems(Vector2 location, IEnumerable<T> items)
    {
        var info = FindPartitioningInfo(location);
        if (info == null)
        {
            Debug.LogError($"AddItems({location}) failed.");
            return;
        }
        info.ContainedItems.AddRange(items);
    }

    public void AddItems(Transform transform, IEnumerable<T> items)
    {
        AddItems(transform.position.ToVec2XZ(), items);
    }

    /// <summary>
    /// Drops empty subtrees. Call once after all items are inserted.
    /// </summary>
    public void Prune()
    {
        mainQuad.Prune();

        leafPartitions.Clear();
        mainQuad.CollectLeaves(leafPartitions);

        TotalPartitionCount = 0;
        CountPartitions(mainQuad);
    }

    private void CountPartitions(PartitioningInfo<T> partition)
    {
        TotalPartitionCount++;
        if (partition.IsLeaf)
            return;

        foreach (var subPartition in partition.SubPartitions)
            CountPartitions(subPartition);
    }

    public void DebugDraw(Color color, float duration = 1000f)
    {
        mainQuad.DebugDraw(color, duration);
    }
}