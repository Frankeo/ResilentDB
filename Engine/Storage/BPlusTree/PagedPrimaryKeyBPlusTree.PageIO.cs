namespace Engine;

public sealed partial class PagedPrimaryKeyBPlusTree
{
    private void WriteLeaf(int pageId, LeafNode node)
    {
        EnsureOverflowEntries(node);
        byte[] page = new byte[_bufferPool.PageSize];
        page[0] = LeafPageType;
        WriteInt32(page, 1, node.Entries.Count);
        WriteInt32(page, 5, node.NextPageId);
        int offset = NodeHeaderSize;

        foreach (var entry in node.Entries)
        {
            WriteInt64(page, offset, entry.Key);
            WriteInt32(page, offset + sizeof(long) + sizeof(byte), entry.Payload.Length);
            if (entry.OverflowHeadPageId > 0)
            {
                page[offset + sizeof(long)] = 1;
                WriteInt32(page, offset + LeafEntryHeaderSize, entry.OverflowHeadPageId);
                offset += LeafOverflowEntrySize;
            }
            else
            {
                page[offset + sizeof(long)] = 0;
                entry.Payload.CopyTo(page, offset + LeafEntryHeaderSize);
                offset += LeafEntryHeaderSize + entry.Payload.Length;
            }
        }

        if (offset > page.Length)
            throw new InvalidOperationException(Constants.PageDataTooLargeError);

        WriteTreePage(pageId, page);
    }

    private LeafNode ReadLeaf(int pageId)
    {
        byte[] page = _bufferPool.ReadPage(pageId);
        if (page[0] != LeafPageType)
            throw new InvalidDataException(Constants.InvalidFileError);

        int count = ReadInt32(page, 1);
        if (count < 0 || count > _maxKeys)
            throw new InvalidDataException(Constants.InvalidFileError);

        var leaf = new LeafNode { NextPageId = ReadInt32(page, 5) };
        int offset = NodeHeaderSize;
        for (int index = 0; index < count; index++)
        {
            if (offset + LeafEntryHeaderSize > page.Length)
                throw new InvalidDataException(Constants.InvalidFileError);

            long key = ReadInt64(page, offset);
            byte storageKind = page[offset + sizeof(long)];
            int payloadLength = ReadInt32(page, offset + sizeof(long) + sizeof(byte));
            if (payloadLength <= 0)
                throw new InvalidDataException(Constants.InvalidFileError);

            if (storageKind == 0)
            {
                if (offset + LeafEntryHeaderSize + payloadLength > page.Length)
                    throw new InvalidDataException(Constants.InvalidFileError);

                byte[] payload = page.AsSpan(offset + LeafEntryHeaderSize, payloadLength).ToArray();
                leaf.Entries.Add(new LeafEntry(key, payload));
                offset += LeafEntryHeaderSize + payloadLength;
            }
            else if (storageKind == 1)
            {
                if (offset + LeafOverflowEntrySize > page.Length)
                    throw new InvalidDataException(Constants.InvalidFileError);

                int overflowHeadPageId = ReadInt32(page, offset + LeafEntryHeaderSize);
                byte[] payload = ReadOverflowPayload(overflowHeadPageId, payloadLength);
                leaf.Entries.Add(new LeafEntry(key, payload, overflowHeadPageId));
                offset += LeafOverflowEntrySize;
            }
            else
            {
                throw new InvalidDataException(Constants.InvalidFileError);
            }
        }

        EnsureSorted(leaf.Entries.Select(entry => entry.Key).ToList());
        return leaf;
    }

    private void EnsureOverflowEntries(LeafNode leaf)
    {
        int inlinePayloadLimit = (_bufferPool.PageSize - NodeHeaderSize - LeafEntryHeaderSize) / 2;
        foreach (var entry in leaf.Entries)
        {
            if (entry.OverflowHeadPageId <= 0 && entry.Payload.Length > inlinePayloadLimit)
                entry.OverflowHeadPageId = WriteOverflowPayload(entry.Payload);
        }
    }

    private int WriteOverflowPayload(byte[] payload)
    {
        int chunkSize = _bufferPool.PageSize - NodeHeaderSize;
        int pageCount = (payload.Length + chunkSize - 1) / chunkSize;
        var pageIds = new int[pageCount];

        for (int index = 0; index < pageCount; index++)
            pageIds[index] = AllocateTreePage();

        int payloadOffset = 0;
        for (int index = 0; index < pageCount; index++)
        {
            int bytesInPage = Math.Min(chunkSize, payload.Length - payloadOffset);
            byte[] page = new byte[_bufferPool.PageSize];
            page[0] = OverflowPageType;
            WriteInt32(page, 1, index + 1 < pageCount ? pageIds[index + 1] : -1);
            WriteInt32(page, 5, bytesInPage);
            payload.AsSpan(payloadOffset, bytesInPage).CopyTo(page.AsSpan(NodeHeaderSize));
            WriteTreePage(pageIds[index], page);
            payloadOffset += bytesInPage;
        }

        return pageIds[0];
    }

    private byte[] ReadOverflowPayload(int firstPageId, int payloadLength)
    {
        if (firstPageId <= 0 || payloadLength <= 0)
            throw new InvalidDataException(Constants.InvalidFileError);

        byte[] payload = new byte[payloadLength];
        int copied = 0;
        int pageId = firstPageId;
        var visitedPages = new HashSet<int>();
        while (copied < payloadLength)
        {
            if (!PageIdIsValid(pageId) || !visitedPages.Add(pageId))
                throw new InvalidDataException(Constants.InvalidFileError);

            byte[] page = _bufferPool.ReadPage(pageId);
            int nextPageId = ReadInt32(page, 1);
            int bytesInPage = ReadInt32(page, 5);
            if (page[0] != OverflowPageType || bytesInPage <= 0 ||
                bytesInPage > page.Length - NodeHeaderSize || copied + bytesInPage > payloadLength)
            {
                throw new InvalidDataException(Constants.InvalidFileError);
            }

            page.AsSpan(NodeHeaderSize, bytesInPage).CopyTo(payload.AsSpan(copied));
            copied += bytesInPage;
            pageId = nextPageId;
        }

        if (pageId != -1)
            throw new InvalidDataException(Constants.InvalidFileError);

        return payload;
    }

    private void WriteInternal(int pageId, InternalNode node)
    {
        byte[] page = new byte[_bufferPool.PageSize];
        page[0] = InternalPageType;
        WriteInt32(page, 1, node.Keys.Count);
        WriteInt32(page, NodeHeaderSize, node.Children[0]);
        int offset = NodeHeaderSize + sizeof(int);

        for (int index = 0; index < node.Keys.Count; index++)
        {
            WriteInt64(page, offset, node.Keys[index]);
            WriteInt32(page, offset + sizeof(long), node.Children[index + 1]);
            offset += InternalEntrySize;
        }

        WriteTreePage(pageId, page);
    }

    private InternalNode ReadInternal(byte[] page)
    {
        if (page[0] != InternalPageType)
            throw new InvalidDataException(Constants.InvalidFileError);

        int count = ReadInt32(page, 1);
        if (count < 1 || count > _maxKeys ||
            NodeHeaderSize + sizeof(int) + count * InternalEntrySize > page.Length)
        {
            throw new InvalidDataException(Constants.InvalidFileError);
        }

        var node = new InternalNode();
        node.Children.Add(ReadInt32(page, NodeHeaderSize));
        int offset = NodeHeaderSize + sizeof(int);
        for (int index = 0; index < count; index++)
        {
            node.Keys.Add(ReadInt64(page, offset));
            node.Children.Add(ReadInt32(page, offset + sizeof(long)));
            offset += InternalEntrySize;
        }

        EnsureSorted(node.Keys);
        return node;
    }
}
