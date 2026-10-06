using Constants = Engine.BPlusTree.Constants;

namespace Engine;

public sealed partial class PagedPrimaryKeyBPlusTree
{
    private sealed class LeafNode
    {
        public List<LeafEntry> Entries { get; } = new();
        public int NextPageId { get; set; } = Constants.InvalidPageId;
    }

    private sealed class LeafEntry(long key, byte[] payload, int overflowHeadPageId = Constants.InvalidPageId)
    {
        public long Key { get; } = key;
        public byte[] Payload { get; } = payload;
        public int OverflowHeadPageId { get; set; } = overflowHeadPageId;
    }

    private sealed class InternalNode
    {
        public List<long> Keys { get; } = new();
        public List<int> Children { get; } = new();
    }

    private sealed record PathEntry(int PageId, int ChildIndex);

    private sealed class MutationContext(int rootPageId, long count, int freePageHead)
    {
        public int RootPageId { get; } = rootPageId;
        public long Count { get; } = count;
        public int FreePageHead { get; } = freePageHead;
        public Dictionary<int, byte[]> OriginalPages { get; } = new();
        public HashSet<int> AllocatedPages { get; } = new();
    }
}
