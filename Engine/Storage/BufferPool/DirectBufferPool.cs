using Engine;

namespace Engine.BufferPool;

public sealed class DirectBufferPool : IBufferPool
{
    private readonly Pager _pager;
    private readonly Dictionary<int, DirectPage> _pinnedPages = new();
    private readonly object _lock = new();
    private bool _disposed;

    public DirectBufferPool(string filePath)
    {
        _pager = new Pager(filePath);
    }

    public int PageSize
    {
        get
        {
            ThrowIfDisposed();
            return _pager.PageSize;
        }
    }
    public int PageCount
    {
        get
        {
            ThrowIfDisposed();
            return _pager.ReadHeader().PageCount;
        }
    }
    public byte[] FetchPage(int pageId)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            if (!_pinnedPages.TryGetValue(pageId, out var page))
            {
                page = new DirectPage(_pager.ReadPage(pageId));
                _pinnedPages.Add(pageId, page);
            }

            page.PinCount++;
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
            if (!_pinnedPages.TryGetValue(pageId, out var page) || page.PinCount == 0)
                return false;

            page.IsDirty |= dirty;
            page.PinCount--;
            if (page.PinCount == 0)
            {
                if (page.IsDirty)
                    _pager.WritePage(pageId, page.Data);
                _pinnedPages.Remove(pageId);
            }
            return true;
        }
    }

    public byte[] ReadPage(int pageId)
    {
        byte[] page = FetchPage(pageId);
        byte[] copy = page.ToArray();
        UnpinPage(pageId, dirty: false);
        return copy;
    }

    public void WritePage(int pageId, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > PageSize)
            throw new ArgumentException(Constants.PageDataTooLargeError, nameof(data));

        byte[] page = FetchPage(pageId);
        data.CopyTo(page, 0);
        if (data.Length < page.Length)
            Array.Clear(page, data.Length, page.Length - data.Length);
        UnpinPage(pageId, dirty: true);
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
            _pager.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            if (_pinnedPages.Values.Any(page => page.PinCount > 0))
                throw new InvalidOperationException(Constants.BufferPoolNoUnpinnedPageError);
            _pager.Dispose();
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DirectBufferPool));
    }

    private sealed class DirectPage(byte[] data)
    {
        public byte[] Data { get; } = data;
        public int PinCount { get; set; }
        public bool IsDirty { get; set; }
    }
}