namespace Engine.BufferPool;

public interface IBufferPool : IDisposable
{
    int PageSize { get; }
    int PageCount { get; }
    byte[] FetchPage(int pageId);
    bool UnpinPage(int pageId, bool dirty);
    byte[] ReadPage(int pageId);
    void WritePage(int pageId, byte[] data);
    int AllocatePage();
    void Flush();
}