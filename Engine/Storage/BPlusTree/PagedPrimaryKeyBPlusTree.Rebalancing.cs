namespace Engine;

public sealed partial class PagedPrimaryKeyBPlusTree
{
    private bool RequiresSplit(LeafNode leaf) => leaf.Entries.Count > _maxKeys || leaf.Entries.Sum(GetLeafEntrySize) > _bufferPool.PageSize - NodeHeaderSize;
    private static int GetLeafEntrySize(LeafEntry entry) => entry.OverflowHeadPageId > 0 ? LeafOverflowEntrySize : LeafEntryHeaderSize + entry.Payload.Length;

    private void SplitLeaf(int leafPageId, LeafNode leaf, List<PathEntry> path)
    {
        int middle = FindLeafSplitIndex(leaf);
        var right = new LeafNode { NextPageId = leaf.NextPageId };
        right.Entries.AddRange(leaf.Entries.Skip(middle));
        leaf.Entries.RemoveRange(middle, leaf.Entries.Count - middle);
        int rightPageId = AllocateTreePage();
        leaf.NextPageId = rightPageId;
        WriteLeaf(leafPageId, leaf);
        WriteLeaf(rightPageId, right);
        InsertIntoParent(leafPageId, right.Entries[0].Key, rightPageId, path);
    }

    private int FindLeafSplitIndex(LeafNode leaf)
    {
        int capacity = _bufferPool.PageSize - NodeHeaderSize, best = -1, difference = int.MaxValue, left = 0, total = leaf.Entries.Sum(GetLeafEntrySize);
        for (int i = 1; i < leaf.Entries.Count; i++)
        {
            left += GetLeafEntrySize(leaf.Entries[i - 1]);
            int right = total - left;
            if (i < MinimumLeafKeys || leaf.Entries.Count - i < MinimumLeafKeys || left > capacity || right > capacity) continue;
            int d = Math.Abs(left - right);
            if (d < difference) { best = i; difference = d; }
        }
        if (best < 0) throw new InvalidOperationException(Constants.PageDataTooLargeError);
        return best;
    }

    private void InsertIntoParent(int leftPageId, long separator, int rightPageId, List<PathEntry> path)
    {
        while (path.Count > 0)
        {
            PathEntry parentPath = path[^1];
            path.RemoveAt(path.Count - 1);
            var parent = ReadInternal(_bufferPool.ReadPage(parentPath.PageId));
            parent.Keys.Insert(parentPath.ChildIndex, separator);
            parent.Children.Insert(parentPath.ChildIndex + 1, rightPageId);
            if (parent.Keys.Count <= _maxKeys) { WriteInternal(parentPath.PageId, parent); return; }
            int middle = parent.Keys.Count / 2;
            long promoted = parent.Keys[middle];
            var right = new InternalNode();
            right.Keys.AddRange(parent.Keys.Skip(middle + 1));
            right.Children.AddRange(parent.Children.Skip(middle + 1));
            parent.Keys.RemoveRange(middle, parent.Keys.Count - middle);
            parent.Children.RemoveRange(middle + 1, parent.Children.Count - middle - 1);
            int rightId = AllocateTreePage();
            WriteInternal(parentPath.PageId, parent);
            WriteInternal(rightId, right);
            leftPageId = parentPath.PageId;
            separator = promoted;
            rightPageId = rightId;
        }
        var root = new InternalNode();
        root.Keys.Add(separator);
        root.Children.Add(leftPageId);
        root.Children.Add(rightPageId);
        _rootPageId = AllocateTreePage();
        WriteInternal(_rootPageId, root);
    }

    private void UpdateAncestorMinimum(List<PathEntry> path, long minimum)
    {
        for (int i = path.Count - 1; i >= 0; i--)
        {
            var entry = path[i];
            if (entry.ChildIndex == 0) continue;
            var parent = ReadInternal(_bufferPool.ReadPage(entry.PageId));
            parent.Keys[entry.ChildIndex - 1] = minimum;
            WriteInternal(entry.PageId, parent);
            return;
        }
    }

    private long? FindSubtreeMinimum(int pageId)
    {
        while (true)
        {
            byte[] page = _bufferPool.ReadPage(pageId);
            if (page[0] == LeafPageType)
            {
                var leaf = ReadLeaf(pageId);
                return leaf.Entries.Count == 0 ? null : leaf.Entries[0].Key;
            }
            pageId = ReadInternal(page).Children[0];
        }
    }

    private void RebalanceLeaf(int leafPageId, LeafNode leaf, List<PathEntry> path)
    {
        if (path.Count == 0) { WriteLeaf(leafPageId, leaf); return; }
        PathEntry parentPath = path[^1];
        path.RemoveAt(path.Count - 1);
        var parent = ReadInternal(_bufferPool.ReadPage(parentPath.PageId));
        int index = parentPath.ChildIndex;
        if (index > 0)
        {
            int leftId = parent.Children[index - 1]; var left = ReadLeaf(leftId);
            if (left.Entries.Count > MinimumLeafKeys) { leaf.Entries.Insert(0, left.Entries[^1]); left.Entries.RemoveAt(left.Entries.Count - 1); parent.Keys[index - 1] = leaf.Entries[0].Key; WriteLeaf(leftId, left); WriteLeaf(leafPageId, leaf); WriteInternal(parentPath.PageId, parent); return; }
        }
        if (index + 1 < parent.Children.Count)
        {
            int rightId = parent.Children[index + 1]; var right = ReadLeaf(rightId);
            if (right.Entries.Count > MinimumLeafKeys) { leaf.Entries.Add(right.Entries[0]); right.Entries.RemoveAt(0); parent.Keys[index] = right.Entries[0].Key; WriteLeaf(leafPageId, leaf); WriteLeaf(rightId, right); WriteInternal(parentPath.PageId, parent); return; }
        }
        if (index > 0)
        {
            int leftId = parent.Children[index - 1]; var left = ReadLeaf(leftId); left.Entries.AddRange(leaf.Entries); left.NextPageId = leaf.NextPageId; WriteLeaf(leftId, left); FreeTreePage(leafPageId); parent.Children.RemoveAt(index); parent.Keys.RemoveAt(index - 1);
        }
        else
        {
            int rightId = parent.Children[index + 1]; var right = ReadLeaf(rightId); leaf.Entries.AddRange(right.Entries); leaf.NextPageId = right.NextPageId; WriteLeaf(leafPageId, leaf); FreeTreePage(rightId); parent.Children.RemoveAt(index + 1); parent.Keys.RemoveAt(index);
        }
        RebalanceInternal(parentPath.PageId, parent, path);
    }

    private void RebalanceInternal(int pageId, InternalNode node, List<PathEntry> path)
    {
        if (pageId == _rootPageId)
        {
            if (node.Keys.Count == 0) { int old = _rootPageId; _rootPageId = node.Children[0]; FreeTreePage(old); }
            else WriteInternal(pageId, node);
            return;
        }
        if (node.Keys.Count >= MinimumInternalKeys) { WriteInternal(pageId, node); return; }
        if (path.Count == 0) { WriteInternal(pageId, node); return; }
        PathEntry parentPath = path[^1]; path.RemoveAt(path.Count - 1);
        var parent = ReadInternal(_bufferPool.ReadPage(parentPath.PageId));
        int index = parentPath.ChildIndex;
        if (index > 0)
        {
            int leftId = parent.Children[index - 1]; var left = ReadInternal(_bufferPool.ReadPage(leftId));
            if (left.Keys.Count > MinimumInternalKeys) { int child = left.Children[^1]; long sep = left.Keys[^1]; left.Children.RemoveAt(left.Children.Count - 1); left.Keys.RemoveAt(left.Keys.Count - 1); node.Children.Insert(0, child); node.Keys.Insert(0, parent.Keys[index - 1]); parent.Keys[index - 1] = sep; WriteInternal(leftId, left); WriteInternal(pageId, node); WriteInternal(parentPath.PageId, parent); return; }
        }
        if (index + 1 < parent.Children.Count)
        {
            int rightId = parent.Children[index + 1]; var right = ReadInternal(_bufferPool.ReadPage(rightId));
            if (right.Keys.Count > MinimumInternalKeys) { node.Keys.Add(parent.Keys[index]); node.Children.Add(right.Children[0]); parent.Keys[index] = right.Keys[0]; right.Children.RemoveAt(0); right.Keys.RemoveAt(0); WriteInternal(pageId, node); WriteInternal(rightId, right); WriteInternal(parentPath.PageId, parent); return; }
        }
        if (index > 0)
        {
            int leftId = parent.Children[index - 1]; var left = ReadInternal(_bufferPool.ReadPage(leftId)); left.Keys.Add(parent.Keys[index - 1]); left.Keys.AddRange(node.Keys); left.Children.AddRange(node.Children); WriteInternal(leftId, left); FreeTreePage(pageId); parent.Children.RemoveAt(index); parent.Keys.RemoveAt(index - 1);
        }
        else
        {
            int rightId = parent.Children[index + 1]; var right = ReadInternal(_bufferPool.ReadPage(rightId)); node.Keys.Add(parent.Keys[index]); node.Keys.AddRange(right.Keys); node.Children.AddRange(right.Children); WriteInternal(pageId, node); FreeTreePage(rightId); parent.Children.RemoveAt(index + 1); parent.Keys.RemoveAt(index);
        }
        RebalanceInternal(parentPath.PageId, parent, path);
    }
}
