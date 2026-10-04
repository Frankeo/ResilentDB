namespace Engine;

public sealed partial class PagedPrimaryKeyBPlusTree
{
    private int AllocateTreePage()
    {
        if (_freePageHead < 0)
        {
            int pageId = _bufferPool.AllocatePage();
            _activeMutation?.AllocatedPages.Add(pageId);
            return pageId;
        }

        int pageId = _freePageHead;
        byte[] page = _bufferPool.ReadPage(pageId);
        if (page[0] != FreePageType)
            throw new InvalidDataException(Constants.InvalidFileError);

        if (_activeMutation is { } mutation && !mutation.OriginalPages.ContainsKey(pageId))
            mutation.OriginalPages.Add(pageId, page.ToArray());

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
        var visitedPages = new HashSet<int>();
        while (pageId > 0)
        {
            if (!PageIdIsValid(pageId) || !visitedPages.Add(pageId))
                throw new InvalidDataException(Constants.InvalidFileError);

            byte[] page = _bufferPool.ReadPage(pageId);
            if (page[0] != OverflowPageType)
                throw new InvalidDataException(Constants.InvalidFileError);

            int nextPageId = ReadInt32(page, 1);
            FreeTreePage(pageId);
            pageId = nextPageId;
        }

        if (pageId != -1)
            throw new InvalidDataException(Constants.InvalidFileError);
    }

    private bool PageIdIsValid(int pageId) =>
        pageId >= Constants.FirstAllocatablePageId && pageId < _bufferPool.PageCount;

    private void ValidateOverflowPages(
        LeafEntry entry,
        HashSet<int> treePages,
        HashSet<int> overflowPages)
    {
        if (entry.OverflowHeadPageId == Constants.InvalidPageId)
            return;

        int pageId = entry.OverflowHeadPageId;
        int payloadLength = entry.Payload.Length;
        int copied = 0;
        var visited = new HashSet<int>();

        while (copied < payloadLength)
        {
            if (!PageIdIsValid(pageId) || !visited.Add(pageId) ||
                treePages.Contains(pageId) || !overflowPages.Add(pageId))
            {
                throw new InvalidDataException(Constants.InvalidFileError);
            }

            byte[] page = _bufferPool.ReadPage(pageId);
            int nextPageId = ReadInt32(page, 1);
            int payloadBytes = ReadInt32(page, 5);
            if (page[0] != OverflowPageType || payloadBytes <= 0 ||
                payloadBytes > page.Length - NodeHeaderSize || copied + payloadBytes > payloadLength)
            {
                throw new InvalidDataException(Constants.InvalidFileError);
            }

            copied += payloadBytes;
            pageId = nextPageId;
        }

        if (pageId != Constants.InvalidPageId)
            throw new InvalidDataException(Constants.InvalidFileError);
    }

    private void ValidateFreeList(HashSet<int> treePages, HashSet<int> overflowPages)
    {
        int pageId = _freePageHead;
        var freePages = new HashSet<int>();
        while (pageId != Constants.InvalidPageId)
        {
            if (!PageIdIsValid(pageId) || treePages.Contains(pageId) ||
                overflowPages.Contains(pageId) || !freePages.Add(pageId))
            {
                throw new InvalidDataException(Constants.InvalidFileError);
            }

            byte[] page = _bufferPool.ReadPage(pageId);
            if (page[0] != FreePageType)
                throw new InvalidDataException(Constants.InvalidFileError);
            pageId = ReadInt32(page, 1);
        }
    }
}
