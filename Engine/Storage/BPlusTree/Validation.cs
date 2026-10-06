using Constants = Engine.BPlusTreeConfig.Constants;

namespace Engine;

public sealed partial class BPlusTree
{
    private (long? Minimum, long? Maximum) ValidateNode(
        int pageId, int depth, bool isRoot, List<int> leafPages,
        HashSet<int> treePages, HashSet<int> overflowPages, ref int? leafDepth)
    {
        if (!PageIdIsValid(pageId) || !treePages.Add(pageId)) throw new InvalidDataException(Constants.InvalidFileError);
        byte[] page = _bufferPool.ReadPage(pageId);
        if (page[0] == LeafPageType)
        {
            var leaf = ReadLeaf(pageId);
            leafDepth ??= depth;
            if (leafDepth != depth || (!isRoot && leaf.Entries.Count < MinimumLeafKeys) || leaf.Entries.Count > _maxKeys)
                throw new InvalidDataException(Constants.InvalidFileError);
            foreach (var entry in leaf.Entries) ValidateOverflowPages(entry, treePages, overflowPages);
            leafPages.Add(pageId);
            return leaf.Entries.Count == 0 ? (null, null) : (leaf.Entries[0].Key, leaf.Entries[^1].Key);
        }
        if (page[0] != InternalPageType) throw new InvalidDataException(Constants.InvalidFileError);
        var node = ReadInternal(page);
        if (node.Children.Count != node.Keys.Count + 1 || (isRoot && node.Children.Count < 2) || (!isRoot && node.Keys.Count < MinimumInternalKeys))
            throw new InvalidDataException(Constants.InvalidFileError);
        long? min = null, max = null;
        for (int i = 0; i < node.Children.Count; i++)
        {
            var bounds = ValidateNode(node.Children[i], depth + 1, false, leafPages, treePages, overflowPages, ref leafDepth);
            if (i > 0 && (!bounds.Minimum.HasValue || bounds.Minimum.Value != node.Keys[i - 1])) throw new InvalidDataException(Constants.InvalidFileError);
            if (i < node.Keys.Count && bounds.Maximum.HasValue && bounds.Maximum.Value >= node.Keys[i]) throw new InvalidDataException(Constants.InvalidFileError);
            min ??= bounds.Minimum;
            if (bounds.Maximum.HasValue) max = bounds.Maximum;
        }
        return (min, max);
    }

    private void ValidateOverflowPages(LeafEntry entry, HashSet<int> treePages, HashSet<int> overflowPages)
    {
        if (entry.OverflowHeadPageId == Constants.InvalidPageId) return;
        int pageId = entry.OverflowHeadPageId, copied = 0;
        var visited = new HashSet<int>();
        while (copied < entry.Payload.Length)
        {
            if (!PageIdIsValid(pageId) || !visited.Add(pageId) || treePages.Contains(pageId) || !overflowPages.Add(pageId)) throw new InvalidDataException(Constants.InvalidFileError);
            byte[] page = _bufferPool.ReadPage(pageId);
            int next = ReadInt32(page, 1), bytes = ReadInt32(page, 5);
            if (page[0] != OverflowPageType || bytes <= 0 || bytes > page.Length - NodeHeaderSize || copied + bytes > entry.Payload.Length) throw new InvalidDataException(Constants.InvalidFileError);
            copied += bytes;
            pageId = next;
        }
        if (pageId != Constants.InvalidPageId) throw new InvalidDataException(Constants.InvalidFileError);
    }
}
