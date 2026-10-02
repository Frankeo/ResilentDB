namespace Engine.BufferPool;

public interface IBufferPool : IDisposable
{
    int PageSize { get; }
    int PageCount { get; }
    byte[] ReadPage(int pageId);
    void WritePage(int pageId, byte[] data);
    int AllocatePage();
    void Flush();
}