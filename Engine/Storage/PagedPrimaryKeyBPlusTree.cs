using System.Buffers.Binary;
using System.Text.Json;
using Engine.BufferPool;

namespace Engine;

public readonly record struct PageId(int Value)
{
    public static PageId Invalid => new(Constants.InvalidPageId);
    public bool IsValid => Value >= Constants.FirstAllocatablePageId;
}

public sealed partial class PagedPrimaryKeyBPlusTree : IDisposable
{
    private readonly IBufferPool _bufferPool;
    private int _maxKeys;
    private readonly int _metadataPageId;
    private readonly bool _ownsBufferPool;
    private readonly object _sync = new();
    private MutationContext? _activeMutation;
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

        try
        {
            if (initializeNew)
            {
                ValidatePageCapacity();
                Initialize();
            }
            else if (_bufferPool.PageCount == 1)
            {
                if (_bufferPool.AllocatePage() != _metadataPageId)
                    throw new InvalidDataException(Constants.InvalidFileError);
                ValidatePageCapacity();
                Initialize();
            }
            else
            {
                ReadMetadata();
                ValidatePageCapacity();
            }
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
            {
                ThrowIfDisposed();
                return _count;
            }
        }
    }

    public PageId RootPageId
    {
        get
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                return new PageId(_rootPageId);
            }
        }
    }

    public int PageCount => _bufferPool.PageCount;

    public int FreePageCount
    {
        get
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                return CountFreePages();
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

        value = BinaryPrimitives.ReadInt64LittleEndian(payload);
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

    public void Insert(long key, long value)
    {
        byte[] payload = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(payload, value);
        InsertPayload(key, payload);
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
            ExecuteMutation(() =>
            {
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
                return true;
            });
        }
    }

    public bool Delete(long key)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return ExecuteMutation(() =>
            {
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
            });
        }
    }

    public IReadOnlyList<KeyValuePair<long, long>> Scan(long? minimum = null, long? maximum = null)
    {
        var result = new List<KeyValuePair<long, long>>();
        foreach (var entry in ScanPayloads(minimum, maximum))
        {
            if (entry.Value.Length != sizeof(long))
                throw new InvalidOperationException(Constants.InvalidFileError);

            result.Add(new KeyValuePair<long, long>(
                entry.Key,
                BinaryPrimitives.ReadInt64LittleEndian(entry.Value)));
        }

        return result;
    }

    public IReadOnlyList<KeyValuePair<long, Row>> ScanRecords(long? minimum = null, long? maximum = null)
    {
        var result = new List<KeyValuePair<long, Row>>();
        foreach (var entry in ScanPayloads(minimum, maximum))
        {
            var row = DeserializeRow(entry.Value);
            result.Add(new KeyValuePair<long, Row>(entry.Key, row));
        }

        return result;
    }

    public void Validate()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var leafPages = new List<int>();
            var treePages = new HashSet<int> { _metadataPageId };
            var overflowPages = new HashSet<int>();
            int? leafDepth = null;
            ValidateNode(_rootPageId, 0, true, leafPages, treePages, overflowPages, ref leafDepth);

            var scanned = ScanPayloads(null, null);
            if (scanned.Count != _count)
                throw new InvalidDataException(Constants.InvalidFileError);

            int leafPageId = leafPages.Count == 0 ? -1 : leafPages[0];
            foreach (var expectedLeafPageId in leafPages)
            {
                if (leafPageId != expectedLeafPageId)
                    throw new InvalidDataException(Constants.InvalidFileError);
                leafPageId = ReadLeaf(leafPageId).NextPageId;
            }

            if (PageIdIsValid(leafPageId))
                throw new InvalidDataException(Constants.InvalidFileError);

            ValidateFreeList(treePages, overflowPages);
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

    private int CountFreePages()
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
}
