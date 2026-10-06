using Constants = Engine.BPlusTreeConfig.Constants;

namespace Engine;

public sealed partial class BPlusTree
{
    private bool TryGetPayload(long key, out byte[] payload)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            int leafPageId = FindLeaf(key, new List<PathEntry>());
            var leaf = ReadLeaf(leafPageId);
            int index = LowerBound(leaf.Entries.Select(x => x.Key).ToList(), key);
            if (index < leaf.Entries.Count && leaf.Entries[index].Key == key)
            {
                payload = leaf.Entries[index].Payload.ToArray();
                return true;
            }
            payload = Array.Empty<byte>();
            return false;
        }
    }

    private int FindLeaf(long key, List<PathEntry> path)
    {
        int pageId = _rootPageId;
        while (true)
        {
            byte[] page = _bufferPool.ReadPage(pageId);
            if (page[0] == LeafPageType) return pageId;
            if (page[0] != InternalPageType)
                throw new InvalidDataException(Constants.InvalidFileError);
            var node = ReadInternal(page);
            int childIndex = UpperBound(node.Keys, key);
            path.Add(new PathEntry(pageId, childIndex));
            pageId = node.Children[childIndex];
        }
    }

    private int FindFirstLeaf()
    {
        int pageId = _rootPageId;
        while (true)
        {
            byte[] page = _bufferPool.ReadPage(pageId);
            if (page[0] == LeafPageType) return pageId;
            if (page[0] != InternalPageType)
                throw new InvalidDataException(Constants.InvalidFileError);
            pageId = ReadInternal(page).Children[0];
        }
    }

    private IReadOnlyList<KeyValuePair<long, byte[]>> ScanPayloads(long? minimum, long? maximum)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var result = new List<KeyValuePair<long, byte[]>>();
            int leafPageId = minimum.HasValue ? FindLeaf(minimum.Value, new List<PathEntry>()) : FindFirstLeaf();
            var visited = new HashSet<int>();
            while (PageIdIsValid(leafPageId))
            {
                if (!visited.Add(leafPageId)) throw new InvalidDataException(Constants.InvalidFileError);
                var leaf = ReadLeaf(leafPageId);
                foreach (var entry in leaf.Entries)
                {
                    if (minimum.HasValue && entry.Key < minimum.Value) continue;
                    if (maximum.HasValue && entry.Key > maximum.Value) return result;
                    result.Add(new KeyValuePair<long, byte[]>(entry.Key, entry.Payload.ToArray()));
                }
                leafPageId = leaf.NextPageId;
            }
            return result;
        }
    }

    private static int LowerBound(List<long> keys, long key)
    {
        int low = 0, high = keys.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (keys[middle] < key) low = middle + 1; else high = middle;
        }
        return low;
    }

    private static int UpperBound(List<long> keys, long key)
    {
        int low = 0, high = keys.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (key >= keys[middle]) low = middle + 1; else high = middle;
        }
        return low;
    }

    private static void EnsureSorted(List<long> keys)
    {
        for (int i = 1; i < keys.Count; i++)
            if (keys[i - 1] >= keys[i]) throw new InvalidDataException(Constants.InvalidFileError);
    }
}
