using Engine;

namespace Engine.BufferPool;

public sealed class FullCacheBufferPool : IBufferPool
{
    private readonly Pager _pager;
    private readonly Dictionary<int, CachedPage> _pages = new();

    public FullCacheBufferPool(string filePath)
    {
        _pager = new Pager(filePath);
        for (var pageId = 1; pageId < PageCount; pageId++)
            _pages.Add(pageId, new CachedPage(_pager.ReadPage(pageId)));
    }

    public int PageSize => _pager.PageSize;
    public int PageCount => _pager.ReadHeader().PageCount;
    public int CachedPageCount => _pages.Count;

    public byte[] ReadPage(int pageId) => GetPage(pageId).Data.ToArray();

    public void WritePage(int pageId, byte[] data)
    {
        ValidateWrite(pageId, data);
        var page = GetPage(pageId);
        Array.Clear(page.Data);
        data.CopyTo(page.Data, 0);
        page.IsDirty = true;
    }

    public int AllocatePage()
    {
        var pageId = _pager.AllocatePage();
        _pages.Add(pageId, new CachedPage(new byte[PageSize]) { IsDirty = true });
        return pageId;
    }

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

    private CachedPage GetPage(int pageId)
    {
        ValidatePageId(pageId);
        return _pages[pageId];
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

    private sealed class CachedPage(byte[] data)
    {
        public byte[] Data { get; } = data;
        public bool IsDirty { get; set; }
    }
}