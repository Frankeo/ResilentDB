using Engine;

namespace Engine.BufferPool;

/// <summary>
/// Thread-safe Clock page cache. ReadPage returns a copy; updates must use WritePage.
/// </summary>
public sealed class ClockBufferPool : IBufferPool
{
    private readonly int _capacity;
    private readonly Pager _pager;
    private readonly List<ClockPage> _frames;
    private readonly Dictionary<int, int> _map;
    private readonly object _lock = new();
    private int _clockHand;
    private bool _disposed;

    public ClockBufferPool(string filePath, int capacity = Constants.DefaultBufferPoolCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _pager = new Pager(filePath);
        _capacity = capacity;
        _frames = new List<ClockPage>(capacity);
        _map = new Dictionary<int, int>(capacity);
    }

    public int PageSize
    {
        get
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                return _pager.PageSize;
            }
        }
    }

    public int PageCount
    {
        get
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                return _pager.ReadHeader().PageCount;
            }
        }
    }

    public int CachedPageCount
    {
        get
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                return _frames.Count;
            }
        }
    }

    public int Capacity
    {
        get
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                return _capacity;
            }
        }
    }

    public byte[] ReadPage(int pageId)
    {
        byte[] data = FetchPage(pageId).ToArray();
        UnpinPage(pageId, dirty: false);
        return data;
    }

    public byte[] FetchPage(int pageId)
    {
        lock (_lock)
        {
            var page = GetPage(pageId);
            page.PinCount++;
            page.Referenced = true;
            return page.Data;
        }
    }

    public PageHandle FetchPageHandle(int pageId) =>
        new(this, pageId, FetchPage(pageId));

    public bool UnpinPage(int pageId, bool dirty)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            if (!_map.TryGetValue(pageId, out int index))
                return false;

            var page = _frames[index];
            if (page.PinCount <= 0)
                return false;

            page.PinCount--;
            page.IsDirty |= dirty;
            return true;
        }
    }

    public void WritePage(int pageId, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > PageSize)
            throw new ArgumentException(Constants.PageDataTooLargeError, nameof(data));

        lock (_lock)
        {
            ThrowIfDisposed();
            var page = GetPage(pageId);
            page.PinCount++;
            data.CopyTo(page.Data, 0);
            if (data.Length < page.Data.Length)
                Array.Clear(page.Data, data.Length, page.Data.Length - data.Length);
            page.IsDirty = true;
            page.Referenced = true;
            page.PinCount--;
        }
    }

    public int AllocatePage()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            return _pager.AllocatePage();
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            FlushAll();
        }
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            if (_frames.Any(page => page.PinCount > 0))
                throw new InvalidOperationException(Constants.BufferPoolNoUnpinnedPageError);

            try
            {
                FlushAll();
            }
            finally
            {
                _pager.Dispose();
                _disposed = true;
            }
        }
    }

    private ClockPage GetPage(int pageId)
    {
        ThrowIfDisposed();
        if (_map.TryGetValue(pageId, out var index))
        {
            var cachedPage = _frames[index];
            cachedPage.Referenced = true;
            return cachedPage;
        }

        var data = _pager.ReadPage(pageId);
        var page = new ClockPage(pageId, data);

        if (_frames.Count < _capacity)
        {
            var freeFrameIndex = _frames.Count;
            _frames.Add(page);
            _map.Add(pageId, freeFrameIndex);
            return page;
        }

        var victimIndex = SelectVictim();
        _frames[victimIndex] = page;
        _map.Add(pageId, victimIndex);
        return page;
    }

    private int SelectVictim()
    {
        int attempts = 0;
        while (attempts < _capacity * 2)
        {
            var page = _frames[_clockHand];
            if (page.PinCount == 0 && !page.Referenced)
            {
                if (page.IsDirty)
                {
                    _pager.WritePage(page.PageId, page.Data);
                    page.IsDirty = false;
                }

                _map.Remove(page.PageId);
                var victimIndex = _clockHand;
                _clockHand = (_clockHand + 1) % _capacity;
                return victimIndex;
            }

            if (page.PinCount == 0)
                page.Referenced = false;
            _clockHand = (_clockHand + 1) % _capacity;
            attempts++;
        }

        throw new InvalidOperationException(Constants.BufferPoolNoUnpinnedPageError);
    }

    private void FlushAll()
    {
        foreach (var page in _frames)
        {
            if (!page.IsDirty)
                continue;

            _pager.WritePage(page.PageId, page.Data);
            page.IsDirty = false;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ClockBufferPool));
    }

    private sealed class ClockPage(int pageId, byte[] data)
    {
        public int PageId { get; } = pageId;
        public byte[] Data { get; } = data;
        public bool IsDirty { get; set; }
        public int PinCount { get; set; }
        public bool Referenced { get; set; } = true;
    }
}