using Constants = Engine.BPlusTree.Constants;

namespace Engine;

public sealed partial class PagedPrimaryKeyBPlusTree
{
    private int AllocateTreePage()
    {
        if (_freePageHead == Constants.InvalidPageId)
        {
            int id = _bufferPool.AllocatePage();
            _activeMutation?.AllocatedPages.Add(id);
            return id;
        }
        int pageId = _freePageHead;
        byte[] page = _bufferPool.ReadPage(pageId);
        if (page[0] != FreePageType) throw new InvalidDataException(Constants.InvalidFileError);
        _activeMutation?.OriginalPages.TryAdd(pageId, page.ToArray());
        _freePageHead = ReadInt32(page, 1);
        return pageId;
    }

    private void FreeTreePage(int pageId)
    {
        if (pageId <= _metadataPageId || pageId >= _bufferPool.PageCount)
            throw new InvalidDataException(Constants.InvalidFileError);
        byte[] page = new byte[_bufferPool.PageSize];
        page[0] = FreePageType;
        WriteInt32(page, 1, _freePageHead);
        WriteTreePage(pageId, page);
        _freePageHead = pageId;
    }

    private void FreeOverflowPages(int firstPageId)
    {
        int pageId = firstPageId;
        var visited = new HashSet<int>();
        while (pageId > 0)
        {
            if (!PageIdIsValid(pageId) || !visited.Add(pageId)) throw new InvalidDataException(Constants.InvalidFileError);
            byte[] page = _bufferPool.ReadPage(pageId);
            if (page[0] != OverflowPageType) throw new InvalidDataException(Constants.InvalidFileError);
            int next = ReadInt32(page, 1);
            FreeTreePage(pageId);
            pageId = next;
        }
        if (pageId != Constants.InvalidPageId) throw new InvalidDataException(Constants.InvalidFileError);
    }

    private bool PageIdIsValid(int pageId) =>
        pageId >= Constants.FirstAllocatablePageId && pageId < _bufferPool.PageCount;

    private void ValidateFreeList(HashSet<int> treePages, HashSet<int> overflowPages)
    {
        int pageId = _freePageHead;
        var free = new HashSet<int>();
        while (pageId != Constants.InvalidPageId)
        {
            if (!PageIdIsValid(pageId) || treePages.Contains(pageId) || overflowPages.Contains(pageId) || !free.Add(pageId))
                throw new InvalidDataException(Constants.InvalidFileError);
            byte[] page = _bufferPool.ReadPage(pageId);
            if (page[0] != FreePageType) throw new InvalidDataException(Constants.InvalidFileError);
            pageId = ReadInt32(page, 1);
        }
    }
}
