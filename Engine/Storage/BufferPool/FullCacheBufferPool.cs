using Engine;

namespace Engine.BufferPool;

public sealed class FullCacheBufferPool : IBufferPool
{
    private readonly Pager _pager;
    private readonly Dictionary<int, CachedPage> _pages = new();
    private bool _disposed;

    public FullCacheBufferPool(string filePath)
    {
        _pager = new Pager(filePath);
        for (var pageId = 1; pageId < PageCount; pageId++)
            _pages.Add(pageId, new CachedPage(_pager.ReadPage(pageId)));
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
    public int CachedPageCount
    {
        get
        {
            ThrowIfDisposed();
            return _pages.Count;
        }
    }

    public byte[] FetchPage(int pageId)
    {
        var page = GetPage(pageId);
        page.PinCount++;
        return page.Data;
    }

    public PageHandle FetchPageHandle(int pageId) =>
        new(this, pageId, FetchPage(pageId));

    public bool UnpinPage(int pageId, bool dirty)
    {
        ThrowIfDisposed();
        if (! _pages.TryGetValue(pageId, out var page) || page.PinCount == 0)
            return false;

        page.PinCount--;
        page.IsDirty |= dirty;
        return true;
    }

    public byte[] ReadPage(int pageId)
    {
        byte[] data = FetchPage(pageId).ToArray();
        UnpinPage(pageId, dirty: false);
        return data;
    }

    public void WritePage(int pageId, byte[] data)
    {
        ValidateWrite(pageId, data);
        var page = GetPage(pageId);
        page.PinCount++;
        Array.Clear(page.Data);
        data.CopyTo(page.Data, 0);
        UnpinPage(pageId, dirty: true);
    }

    public int AllocatePage()
    {
        ThrowIfDisposed();
        var pageId = _pager.AllocatePage();
        _pages.Add(pageId, new CachedPage(new byte[PageSize]) { IsDirty = true });
        return pageId;
    }

    public void Flush()
    {
        ThrowIfDisposed();
        foreach (var (pageId, page) in _pages)
        {
            if (!page.IsDirty)
                continue;

            _pager.WritePage(pageId, page.Data);
            page.IsDirty = false;
        }
        _pager.Flush();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (_pages.Values.Any(page => page.PinCount > 0))
            throw new InvalidOperationException(Constants.BufferPoolNoUnpinnedPageError);
        try
        {
            Flush();
        }
        finally
        {
            _pager.Dispose();
            _disposed = true;
        }
    }

    private CachedPage GetPage(int pageId)
    {
        ThrowIfDisposed();
        ValidatePageId(pageId);
        return _pages[pageId];
    }

    private void ValidatePageId(int pageId)
    {
        ThrowIfDisposed();
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

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FullCacheBufferPool));
    }

    private sealed class CachedPage(byte[] data)
    {
        public byte[] Data { get; } = data;
        public bool IsDirty { get; set; }
        public int PinCount { get; set; }
    }
}