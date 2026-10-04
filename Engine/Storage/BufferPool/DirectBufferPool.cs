using Engine;

namespace Engine.BufferPool;

public sealed class DirectBufferPool : IBufferPool
{
    private readonly Pager _pager;

    public DirectBufferPool(string filePath)
    {
        _pager = new Pager(filePath);
    }

    public int PageSize => _pager.PageSize;
    public int PageCount => _pager.ReadHeader().PageCount;
    public byte[] FetchPage(int pageId) => _pager.ReadPage(pageId);
    public bool UnpinPage(int pageId, bool dirty) => true;
    public byte[] ReadPage(int pageId) => _pager.ReadPage(pageId);
    public void WritePage(int pageId, byte[] data) => _pager.WritePage(pageId, data);
    public int AllocatePage() => _pager.AllocatePage();
    public void Flush() { }
    public void Dispose() => _pager.Dispose();
}