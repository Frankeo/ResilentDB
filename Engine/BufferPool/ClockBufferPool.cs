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

    public ClockBufferPool(string filePath, int capacity = Constants.DefaultBufferPoolCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _pager = new Pager(filePath);
        _capacity = capacity;
        _frames = new List<ClockPage>(capacity);
        _map = new Dictionary<int, int>(capacity);
    }

    public int PageSize => _pager.PageSize;

    public int PageCount
    {
        get
        {
            lock (_lock)
                return _pager.ReadHeader().PageCount;
        }
    }

    public int CachedPageCount
    {
        get
        {
            lock (_lock)
                return _frames.Count;
        }
    }

    public int Capacity => _capacity;

    public byte[] ReadPage(int pageId)
    {
        lock (_lock)
            return GetPage(pageId).Data.ToArray();
    }

    public void WritePage(int pageId, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > PageSize)
            throw new ArgumentException(Constants.PageDataTooLargeError, nameof(data));

        lock (_lock)
        {
            var page = GetPage(pageId);
            data.CopyTo(page.Data, 0);
            if (data.Length < page.Data.Length)
                Array.Clear(page.Data, data.Length, page.Data.Length - data.Length);
            page.IsDirty = true;
            page.Referenced = true;
        }
    }

    public int AllocatePage()
    {
        lock (_lock)
            return _pager.AllocatePage();
    }

    public void Flush()
    {
        lock (_lock)
            FlushAll();
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        lock (_lock)
        {
            try
            {
                FlushAll();
            }
            finally
            {
                _pager.Dispose();
            }
        }
    }

    private ClockPage GetPage(int pageId)
    {
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
        while (true)
        {
            var page = _frames[_clockHand];
            if (!page.Referenced)
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

            page.Referenced = false;
            _clockHand = (_clockHand + 1) % _capacity;
        }
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

    private sealed class ClockPage(int pageId, byte[] data)
    {
        public int PageId { get; } = pageId;
        public byte[] Data { get; } = data;
        public bool IsDirty { get; set; }
        public bool Referenced { get; set; } = true;
    }
}