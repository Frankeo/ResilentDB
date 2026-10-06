using System.Buffers.Binary;
using System.Text;
using Constants = Engine.BPlusTreeConfig.Constants;

namespace Engine;

public sealed partial class BPlusTree
{
    private const int DefaultMetadataPageId = Constants.DefaultMetadataPageId;
    private const int NodeHeaderSize = 9;
    private const int LeafEntryHeaderSize = sizeof(long) + sizeof(byte) + sizeof(int);
    private const int LeafOverflowEntrySize = LeafEntryHeaderSize + sizeof(int);
    private const int InternalEntrySize = sizeof(long) + sizeof(int);
    private const byte LeafPageType = 1;
    private const byte InternalPageType = 2;
    private const byte OverflowPageType = 3;
    private const byte FreePageType = 4;
    private const int FormatVersion = 4;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("RDBI");

    private int MinimumLeafKeys => (_maxKeys + 1) / 2;
    private int MinimumInternalKeys => _maxKeys / 2;

    private void ValidatePageCapacity()
    {
        if (NodeHeaderSize + LeafOverflowEntrySize * _maxKeys > _bufferPool.PageSize ||
            NodeHeaderSize + sizeof(int) + InternalEntrySize * _maxKeys > _bufferPool.PageSize)
            throw new ArgumentOutOfRangeException(nameof(_maxKeys));
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));

    private static long ReadInt64(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, sizeof(long)));

    private static void WriteInt32(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)), value);

    private static void WriteInt64(byte[] bytes, int offset, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset, sizeof(long)), value);

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BPlusTree));
    }
}
