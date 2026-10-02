using Engine;

namespace Engine.BufferPool;

public sealed class LruBufferPool : IBufferPool
{
    private readonly Pager _pager;
    private readonly int _capacity;
    private readonly Dictionary<int, CacheEntry> _pages = new();
    private readonly LinkedList<int> _leastRecentlyUsed = new();

    public LruBufferPool(string filePath, int capacity = 100)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _pager = new Pager(filePath);
        _capacity = capacity;
    }

    public int PageSize => _pager.PageSize;
    public int PageCount => _pager.ReadHeader().PageCount;
    public int CachedPageCount => _pages.Count;
    public int Capacity => _capacity;

    public byte[] ReadPage(int pageId) => GetPage(pageId).Data.ToArray();

    public void WritePage(int pageId, byte[] data)
    {
        ValidateWrite(pageId, data);
        var page = GetPage(pageId);
        Array.Clear(page.Data);
        data.CopyTo(page.Data, 0);
        page.IsDirty = true;
    }

    public int AllocatePage() => _pager.AllocatePage();

    public void Flush()
    {
        foreach (var (pageId, page) in _pages)
        {
            if (!page.IsDirty)
                continue;

            _pager.WritePage(pageId, page.Data);
            page.IsDirty = false;
        }
    }

    public void Dispose()
    {
        Flush();
        _pager.Dispose();
    }

    private CacheEntry GetPage(int pageId)
    {
        ValidatePageId(pageId);
        if (_pages.TryGetValue(pageId, out var cached))
        {
            MarkMostRecentlyUsed(cached);
            return cached;
        }

        if (_pages.Count == _capacity)
            EvictLeastRecentlyUsed();

        var entry = new CacheEntry(pageId, _pager.ReadPage(pageId), _leastRecentlyUsed.AddFirst(pageId));
        _pages.Add(pageId, entry);
        return entry;
    }

    private void EvictLeastRecentlyUsed()
    {
        var node = _leastRecentlyUsed.Last!;
        var page = _pages[node.Value];
        if (page.IsDirty)
        {
            _pager.WritePage(page.PageId, page.Data);
            page.IsDirty = false;
        }

        _pages.Remove(page.PageId);
        _leastRecentlyUsed.RemoveLast();
    }

    private void MarkMostRecentlyUsed(CacheEntry page)
    {
        _leastRecentlyUsed.Remove(page.Node);
        _leastRecentlyUsed.AddFirst(page.Node);
    }

    private void ValidatePageId(int pageId)
    {
        if (pageId <= 0 || pageId >= PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageId));
    }

    private void ValidateWrite(int pageId, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidatePageId(pageId);
        if (data.Length > PageSize)
            throw new ArgumentException(Constants.PageDataTooLargeError, nameof(data));
    }

    private sealed class CacheEntry(int pageId, byte[] data, LinkedListNode<int> node)
    {
        public int PageId { get; } = pageId;
        public byte[] Data { get; } = data;
        public LinkedListNode<int> Node { get; } = node;
        public bool IsDirty { get; set; }
    }
}