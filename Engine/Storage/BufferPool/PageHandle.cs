namespace Engine.BufferPool;

public sealed class PageHandle : IDisposable
{
    private IBufferPool? _bufferPool;
    private bool _isDirty;

    internal PageHandle(IBufferPool bufferPool, int pageId, byte[] data)
    {
        _bufferPool = bufferPool;
        PageId = pageId;
        Data = data;
    }

    public int PageId { get; }
    public byte[] Data { get; }

    public void MarkDirty()
    {
        ObjectDisposedException.ThrowIf(_bufferPool is null, this);
        _isDirty = true;
    }

    public void Dispose()
    {
        var bufferPool = Interlocked.Exchange(ref _bufferPool, null);
        if (bufferPool is not null && !bufferPool.UnpinPage(PageId, _isDirty))
            throw new InvalidOperationException(Constants.InvalidFileError);
    }
}