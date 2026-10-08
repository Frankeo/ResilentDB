using System.Text.Json;
using System.Buffers.Binary;
using Engine.BufferPool;
using Constants = Engine.BPlusTreeConfig.Constants;
using BufferPoolConstants = Engine.BufferPool.Constants;

namespace Engine;

public readonly record struct PageId(int Value)
{
    public static PageId Invalid => new(Constants.InvalidPageId);
    public bool IsValid => Value >= Constants.FirstAllocatablePageId;
}

public sealed partial class BPlusTree : IDisposable, IPrimaryKeyIndex
{
    private readonly IBufferPool _bufferPool;
    private readonly IEngineTraceSink? _traceSink;
    private int _maxKeys;
    private readonly int _metadataPageId;
    private readonly bool _ownsBufferPool;
    private readonly object _sync = new();
    private MutationContext? _activeMutation;
    private int _rootPageId;
    private int _freePageHead = Constants.InvalidPageId;
    private long _count;
    private bool _disposed;

    public BPlusTree(string filePath, int maxKeys = Constants.DefaultMaxKeys, int bufferPoolCapacity = BufferPoolConstants.DefaultBufferPoolCapacity, IEngineTraceSink? traceSink = null)
        : this(new ClockBufferPool(filePath, bufferPoolCapacity), DefaultMetadataPageId, maxKeys, true, false, traceSink)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
    }

    private BPlusTree(IBufferPool bufferPool, int metadataPageId, int maxKeys, bool ownsBufferPool, bool initializeNew, IEngineTraceSink? traceSink)
    {
        if (maxKeys < 3) throw new ArgumentOutOfRangeException(nameof(maxKeys));
        _bufferPool = bufferPool;
        _traceSink = traceSink;
        _metadataPageId = metadataPageId;
        _ownsBufferPool = ownsBufferPool;
        _maxKeys = maxKeys;
        try
        {
            if (initializeNew) { ValidatePageCapacity(); Initialize(); }
            else if (_bufferPool.PageCount == 1)
            {
                if (_bufferPool.AllocatePage() != _metadataPageId) throw new InvalidDataException(Constants.InvalidFileError);
                ValidatePageCapacity(); Initialize();
            }
            else { ReadMetadata(); ValidatePageCapacity(); }
        }
        catch { if (_ownsBufferPool) _bufferPool.Dispose(); throw; }
    }

    internal int MetadataPageId => _metadataPageId;
    internal static BPlusTree CreateOnBufferPool(IBufferPool bufferPool, int maxKeys, IEngineTraceSink? traceSink = null)
    {
        int metadataPageId = bufferPool.AllocatePage();
        return new BPlusTree(bufferPool, metadataPageId, maxKeys, false, true, traceSink);
    }
    internal static BPlusTree Open(IBufferPool bufferPool, int metadataPageId, int maxKeys, IEngineTraceSink? traceSink = null) => new(bufferPool, metadataPageId, maxKeys, false, false, traceSink);

    public long Count { get { lock (_sync) { ThrowIfDisposed(); return _count; } } }
    public PageId RootPageId { get { lock (_sync) { ThrowIfDisposed(); return new PageId(_rootPageId); } } }
    public int PageCount => _bufferPool.PageCount;
    public int FreePageCount
    {
        get
        {
            lock (_sync)
            {
                ThrowIfDisposed(); int count = 0, pageId = _freePageHead; var visited = new HashSet<int>();
                while (pageId != Constants.InvalidPageId) { if (!PageIdIsValid(pageId) || !visited.Add(pageId)) throw new InvalidDataException(Constants.InvalidFileError); byte[] page = _bufferPool.ReadPage(pageId); if (page[0] != FreePageType) throw new InvalidDataException(Constants.InvalidFileError); count++; pageId = ReadInt32(page, 1); }
                return count;
            }
        }
    }

    public bool ContainsKey(long key) => TryGetPayload(key, out _);
    public bool TryGetValue(long key, out long value)
    {
        if (!TryGetPayload(key, out var payload) || payload.Length != sizeof(long)) { value = default; return false; }
        value = BinaryPrimitives.ReadInt64LittleEndian(payload); return true;
    }
    public bool TryGetRecord(long key, out Row? row)
    {
        if (!TryGetPayload(key, out var payload)) { row = null; return false; }
        row = DeserializeRow(payload); return true;
    }
    public void Insert(long key, long value) { byte[] payload = new byte[sizeof(long)]; BinaryPrimitives.WriteInt64LittleEndian(payload, value); InsertPayload(key, payload); }
    public void InsertRecord(long key, Row row) { ArgumentNullException.ThrowIfNull(row); InsertPayload(key, JsonSerializer.SerializeToUtf8Bytes(row)); }

    public bool UpdateRecord(long key, Row row)
    {
        ArgumentNullException.ThrowIfNull(row); byte[] payload = JsonSerializer.SerializeToUtf8Bytes(row);
        lock (_sync)
        {
            ThrowIfDisposed();
            Trace("UpdateStarted", $"key={key}, payloadBytes={payload.Length}");
            return ExecuteMutation(() =>
            {
                var path = new List<PathEntry>(); int leafPageId = FindLeaf(key, path); var leaf = ReadLeaf(leafPageId); int index = LowerBound(leaf.Entries.Select(x => x.Key).ToList(), key);
                if (index >= leaf.Entries.Count || leaf.Entries[index].Key != key)
                {
                    Trace("UpdateNotFound", $"key={key}, leafPage={leafPageId}");
                    return false;
                }
                FreeOverflowPages(leaf.Entries[index].OverflowHeadPageId); leaf.Entries[index] = new LeafEntry(key, payload); EnsureOverflowEntries(leaf);
                if (RequiresSplit(leaf)) SplitLeaf(leafPageId, leaf, path); else WriteLeaf(leafPageId, leaf); WriteMetadata();
                Trace("UpdateCompleted", $"key={key}, leafPage={leafPageId}, payloadBytes={payload.Length}");
                return true;
            });
        }
    }

    private void InsertPayload(long key, byte[] payload)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            Trace("InsertStarted", $"key={key}, payloadBytes={payload.Length}");
            ExecuteMutation(() =>
            {
                var path = new List<PathEntry>(); int leafPageId = FindLeaf(key, path); var leaf = ReadLeaf(leafPageId); int index = LowerBound(leaf.Entries.Select(x => x.Key).ToList(), key);
                if (index < leaf.Entries.Count && leaf.Entries[index].Key == key) throw new InvalidOperationException(Constants.DuplicatePrimaryKeyError);
                Trace("LeafInsertPosition", $"page={leafPageId}, key={key}, index={index}, existingKeys=[{string.Join(",", leaf.Entries.Select(entry => entry.Key))}]");
                leaf.Entries.Insert(index, new LeafEntry(key, payload)); EnsureOverflowEntries(leaf); if (RequiresSplit(leaf)) SplitLeaf(leafPageId, leaf, path); else WriteLeaf(leafPageId, leaf); _count++; WriteMetadata(); return true;
            });
            Trace("InsertCompleted", $"key={key}, count={_count}, rootPage={_rootPageId}");
        }
    }

    public bool Delete(long key)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            Trace("DeleteStarted", $"key={key}");
            return ExecuteMutation(() =>
            {
                var path = new List<PathEntry>(); int leafPageId = FindLeaf(key, path); var leaf = ReadLeaf(leafPageId); int index = LowerBound(leaf.Entries.Select(x => x.Key).ToList(), key);
                if (index >= leaf.Entries.Count || leaf.Entries[index].Key != key)
                {
                    Trace("DeleteNotFound", $"key={key}, leafPage={leafPageId}");
                    return false;
                }
                FreeOverflowPages(leaf.Entries[index].OverflowHeadPageId); leaf.Entries.RemoveAt(index);
                if (leafPageId == _rootPageId) WriteLeaf(leafPageId, leaf); else if (leaf.Entries.Count >= MinimumLeafKeys) { WriteLeaf(leafPageId, leaf); if (leaf.Entries.Count > 0) UpdateAncestorMinimum(path, leaf.Entries[0].Key); } else RebalanceLeaf(leafPageId, leaf, path);
                _count--; WriteMetadata(); Trace("DeleteCompleted", $"key={key}, count={_count}, rootPage={_rootPageId}"); return true;
            });
        }
    }

    public IReadOnlyList<KeyValuePair<long, long>> Scan(long? minimum = null, long? maximum = null)
    {
        var result = new List<KeyValuePair<long, long>>(); foreach (var entry in ScanPayloads(minimum, maximum)) { if (entry.Value.Length != sizeof(long)) throw new InvalidOperationException(Constants.InvalidFileError); result.Add(new(entry.Key, BinaryPrimitives.ReadInt64LittleEndian(entry.Value))); } return result;
    }
    public IReadOnlyList<KeyValuePair<long, Row>> ScanRecords(long? minimum = null, long? maximum = null)
    {
        var result = new List<KeyValuePair<long, Row>>(); foreach (var entry in ScanPayloads(minimum, maximum)) result.Add(new(entry.Key, DeserializeRow(entry.Value))); return result;
    }

    public void Validate()
    {
        lock (_sync)
        {
            ThrowIfDisposed(); var leaves = new List<int>(); var trees = new HashSet<int> { _metadataPageId }; var overflow = new HashSet<int>(); int? depth = null;
            ValidateNode(_rootPageId, 0, true, leaves, trees, overflow, ref depth); if (ScanPayloads(null, null).Count != _count) throw new InvalidDataException(Constants.InvalidFileError);
            int pageId = leaves.Count == 0 ? Constants.InvalidPageId : leaves[0]; foreach (int expected in leaves) { if (pageId != expected) throw new InvalidDataException(Constants.InvalidFileError); pageId = ReadLeaf(pageId).NextPageId; } if (PageIdIsValid(pageId)) throw new InvalidDataException(Constants.InvalidFileError); ValidateFreeList(trees, overflow);
        }
    }

    public void Dispose() { lock (_sync) { if (_disposed) return; if (_ownsBufferPool) _bufferPool.Dispose(); _disposed = true; } }

    private T ExecuteMutation<T>(Func<T> operation)
    {
        if (_activeMutation is not null) return operation();
        var mutation = new MutationContext(_rootPageId, _count, _freePageHead); _activeMutation = mutation;
        try { return operation(); }
        catch (Exception operationException)
        {
            try { RollbackMutation(mutation); } catch (Exception rollbackException) { throw new AggregateException(operationException, rollbackException); }
            throw;
        }
        finally { _activeMutation = null; }
    }

    private void RollbackMutation(MutationContext mutation)
    {
        foreach (var pair in mutation.OriginalPages) _bufferPool.WritePage(pair.Key, pair.Value);
        _rootPageId = mutation.RootPageId; _count = mutation.Count; _freePageHead = mutation.FreePageHead;
        foreach (int pageId in mutation.AllocatedPages) { byte[] page = new byte[_bufferPool.PageSize]; page[0] = FreePageType; WriteInt32(page, 1, _freePageHead); _bufferPool.WritePage(pageId, page); _freePageHead = pageId; }
        WriteMetadata();
    }

    private void WriteTreePage(int pageId, byte[] pageData)
    {
        if (_activeMutation is { } mutation && !mutation.AllocatedPages.Contains(pageId) && !mutation.OriginalPages.ContainsKey(pageId)) mutation.OriginalPages.Add(pageId, _bufferPool.ReadPage(pageId).ToArray());
        Trace("BufferPoolWrite", $"page={pageId}, bytes={pageData.Length}, pageType={pageData[0]}");
        _bufferPool.WritePage(pageId, pageData);
    }

    private void Initialize() { _rootPageId = AllocateTreePage(); _count = 0; WriteLeaf(_rootPageId, new LeafNode()); WriteMetadata(); }
    private void ReadMetadata()
    {
        if (_bufferPool.PageCount <= _metadataPageId) throw new InvalidDataException(Constants.InvalidFileError); byte[] page = _bufferPool.ReadPage(_metadataPageId);
        if (!page.AsSpan(0, Magic.Length).SequenceEqual(Magic) || ReadInt32(page, 4) != FormatVersion) throw new InvalidDataException(Constants.InvalidFileError);
        _rootPageId = ReadInt32(page, 8); _count = ReadInt64(page, 12); _maxKeys = ReadInt32(page, 20); _freePageHead = ReadInt32(page, 24);
        Trace("MetadataRead", $"page={_metadataPageId}, root={_rootPageId}, count={_count}, maxKeys={_maxKeys}, freeHead={_freePageHead}");
        if (_maxKeys < 3 || _rootPageId <= _metadataPageId || _rootPageId >= _bufferPool.PageCount || _count < 0 || (_freePageHead != Constants.InvalidPageId && !PageIdIsValid(_freePageHead))) throw new InvalidDataException(Constants.InvalidFileError);
    }
    private void WriteMetadata()
    {
        byte[] page = new byte[_bufferPool.PageSize]; Magic.CopyTo(page, 0); WriteInt32(page, 4, FormatVersion); WriteInt32(page, 8, _rootPageId); WriteInt64(page, 12, _count); WriteInt32(page, 20, _maxKeys); WriteInt32(page, 24, _freePageHead); WriteTreePage(_metadataPageId, page);
        Trace("MetadataWritten", $"page={_metadataPageId}, root={_rootPageId}, count={_count}, maxKeys={_maxKeys}, freeHead={_freePageHead}");
    }

    private void Trace(string operation, string detail, int depth = 0) =>
        _traceSink?.Write(new EngineTraceEvent("BPlusTree", operation, detail, depth));
    private static Row DeserializeRow(byte[] payload)
    {
        var row = JsonSerializer.Deserialize<Row>(payload) ?? throw new InvalidDataException(Constants.InvalidFileError);
        foreach (var pair in row.Values.ToList()) if (pair.Value is JsonElement json) row.Values[pair.Key] = json.ValueKind switch { JsonValueKind.String => json.GetString()!, JsonValueKind.Number when json.TryGetInt32(out int i) => i, JsonValueKind.Number when json.TryGetInt64(out long l) => l, JsonValueKind.Number => json.GetDecimal(), JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => null!, _ => json };
        return row;
    }
}
