using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Engine.BufferPool;

namespace Engine;

public readonly record struct PageId(int Value)
{
    public static PageId Invalid => new(-1);
    public bool IsValid => Value > 0;
}

public sealed class PagedPrimaryKeyBPlusTree : IDisposable
{
    private const int DefaultMetadataPageId = 1;
    private const int NodeHeaderSize = 9;
    private const int LeafEntryHeaderSize = sizeof(long) + sizeof(byte) + sizeof(int);
    private const int LeafOverflowEntrySize = LeafEntryHeaderSize + sizeof(int);
    private const int InternalEntrySize = sizeof(long) + sizeof(int);
    private const byte LeafPageType = 1;
    private const byte InternalPageType = 2;
    private const byte OverflowPageType = 3;
    private const byte FreePageType = 4;
    private const int FormatVersion = 3;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("RDBI");

    private readonly IBufferPool _bufferPool;
    private readonly int _maxKeys;
    private readonly int _metadataPageId;
    private readonly bool _ownsBufferPool;
    private readonly object _sync = new();
    private int _rootPageId;
    private int _freePageHead = -1;
    private long _count;
    private bool _disposed;

    public PagedPrimaryKeyBPlusTree(
        string filePath,
        int maxKeys = 32,
        int bufferPoolCapacity = Constants.DefaultBufferPoolCapacity)
        : this(
            new ClockBufferPool(filePath, bufferPoolCapacity),
            DefaultMetadataPageId,
            maxKeys,
            ownsBufferPool: true,
            initializeNew: false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
    }

    private PagedPrimaryKeyBPlusTree(
        IBufferPool bufferPool,
        int metadataPageId,
        int maxKeys,
        bool ownsBufferPool,
        bool initializeNew)
    {
        if (maxKeys < 3)
            throw new ArgumentOutOfRangeException(nameof(maxKeys));

        _bufferPool = bufferPool;
        _metadataPageId = metadataPageId;
        _ownsBufferPool = ownsBufferPool;
        _maxKeys = maxKeys;
        if (NodeHeaderSize + (LeafOverflowEntrySize * maxKeys) > _bufferPool.PageSize ||
            NodeHeaderSize + sizeof(int) + (InternalEntrySize * maxKeys) > _bufferPool.PageSize)
        {
            if (_ownsBufferPool)
                _bufferPool.Dispose();
            throw new ArgumentOutOfRangeException(nameof(maxKeys));
        }

        try
        {
            if (initializeNew)
                Initialize();
            else if (_bufferPool.PageCount == 1)
            {
                if (_bufferPool.AllocatePage() != _metadataPageId)
                    throw new InvalidDataException(Constants.InvalidFileError);
                Initialize();
            }
            else
                ReadMetadata();
        }
        catch
        {
            if (_ownsBufferPool)
                _bufferPool.Dispose();
            throw;
        }
    }

    internal int MetadataPageId => _metadataPageId;

    internal static PagedPrimaryKeyBPlusTree CreateOnBufferPool(
        IBufferPool bufferPool,
        int maxKeys)
    {
        int metadataPageId = bufferPool.AllocatePage();
        return new PagedPrimaryKeyBPlusTree(
            bufferPool,
            metadataPageId,
            maxKeys,
            ownsBufferPool: false,
            initializeNew: true);
    }

    internal static PagedPrimaryKeyBPlusTree Open(
        IBufferPool bufferPool,
        int metadataPageId,
        int maxKeys) =>
        new(
            bufferPool,
            metadataPageId,
            maxKeys,
            ownsBufferPool: false,
            initializeNew: false);

    public long Count
    {
        get
        {
            lock (_sync)
                return _count;
        }
    }

    public PageId RootPageId
    {
        get
        {
            lock (_sync)
                return new PageId(_rootPageId);
        }
    }

    public int PageCount => _bufferPool.PageCount;

    public int FreePageCount
    {
        get
        {
            lock (_sync)
            {
                int count = 0;
                int pageId = _freePageHead;
                var visited = new HashSet<int>();
                while (pageId > 0)
                {
                    if (!PageIdIsValid(pageId) || !visited.Add(pageId))
                        throw new InvalidDataException(Constants.InvalidFileError);
                    byte[] page = _bufferPool.ReadPage(pageId);
                    if (page[0] != FreePageType)
                        throw new InvalidDataException(Constants.InvalidFileError);
                    count++;
                    pageId = ReadInt32(page, 1);
                }
                return count;
            }
        }
    }

    public bool ContainsKey(long key) => TryGetPayload(key, out _);

    public bool TryGetValue(long key, out long value)
    {
        if (!TryGetPayload(key, out var payload) || payload.Length != sizeof(long))
        {
            value = default;
            return false;
        }

        value = BitConverter.ToInt64(payload);
        return true;
    }

    public bool TryGetRecord(long key, out Row? row)
    {
        if (!TryGetPayload(key, out var payload))
        {
            row = null;
            return false;
        }

        row = DeserializeRow(payload);
        return row is not null;
    }

    private bool TryGetPayload(long key, out byte[] payload)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var path = new List<PathEntry>();
            int leafPageId = FindLeaf(key, path);
            var leaf = ReadLeaf(leafPageId);
            int index = LowerBound(leaf.Entries.Select(entry => entry.Key).ToList(), key);

            if (index < leaf.Entries.Count && leaf.Entries[index].Key == key)
            {
                payload = leaf.Entries[index].Payload;
                return true;
            }

            payload = Array.Empty<byte>();
            return false;
        }
    }

    public void Insert(long key, long value)
    {
        InsertPayload(key, BitConverter.GetBytes(value));
    }

    public void InsertRecord(long key, Row row)
    {
        ArgumentNullException.ThrowIfNull(row);
        InsertPayload(key, JsonSerializer.SerializeToUtf8Bytes(row));
    }

    public void UpdateRecord(long key, Row row)
    {
        ArgumentNullException.ThrowIfNull(row);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(row);
        lock (_sync)
        {
            ThrowIfDisposed();
            var path = new List<PathEntry>();
            int leafPageId = FindLeaf(key, path);
            var leaf = ReadLeaf(leafPageId);
            int index = LowerBound(leaf.Entries.Select(entry => entry.Key).ToList(), key);
            if (index >= leaf.Entries.Count || leaf.Entries[index].Key != key)
                throw new InvalidOperationException(Constants.InvalidFileError);

            FreeOverflowPages(leaf.Entries[index].OverflowHeadPageId);
            leaf.Entries[index] = new LeafEntry(key, payload);
            EnsureOverflowEntries(leaf);
            if (RequiresSplit(leaf))
                SplitLeaf(leafPageId, leaf, path);
            else
                WriteLeaf(leafPageId, leaf);
            WriteMetadata();
        }
    }

    private void InsertPayload(long key, byte[] payload)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var path = new List<PathEntry>();
            int leafPageId = FindLeaf(key, path);
            var leaf = ReadLeaf(leafPageId);
            int index = LowerBound(leaf.Entries.Select(entry => entry.Key).ToList(), key);

            if (index < leaf.Entries.Count && leaf.Entries[index].Key == key)
                throw new InvalidOperationException(Constants.DuplicatePrimaryKeyError);

            leaf.Entries.Insert(index, new LeafEntry(key, payload));
            EnsureOverflowEntries(leaf);

            if (!RequiresSplit(leaf))
            {
                WriteLeaf(leafPageId, leaf);
            }
            else
            {
                SplitLeaf(leafPageId, leaf, path);
            }

            _count++;
            WriteMetadata();
        }
    }

    public bool Delete(long key)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var path = new List<PathEntry>();
            int leafPageId = FindLeaf(key, path);
            var leaf = ReadLeaf(leafPageId);
            int index = LowerBound(leaf.Entries.Select(entry => entry.Key).ToList(), key);

            if (index >= leaf.Entries.Count || leaf.Entries[index].Key != key)
                return false;

            FreeOverflowPages(leaf.Entries[index].OverflowHeadPageId);
            leaf.Entries.RemoveAt(index);
            if (leafPageId == _rootPageId)
            {
                WriteLeaf(leafPageId, leaf);
            }
            else if (leaf.Entries.Count >= MinimumLeafKeys)
            {
                WriteLeaf(leafPageId, leaf);
                if (leaf.Entries.Count > 0)
                    UpdateAncestorMinimum(path, leaf.Entries[0].Key);
            }
            else
            {
                RebalanceLeaf(leafPageId, leaf, path);
            }

            _count--;
            WriteMetadata();
            return true;
        }
    }

    private void RebalanceLeaf(int leafPageId, LeafNode leaf, List<PathEntry> path)
    {
        PathEntry parentPath = path[^1];
        path.RemoveAt(path.Count - 1);
        var parent = ReadInternal(_bufferPool.ReadPage(parentPath.PageId));
        int childIndex = parentPath.ChildIndex;

        if (childIndex > 0)
        {
            int leftPageId = parent.Children[childIndex - 1];
            var left = ReadLeaf(leftPageId);
            if (left.Entries.Count > MinimumLeafKeys)
            {
                leaf.Entries.Insert(0, left.Entries[^1]);
                left.Entries.RemoveAt(left.Entries.Count - 1);
                parent.Keys[childIndex - 1] = leaf.Entries[0].Key;
                WriteLeaf(leftPageId, left);
                WriteLeaf(leafPageId, leaf);
                WriteInternal(parentPath.PageId, parent);
                return;
            }
        }

        if (childIndex < parent.Children.Count - 1)
        {
            int rightPageId = parent.Children[childIndex + 1];
            var right = ReadLeaf(rightPageId);
            if (right.Entries.Count > MinimumLeafKeys)
            {
                leaf.Entries.Add(right.Entries[0]);
                right.Entries.RemoveAt(0);
                parent.Keys[childIndex] = right.Entries[0].Key;
                if (childIndex > 0)
                    parent.Keys[childIndex - 1] = leaf.Entries[0].Key;
                WriteLeaf(leafPageId, leaf);
                WriteLeaf(rightPageId, right);
                WriteInternal(parentPath.PageId, parent);
                if (childIndex == 0 && leaf.Entries.Count > 0)
                    UpdateAncestorMinimum(path, leaf.Entries[0].Key);
                return;
            }
        }

        if (childIndex > 0)
        {
            int leftPageId = parent.Children[childIndex - 1];
            var left = ReadLeaf(leftPageId);
            left.Entries.AddRange(leaf.Entries);
            left.NextPageId = leaf.NextPageId;
            WriteLeaf(leftPageId, left);
            FreeTreePage(leafPageId);
            parent.Children.RemoveAt(childIndex);
            parent.Keys.RemoveAt(childIndex - 1);
        }
        else
        {
            int rightPageId = parent.Children[childIndex + 1];
            var right = ReadLeaf(rightPageId);
            leaf.Entries.AddRange(right.Entries);
            leaf.NextPageId = right.NextPageId;
            WriteLeaf(leafPageId, leaf);
            FreeTreePage(rightPageId);
            parent.Children.RemoveAt(childIndex + 1);
            parent.Keys.RemoveAt(childIndex);
        }

        RebalanceInternal(parentPath.PageId, parent, path);
    }

    private void RebalanceInternal(int pageId, InternalNode node, List<PathEntry> path)
    {
        if (pageId == _rootPageId)
        {
            if (node.Keys.Count == 0)
            {
                int oldRootPageId = _rootPageId;
                _rootPageId = node.Children[0];
                FreeTreePage(oldRootPageId);
            }
            else
            {
                WriteInternal(pageId, node);
            }
            return;
        }

        if (node.Keys.Count >= MinimumInternalKeys)
        {
            WriteInternal(pageId, node);
            var subtreeMinimum = FindSubtreeMinimum(node.Children[0]);
            if (subtreeMinimum.HasValue)
                UpdateAncestorMinimum(path, subtreeMinimum.Value);
            return;
        }

        PathEntry parentPath = path[^1];
        path.RemoveAt(path.Count - 1);
        var parent = ReadInternal(_bufferPool.ReadPage(parentPath.PageId));
        int childIndex = parentPath.ChildIndex;

        if (childIndex > 0)
        {
            int leftPageId = parent.Children[childIndex - 1];
            var left = ReadInternal(_bufferPool.ReadPage(leftPageId));
            if (left.Keys.Count > MinimumInternalKeys)
            {
                int movedChildIndex = left.Children.Count - 1;
                int movedChild = left.Children[movedChildIndex];
                long newParentSeparator = left.Keys[^1];
                left.Children.RemoveAt(movedChildIndex);
                left.Keys.RemoveAt(left.Keys.Count - 1);
                node.Children.Insert(0, movedChild);
                node.Keys.Insert(0, parent.Keys[childIndex - 1]);
                parent.Keys[childIndex - 1] = newParentSeparator;
                WriteInternal(leftPageId, left);
                WriteInternal(pageId, node);
                WriteInternal(parentPath.PageId, parent);
                return;
            }
        }

        if (childIndex < parent.Children.Count - 1)
        {
            int rightPageId = parent.Children[childIndex + 1];
            var right = ReadInternal(_bufferPool.ReadPage(rightPageId));
            if (right.Keys.Count > MinimumInternalKeys)
            {
                node.Keys.Add(parent.Keys[childIndex]);
                node.Children.Add(right.Children[0]);
                parent.Keys[childIndex] = right.Keys[0];
                right.Children.RemoveAt(0);
                right.Keys.RemoveAt(0);
                WriteInternal(pageId, node);
                WriteInternal(rightPageId, right);
                WriteInternal(parentPath.PageId, parent);
                return;
            }
        }

        if (childIndex > 0)
        {
            int leftPageId = parent.Children[childIndex - 1];
            var left = ReadInternal(_bufferPool.ReadPage(leftPageId));
            left.Keys.Add(parent.Keys[childIndex - 1]);
            left.Keys.AddRange(node.Keys);
            left.Children.AddRange(node.Children);
            WriteInternal(leftPageId, left);
            FreeTreePage(pageId);
            parent.Children.RemoveAt(childIndex);
            parent.Keys.RemoveAt(childIndex - 1);
        }
        else
        {
            int rightPageId = parent.Children[childIndex + 1];
            var right = ReadInternal(_bufferPool.ReadPage(rightPageId));
            node.Keys.Add(parent.Keys[childIndex]);
            node.Keys.AddRange(right.Keys);
            node.Children.AddRange(right.Children);
            WriteInternal(pageId, node);
            FreeTreePage(rightPageId);
            parent.Children.RemoveAt(childIndex + 1);
            parent.Keys.RemoveAt(childIndex);
        }

        RebalanceInternal(parentPath.PageId, parent, path);
    }

    private void UpdateAncestorMinimum(List<PathEntry> path, long minimum)
    {
        for (int index = path.Count - 1; index >= 0; index--)
        {
            PathEntry entry = path[index];
            if (entry.ChildIndex == 0)
                continue;

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

    private int MinimumLeafKeys => (_maxKeys + 1) / 2;
    private int MinimumInternalKeys => _maxKeys / 2;

    public IReadOnlyList<KeyValuePair<long, long>> Scan(
        long? minimum = null,
        long? maximum = null)
    {
        var result = new List<KeyValuePair<long, long>>();
        foreach (var entry in ScanPayloads(minimum, maximum))
        {
            if (entry.Value.Length != sizeof(long))
                throw new InvalidOperationException(Constants.InvalidFileError);
            result.Add(new KeyValuePair<long, long>(entry.Key, BitConverter.ToInt64(entry.Value)));
        }
        return result;
    }

    public IReadOnlyList<KeyValuePair<long, Row>> ScanRecords(
        long? minimum = null,
        long? maximum = null)
    {
        var result = new List<KeyValuePair<long, Row>>();
        foreach (var entry in ScanPayloads(minimum, maximum))
        {
            var row = DeserializeRow(entry.Value);
            result.Add(new KeyValuePair<long, Row>(entry.Key, row));
        }
        return result;
    }

    private IReadOnlyList<KeyValuePair<long, byte[]>> ScanPayloads(
        long? minimum,
        long? maximum)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var result = new List<KeyValuePair<long, byte[]>>();
            int leafPageId = minimum.HasValue
                ? FindLeaf(minimum.Value, new List<PathEntry>())
                : FindFirstLeaf();
            var visitedLeaves = new HashSet<int>();

            while (PageIdIsValid(leafPageId))
            {
                if (!visitedLeaves.Add(leafPageId))
                    throw new InvalidDataException(Constants.InvalidFileError);

                var leaf = ReadLeaf(leafPageId);
                foreach (var entry in leaf.Entries)
                {
                    if (minimum.HasValue && entry.Key < minimum.Value)
                        continue;
                    if (maximum.HasValue && entry.Key > maximum.Value)
                        return result;

                    result.Add(new KeyValuePair<long, byte[]>(entry.Key, entry.Payload));
                }

                leafPageId = leaf.NextPageId;
            }

            return result;
        }
    }

    private static Row DeserializeRow(byte[] payload)
    {
        var row = JsonSerializer.Deserialize<Row>(payload)
            ?? throw new InvalidDataException(Constants.InvalidFileError);

        foreach (var (column, value) in row.Values.ToList())
        {
            if (value is not JsonElement json)
                continue;

            row.Values[column] = json.ValueKind switch
            {
                JsonValueKind.String => json.GetString()!,
                JsonValueKind.Number when json.TryGetInt32(out int intValue) => intValue,
                JsonValueKind.Number when json.TryGetInt64(out long longValue) => longValue,
                JsonValueKind.Number => json.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null!,
                _ => json
            };
        }

        return row;
    }

    public void Validate()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var leafPages = new List<int>();
            int? leafDepth = null;
            ValidateNode(_rootPageId, 0, leafPages, ref leafDepth);

            var scanned = ScanPayloads(null, null);
            if (scanned.Count != _count)
                throw new InvalidDataException(Constants.InvalidFileError);

            int leafPageId = leafPages.Count == 0 ? -1 : leafPages[0];
            foreach (int expectedLeafPageId in leafPages)
            {
                if (leafPageId != expectedLeafPageId)
                    throw new InvalidDataException(Constants.InvalidFileError);
                leafPageId = ReadLeaf(leafPageId).NextPageId;
            }

            if (PageIdIsValid(leafPageId))
                throw new InvalidDataException(Constants.InvalidFileError);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            if (_ownsBufferPool)
                _bufferPool.Dispose();
            _disposed = true;
        }
    }

    private void Initialize()
    {
        _rootPageId = AllocateTreePage();
        _count = 0;
        WriteLeaf(_rootPageId, new LeafNode());
        WriteMetadata();
    }

    private void ReadMetadata()
    {
        if (_bufferPool.PageCount <= _metadataPageId)
            throw new InvalidDataException(Constants.InvalidFileError);

        byte[] page = _bufferPool.ReadPage(_metadataPageId);
        if (!page.AsSpan(0, Magic.Length).SequenceEqual(Magic) ||
            ReadInt32(page, 4) != FormatVersion ||
            ReadInt32(page, 20) != _maxKeys)
        {
            throw new InvalidDataException(Constants.InvalidFileError);
        }

        _rootPageId = ReadInt32(page, 8);
        _count = ReadInt64(page, 12);
        _freePageHead = ReadInt32(page, 24);
        if (_rootPageId <= _metadataPageId ||
            _rootPageId >= _bufferPool.PageCount ||
            _count < 0 ||
            (_freePageHead != -1 && !PageIdIsValid(_freePageHead)))
            throw new InvalidDataException(Constants.InvalidFileError);
    }

    private void WriteMetadata()
    {
        byte[] page = new byte[_bufferPool.PageSize];
        Magic.CopyTo(page, 0);
        WriteInt32(page, 4, FormatVersion);
        WriteInt32(page, 8, _rootPageId);
        WriteInt64(page, 12, _count);
        WriteInt32(page, 20, _maxKeys);
        WriteInt32(page, 24, _freePageHead);
        _bufferPool.WritePage(_metadataPageId, page);
    }

    private int FindLeaf(long key, List<PathEntry> path)
    {
        int pageId = _rootPageId;
        while (true)
        {
            byte[] page = _bufferPool.ReadPage(pageId);
            if (page[0] == LeafPageType)
                return pageId;
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
            if (page[0] == LeafPageType)
                return pageId;
            if (page[0] != InternalPageType)
                throw new InvalidDataException(Constants.InvalidFileError);

            pageId = ReadInternal(page).Children[0];
        }
    }

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
        int pageCapacity = _bufferPool.PageSize - NodeHeaderSize;
        int bestIndex = -1;
        int bestDifference = int.MaxValue;
        int leftBytes = 0;
        int totalBytes = leaf.Entries.Sum(GetLeafEntrySize);

        for (int index = 1; index < leaf.Entries.Count; index++)
        {
            leftBytes += GetLeafEntrySize(leaf.Entries[index - 1]);
            int rightBytes = totalBytes - leftBytes;
            if (index < MinimumLeafKeys || leaf.Entries.Count - index < MinimumLeafKeys ||
                index > _maxKeys || leaf.Entries.Count - index > _maxKeys ||
                leftBytes > pageCapacity || rightBytes > pageCapacity)
            {
                continue;
            }

            int difference = Math.Abs(leftBytes - rightBytes);
            if (difference < bestDifference)
            {
                bestIndex = index;
                bestDifference = difference;
            }
        }

        if (bestIndex < 0)
            throw new InvalidOperationException(Constants.PageDataTooLargeError);
        return bestIndex;
    }

    private bool RequiresSplit(LeafNode leaf) =>
        leaf.Entries.Count > _maxKeys ||
        leaf.Entries.Sum(GetLeafEntrySize) > _bufferPool.PageSize - NodeHeaderSize;

    private static int GetLeafEntrySize(LeafEntry entry) =>
        entry.OverflowHeadPageId > 0
            ? LeafOverflowEntrySize
            : LeafEntryHeaderSize + entry.Payload.Length;

    private void InsertIntoParent(
        int leftPageId,
        long separator,
        int rightPageId,
        List<PathEntry> path)
    {
        while (path.Count > 0)
        {
            int lastPathIndex = path.Count - 1;
            PathEntry parentPath = path[lastPathIndex];
            path.RemoveAt(lastPathIndex);

            var parent = ReadInternal(_bufferPool.ReadPage(parentPath.PageId));
            parent.Keys.Insert(parentPath.ChildIndex, separator);
            parent.Children.Insert(parentPath.ChildIndex + 1, rightPageId);

            if (parent.Keys.Count <= _maxKeys)
            {
                WriteInternal(parentPath.PageId, parent);
                return;
            }

            int middle = parent.Keys.Count / 2;
            long promotedKey = parent.Keys[middle];
            var right = new InternalNode();
            right.Keys.AddRange(parent.Keys.Skip(middle + 1));
            right.Children.AddRange(parent.Children.Skip(middle + 1));
            parent.Keys.RemoveRange(middle, parent.Keys.Count - middle);
            parent.Children.RemoveRange(middle + 1, parent.Children.Count - middle - 1);

            int rightInternalPageId = AllocateTreePage();
            WriteInternal(parentPath.PageId, parent);
            WriteInternal(rightInternalPageId, right);

            leftPageId = parentPath.PageId;
            separator = promotedKey;
            rightPageId = rightInternalPageId;
        }

        var newRoot = new InternalNode
        {
            Keys = { separator },
            Children = { leftPageId, rightPageId }
        };
        _rootPageId = AllocateTreePage();
        WriteInternal(_rootPageId, newRoot);
    }

    private void WriteLeaf(int pageId, LeafNode node)
    {
        EnsureOverflowEntries(node);
        byte[] page = new byte[_bufferPool.PageSize];
        page[0] = LeafPageType;
        WriteInt32(page, 1, node.Entries.Count);
        WriteInt32(page, 5, node.NextPageId);
        int offset = NodeHeaderSize;
        foreach (var entry in node.Entries)
        {
            WriteInt64(page, offset, entry.Key);
            WriteInt32(page, offset + sizeof(long) + sizeof(byte), entry.Payload.Length);
            if (entry.OverflowHeadPageId > 0)
            {
                page[offset + sizeof(long)] = 1;
                WriteInt32(page, offset + LeafEntryHeaderSize, entry.OverflowHeadPageId);
                offset += LeafOverflowEntrySize;
            }
            else
            {
                page[offset + sizeof(long)] = 0;
                entry.Payload.CopyTo(page, offset + LeafEntryHeaderSize);
                offset += LeafEntryHeaderSize + entry.Payload.Length;
            }
        }

        if (offset > page.Length)
            throw new InvalidOperationException(Constants.PageDataTooLargeError);
        _bufferPool.WritePage(pageId, page);
    }

    private LeafNode ReadLeaf(int pageId)
    {
        byte[] page = _bufferPool.ReadPage(pageId);
        if (page[0] != LeafPageType)
            throw new InvalidDataException(Constants.InvalidFileError);

        int count = ReadInt32(page, 1);
        if (count < 0 || count > _maxKeys)
        {
            throw new InvalidDataException(Constants.InvalidFileError);
        }

        var leaf = new LeafNode { NextPageId = ReadInt32(page, 5) };
        int offset = NodeHeaderSize;
        for (int index = 0; index < count; index++)
        {
            if (offset + LeafEntryHeaderSize > page.Length)
                throw new InvalidDataException(Constants.InvalidFileError);

            long key = ReadInt64(page, offset);
            byte storageKind = page[offset + sizeof(long)];
            int payloadLength = ReadInt32(page, offset + sizeof(long) + sizeof(byte));
            if (payloadLength <= 0)
                throw new InvalidDataException(Constants.InvalidFileError);

            if (storageKind == 0)
            {
                if (offset + LeafEntryHeaderSize + payloadLength > page.Length)
                    throw new InvalidDataException(Constants.InvalidFileError);

                byte[] payload = page.AsSpan(offset + LeafEntryHeaderSize, payloadLength).ToArray();
                leaf.Entries.Add(new LeafEntry(key, payload));
                offset += LeafEntryHeaderSize + payloadLength;
            }
            else if (storageKind == 1)
            {
                if (offset + LeafOverflowEntrySize > page.Length)
                    throw new InvalidDataException(Constants.InvalidFileError);

                int overflowHeadPageId = ReadInt32(page, offset + LeafEntryHeaderSize);
                byte[] payload = ReadOverflowPayload(overflowHeadPageId, payloadLength);
                leaf.Entries.Add(new LeafEntry(key, payload, overflowHeadPageId));
                offset += LeafOverflowEntrySize;
            }
            else
            {
                throw new InvalidDataException(Constants.InvalidFileError);
            }
        }

        EnsureSorted(leaf.Entries.Select(entry => entry.Key).ToList());
        return leaf;
    }

    private void EnsureOverflowEntries(LeafNode leaf)
    {
        int inlinePayloadLimit = (_bufferPool.PageSize - NodeHeaderSize - LeafEntryHeaderSize) / 2;
        foreach (var entry in leaf.Entries)
        {
            if (entry.OverflowHeadPageId <= 0 && entry.Payload.Length > inlinePayloadLimit)
                entry.OverflowHeadPageId = WriteOverflowPayload(entry.Payload);
        }
    }

    private int AllocateTreePage()
    {
        if (_freePageHead < 0)
            return _bufferPool.AllocatePage();

        int pageId = _freePageHead;
        byte[] page = _bufferPool.ReadPage(pageId);
        if (page[0] != FreePageType)
            throw new InvalidDataException(Constants.InvalidFileError);

        _freePageHead = ReadInt32(page, 1);
        return pageId;
    }

    private void FreeTreePage(int pageId)
    {
        if (pageId <= _metadataPageId || pageId >= _bufferPool.PageCount)
            throw new InvalidDataException(Constants.InvalidFileError);

        byte[] page = new byte[_bufferPool.PageSize];
        page[0] = FreePageType;
        WriteInt32(page, 1, _freePageHead);
        _bufferPool.WritePage(pageId, page);
        _freePageHead = pageId;
    }

    private void FreeOverflowPages(int firstPageId)
    {
        int pageId = firstPageId;
        var visitedPages = new HashSet<int>();
        while (pageId > 0)
        {
            if (!PageIdIsValid(pageId) || !visitedPages.Add(pageId))
                throw new InvalidDataException(Constants.InvalidFileError);

            byte[] page = _bufferPool.ReadPage(pageId);
            if (page[0] != OverflowPageType)
                throw new InvalidDataException(Constants.InvalidFileError);

            int nextPageId = ReadInt32(page, 1);
            FreeTreePage(pageId);
            pageId = nextPageId;
        }

        if (pageId != -1)
            throw new InvalidDataException(Constants.InvalidFileError);
    }

    private int WriteOverflowPayload(byte[] payload)
    {
        int chunkSize = _bufferPool.PageSize - NodeHeaderSize;
        int pageCount = (payload.Length + chunkSize - 1) / chunkSize;
        var pageIds = new int[pageCount];
        for (int index = 0; index < pageCount; index++)
            pageIds[index] = AllocateTreePage();

        int payloadOffset = 0;
        for (int index = 0; index < pageCount; index++)
        {
            int bytesInPage = Math.Min(chunkSize, payload.Length - payloadOffset);
            byte[] page = new byte[_bufferPool.PageSize];
            page[0] = OverflowPageType;
            WriteInt32(page, 1, index + 1 < pageCount ? pageIds[index + 1] : -1);
            WriteInt32(page, 5, bytesInPage);
            payload.AsSpan(payloadOffset, bytesInPage).CopyTo(page.AsSpan(NodeHeaderSize));
            _bufferPool.WritePage(pageIds[index], page);
            payloadOffset += bytesInPage;
        }

        return pageIds[0];
    }

    private byte[] ReadOverflowPayload(int firstPageId, int payloadLength)
    {
        if (firstPageId <= 0 || payloadLength <= 0)
            throw new InvalidDataException(Constants.InvalidFileError);

        byte[] payload = new byte[payloadLength];
        int copied = 0;
        int pageId = firstPageId;
        var visitedPages = new HashSet<int>();
        while (copied < payloadLength)
        {
            if (!PageIdIsValid(pageId) || !visitedPages.Add(pageId))
                throw new InvalidDataException(Constants.InvalidFileError);

            byte[] page = _bufferPool.ReadPage(pageId);
            int nextPageId = ReadInt32(page, 1);
            int bytesInPage = ReadInt32(page, 5);
            if (page[0] != OverflowPageType || bytesInPage <= 0 ||
                bytesInPage > page.Length - NodeHeaderSize || copied + bytesInPage > payloadLength)
            {
                throw new InvalidDataException(Constants.InvalidFileError);
            }

            page.AsSpan(NodeHeaderSize, bytesInPage).CopyTo(payload.AsSpan(copied));
            copied += bytesInPage;
            pageId = nextPageId;
        }

        if (pageId != -1)
            throw new InvalidDataException(Constants.InvalidFileError);

        return payload;
    }

    private void WriteInternal(int pageId, InternalNode node)
    {
        byte[] page = new byte[_bufferPool.PageSize];
        page[0] = InternalPageType;
        WriteInt32(page, 1, node.Keys.Count);
        WriteInt32(page, NodeHeaderSize, node.Children[0]);
        int offset = NodeHeaderSize + sizeof(int);
        for (int index = 0; index < node.Keys.Count; index++)
        {
            WriteInt64(page, offset, node.Keys[index]);
            WriteInt32(page, offset + sizeof(long), node.Children[index + 1]);
            offset += InternalEntrySize;
        }
        _bufferPool.WritePage(pageId, page);
    }

    private InternalNode ReadInternal(byte[] page)
    {
        if (page[0] != InternalPageType)
            throw new InvalidDataException(Constants.InvalidFileError);

        int count = ReadInt32(page, 1);
        if (count < 1 || count > _maxKeys ||
            NodeHeaderSize + sizeof(int) + count * InternalEntrySize > page.Length)
        {
            throw new InvalidDataException(Constants.InvalidFileError);
        }

        var node = new InternalNode();
        node.Children.Add(ReadInt32(page, NodeHeaderSize));
        int offset = NodeHeaderSize + sizeof(int);
        for (int index = 0; index < count; index++)
        {
            node.Keys.Add(ReadInt64(page, offset));
            node.Children.Add(ReadInt32(page, offset + sizeof(long)));
            offset += InternalEntrySize;
        }
        EnsureSorted(node.Keys);
        return node;
    }

    private (long? Minimum, long? Maximum) ValidateNode(
        int pageId,
        int depth,
        List<int> leafPages,
        ref int? leafDepth)
    {
        byte[] page = _bufferPool.ReadPage(pageId);
        if (page[0] == LeafPageType)
        {
            var leaf = ReadLeaf(pageId);
            leafDepth ??= depth;
            if (leafDepth != depth)
                throw new InvalidDataException(Constants.InvalidFileError);
            leafPages.Add(pageId);
            return leaf.Entries.Count == 0
                ? (null, null)
                : (leaf.Entries[0].Key, leaf.Entries[^1].Key);
        }

        var node = ReadInternal(page);
        if (node.Children.Count != node.Keys.Count + 1)
            throw new InvalidDataException(Constants.InvalidFileError);

        long? minimum = null;
        long? maximum = null;
        for (int index = 0; index < node.Children.Count; index++)
        {
            var bounds = ValidateNode(node.Children[index], depth + 1, leafPages, ref leafDepth);
            if (index > 0 && bounds.Minimum.HasValue && bounds.Minimum.Value < node.Keys[index - 1])
                throw new InvalidDataException(Constants.InvalidFileError);
            if (index < node.Keys.Count && bounds.Maximum.HasValue && bounds.Maximum.Value >= node.Keys[index])
                throw new InvalidDataException(Constants.InvalidFileError);
            minimum ??= bounds.Minimum;
            if (bounds.Maximum.HasValue)
                maximum = bounds.Maximum;
        }
        return (minimum, maximum);
    }

    private bool PageIdIsValid(int pageId) => pageId > 0 && pageId < _bufferPool.PageCount;

    private static int LowerBound(List<long> keys, long key)
    {
        int low = 0;
        int high = keys.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (keys[middle] < key)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private static int UpperBound(List<long> keys, long key)
    {
        int low = 0;
        int high = keys.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (key >= keys[middle])
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private static void EnsureSorted(List<long> keys)
    {
        for (int index = 1; index < keys.Count; index++)
        {
            if (keys[index - 1] >= keys[index])
                throw new InvalidDataException(Constants.InvalidFileError);
        }
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));

    private static long ReadInt64(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, sizeof(long)));

    private static void WriteInt32(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)), value);

    private static void WriteInt64(byte[] bytes, int offset, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset, sizeof(long)), value);

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PagedPrimaryKeyBPlusTree));
    }

    private sealed class LeafNode
    {
        public List<LeafEntry> Entries { get; } = new();
        public int NextPageId { get; set; } = -1;
    }

    private sealed class LeafEntry(long key, byte[] payload, int overflowHeadPageId = -1)
    {
        public long Key { get; } = key;
        public byte[] Payload { get; } = payload;
        public int OverflowHeadPageId { get; set; } = overflowHeadPageId;
    }

    private sealed class InternalNode
    {
        public List<long> Keys { get; } = new();
        public List<int> Children { get; } = new();
    }

    private sealed record PathEntry(int PageId, int ChildIndex);
}