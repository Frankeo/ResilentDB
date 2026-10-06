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
        if (offset > page.Length) throw new InvalidOperationException(Constants.PageDataTooLargeError);
        WriteTreePage(pageId, page);
    }

    private LeafNode ReadLeaf(int pageId)
    {
        byte[] page = _bufferPool.ReadPage(pageId);
        if (page[0] != LeafPageType) throw new InvalidDataException(Constants.InvalidFileError);
        int count = ReadInt32(page, 1);
        if (count < 0 || count > _maxKeys) throw new InvalidDataException(Constants.InvalidFileError);
        var leaf = new LeafNode { NextPageId = ReadInt32(page, 5) };
        int offset = NodeHeaderSize;
        for (int i = 0; i < count; i++)
        {
            if (offset + LeafEntryHeaderSize > page.Length) throw new InvalidDataException(Constants.InvalidFileError);
            long key = ReadInt64(page, offset);
            byte kind = page[offset + sizeof(long)];
            int length = ReadInt32(page, offset + sizeof(long) + sizeof(byte));
            if (length <= 0) throw new InvalidDataException(Constants.InvalidFileError);
            if (kind == 0)
            {
                if (offset + LeafEntryHeaderSize + length > page.Length) throw new InvalidDataException(Constants.InvalidFileError);
                leaf.Entries.Add(new LeafEntry(key, page.AsSpan(offset + LeafEntryHeaderSize, length).ToArray()));
                offset += LeafEntryHeaderSize + length;
            }
            else if (kind == 1)
            {
                if (offset + LeafOverflowEntrySize > page.Length) throw new InvalidDataException(Constants.InvalidFileError);
                int head = ReadInt32(page, offset + LeafEntryHeaderSize);
                leaf.Entries.Add(new LeafEntry(key, ReadOverflowPayload(head, length), head));
                offset += LeafOverflowEntrySize;
            }
            else throw new InvalidDataException(Constants.InvalidFileError);
        }
        EnsureSorted(leaf.Entries.Select(x => x.Key).ToList());
        return leaf;
    }

    private void EnsureOverflowEntries(LeafNode leaf)
    {
        int limit = (_bufferPool.PageSize - NodeHeaderSize - LeafEntryHeaderSize) / 2;
        foreach (var entry in leaf.Entries)
            if (entry.OverflowHeadPageId <= 0 && entry.Payload.Length > limit)
                entry.OverflowHeadPageId = WriteOverflowPayload(entry.Payload);
    }

    private int WriteOverflowPayload(byte[] payload)
    {
        int chunk = _bufferPool.PageSize - NodeHeaderSize;
        int count = (payload.Length + chunk - 1) / chunk;
        var ids = new int[count];
        for (int i = 0; i < count; i++) ids[i] = AllocateTreePage();
        int offset = 0;
        for (int i = 0; i < count; i++)
        {
            int bytes = Math.Min(chunk, payload.Length - offset);
            byte[] page = new byte[_bufferPool.PageSize];
            page[0] = OverflowPageType;
            WriteInt32(page, 1, i + 1 < count ? ids[i + 1] : Constants.InvalidPageId);
            WriteInt32(page, 5, bytes);
            payload.AsSpan(offset, bytes).CopyTo(page.AsSpan(NodeHeaderSize));
            WriteTreePage(ids[i], page);
            offset += bytes;
        }
        return ids[0];
    }

    private byte[] ReadOverflowPayload(int firstPageId, int length)
    {
        if (firstPageId <= 0 || length <= 0) throw new InvalidDataException(Constants.InvalidFileError);
        byte[] payload = new byte[length];
        int copied = 0, pageId = firstPageId;
        var visited = new HashSet<int>();
        while (copied < length)
        {
            if (!PageIdIsValid(pageId) || !visited.Add(pageId)) throw new InvalidDataException(Constants.InvalidFileError);
            byte[] page = _bufferPool.ReadPage(pageId);
            int next = ReadInt32(page, 1);
            int bytes = ReadInt32(page, 5);
            if (page[0] != OverflowPageType || bytes <= 0 || bytes > page.Length - NodeHeaderSize || copied + bytes > length)
                throw new InvalidDataException(Constants.InvalidFileError);
            page.AsSpan(NodeHeaderSize, bytes).CopyTo(payload.AsSpan(copied));
            copied += bytes;
            pageId = next;
        }
        if (pageId != Constants.InvalidPageId) throw new InvalidDataException(Constants.InvalidFileError);
        return payload;
    }

    private void WriteInternal(int pageId, InternalNode node)
    {
        byte[] page = new byte[_bufferPool.PageSize];
        page[0] = InternalPageType;
        WriteInt32(page, 1, node.Keys.Count);
        WriteInt32(page, NodeHeaderSize, node.Children[0]);
        int offset = NodeHeaderSize + sizeof(int);
        for (int i = 0; i < node.Keys.Count; i++)
        {
            WriteInt64(page, offset, node.Keys[i]);
            WriteInt32(page, offset + sizeof(long), node.Children[i + 1]);
            offset += InternalEntrySize;
        }
        WriteTreePage(pageId, page);
    }

    private InternalNode ReadInternal(byte[] page)
    {
        if (page[0] != InternalPageType) throw new InvalidDataException(Constants.InvalidFileError);
        int count = ReadInt32(page, 1);
        if (count < 1 || count > _maxKeys || NodeHeaderSize + sizeof(int) + count * InternalEntrySize > page.Length)
            throw new InvalidDataException(Constants.InvalidFileError);
        var node = new InternalNode();
        node.Children.Add(ReadInt32(page, NodeHeaderSize));
        int offset = NodeHeaderSize + sizeof(int);
        for (int i = 0; i < count; i++)
        {
            node.Keys.Add(ReadInt64(page, offset));
            node.Children.Add(ReadInt32(page, offset + sizeof(long)));
            offset += InternalEntrySize;
        }
        EnsureSorted(node.Keys);
        return node;
    }
}
