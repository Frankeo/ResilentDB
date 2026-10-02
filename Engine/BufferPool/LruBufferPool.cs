using Engine;

namespace Engine.BufferPool;

public sealed class LruBufferPool : IBufferPool
{
    private readonly Pager _pager;
    private readonly int _capacity;
    private readonly Dictionary<int, CacheEntry> _pages = new();
    private readonly LinkedList<int> _leastRecentlyUsed = new();
    private readonly object _lock = new();
    private int _pageCount;

    public LruBufferPool(string filePath, int capacity = 100)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _pager = new Pager(filePath);
        _capacity = capacity;
        _pageCount = _pager.ReadHeader().PageCount;
    }

    public int PageSize => _pager.PageSize;
    public int PageCount
    {
        get
        {
            lock (_lock)
                return _pageCount;
        }
    }

    public int CachedPageCount
    {
        get
        {
            lock (_lock)
                return _pages.Count;
        }
    }

    public int Capacity => _capacity;

    public byte[] ReadPage(int pageId)
    {
        lock (_lock)
            return GetPageLocked(pageId).Data.ToArray();
    }

    public void WritePage(int pageId, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > PageSize)
            throw new ArgumentException(Constants.PageDataTooLargeError, nameof(data));

        lock (_lock)
        {
            var page = GetPageLocked(pageId);
            data.CopyTo(page.Data, 0);
            if (data.Length < page.Data.Length)
                Array.Clear(page.Data, data.Length, page.Data.Length - data.Length);
            page.IsDirty = true;
        }
    }

    public int AllocatePage()
    {
        lock (_lock)
        {
            var pageId = _pager.AllocatePage();
            _pageCount = pageId + 1;
            return pageId;
        }
    }

    public void Flush()
    {
        lock (_lock)
            FlushLocked();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try
            {
                FlushLocked();
            }
            finally
            {
                _pager.Dispose();
            }
        }
    }

    private void FlushLocked()
    {
        foreach (var (pageId, page) in _pages)
        {
            if (!page.IsDirty)
                continue;

            _pager.WritePage(pageId, page.Data);
            page.IsDirty = false;
        }
    }

    private CacheEntry GetPageLocked(int pageId)
    {
        ValidatePageIdLocked(pageId);
        if (_pages.TryGetValue(pageId, out var cached))
        {
            MarkMostRecentlyUsedLocked(cached);
            return cached;
        }

        if (_pages.Count == _capacity)
            EvictLeastRecentlyUsedLocked();

        var entry = new CacheEntry(pageId, _pager.ReadPage(pageId), _leastRecentlyUsed.AddFirst(pageId));
        _pages.Add(pageId, entry);
        return entry;
    }

    private void EvictLeastRecentlyUsedLocked()
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

    private void MarkMostRecentlyUsedLocked(CacheEntry page)
    {
        _leastRecentlyUsed.Remove(page.Node);
        _leastRecentlyUsed.AddFirst(page.Node);
    }

    private void ValidatePageIdLocked(int pageId)
    {
        if (pageId <= 0 || pageId >= _pageCount)
            throw new ArgumentOutOfRangeException(nameof(pageId));
    }

    private sealed class CacheEntry(int pageId, byte[] data, LinkedListNode<int> node)
    {
        public int PageId { get; } = pageId;
        public byte[] Data { get; } = data;
        public LinkedListNode<int> Node { get; } = node;
        public bool IsDirty { get; set; }
    }
}