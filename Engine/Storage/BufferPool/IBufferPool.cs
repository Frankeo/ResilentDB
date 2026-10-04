namespace Engine.BufferPool;

public interface IBufferPool : IDisposable
{
    int PageSize { get; }
    int PageCount { get; }

    /// <summary>Returns the mutable cached frame. Dispose the handle to unpin it.</summary>
    PageHandle FetchPageHandle(int pageId) =>
        new(this, pageId, FetchPage(pageId));

    /// <summary>Returns the mutable cached frame. The caller must unpin it.</summary>
    byte[] FetchPage(int pageId);

    /// <summary>Releases a page previously fetched by this buffer pool.</summary>
    bool UnpinPage(int pageId, bool dirty);
    byte[] ReadPage(int pageId);
    void WritePage(int pageId, byte[] data);
    int AllocatePage();
    void Flush();
}