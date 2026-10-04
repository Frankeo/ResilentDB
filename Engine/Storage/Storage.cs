using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Engine.BufferPool;

namespace Engine
{

    public class Storage : IDisposable
    {
        private static readonly byte[] CatalogMagic = Encoding.ASCII.GetBytes("RDC4");
        private readonly string _filePath;
        private Dictionary<string, int> _primaryIndexMetadataPages = new();
        private readonly Dictionary<string, PagedPrimaryKeyBPlusTree> _primaryIndexes =
            new(StringComparer.OrdinalIgnoreCase);
        private ClockBufferPool? _bufferPool;
        private bool _disposed;

        private ClockBufferPool BufferPool
        {
            get
            {
                ThrowIfDisposed();
                return _bufferPool ??= new ClockBufferPool(_filePath);
            }
        }

        public Storage(string filePath)
        {
            _filePath = filePath;
        }

        public bool Exists
        {
            get
            {
                ThrowIfDisposed();
                return File.Exists(_filePath);
            }
        }

        internal string FilePath => _filePath;
        internal IReadOnlyDictionary<string, int> PrimaryIndexMetadataPages => _primaryIndexMetadataPages;

        public void CreateFile()
        {
            if (BufferPool.AllocatePage() != Constants.CatalogPageId)
                throw new InvalidDataException(Constants.InvalidFileError);

            WriteCatalog(new Schema());
            BufferPool.Flush();
        }

        public Schema Load()
        {
            if (BufferPool.PageCount < 2)
                throw new InvalidDataException(Constants.InvalidFileError);

            byte[] page = BufferPool.ReadPage(Constants.CatalogPageId);
            if (!page.AsSpan(0, CatalogMagic.Length).SequenceEqual(CatalogMagic))
                throw new InvalidDataException(Constants.InvalidFileError);

            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(4, sizeof(int)));
            if (payloadLength <= 0 || payloadLength > page.Length - 8)
                throw new InvalidDataException(Constants.InvalidFileError);

            var catalog = JsonSerializer.Deserialize<StorageCatalog>(page.AsSpan(8, payloadLength))
                ?? throw new InvalidDataException(Constants.InvalidFileError);
            _primaryIndexMetadataPages = new Dictionary<string, int>(
                catalog.PrimaryIndexMetadataPages,
                StringComparer.OrdinalIgnoreCase);

            int expectedIndexCount = catalog.Schema.Tables.Count;
            if (_primaryIndexMetadataPages.Count != expectedIndexCount ||
                _primaryIndexMetadataPages.Values.Distinct().Count() != expectedIndexCount ||
                _primaryIndexMetadataPages.Any(entry =>
                    entry.Value < Constants.FirstAllocatablePageId || entry.Value >= BufferPool.PageCount ||
                    !catalog.Schema.Tables.TryGetValue(entry.Key, out var table) ||
                    table.Columns.Count(column => column.IsPrimaryKey) != 1 ||
                    table.Columns.Single(column => column.IsPrimaryKey).Type != "INTEGER"))
            {
                throw new InvalidDataException(Constants.InvalidFileError);
            }

            return catalog.Schema;
        }

        public void Save(Schema schema)
        {
            WriteCatalog(schema);
            BufferPool.Flush();
        }

        internal PagedPrimaryKeyBPlusTree OpenPrimaryIndex(string tableName)
        {
            if (_primaryIndexes.TryGetValue(tableName, out var index))
                return index;

            if (!_primaryIndexMetadataPages.TryGetValue(tableName, out int metadataPageId))
                throw new InvalidDataException(Constants.InvalidFileError);

            index = PagedPrimaryKeyBPlusTree.Open(
                BufferPool,
                metadataPageId,
                Constants.DefaultBPlusTreeMaxKeys);
            _primaryIndexes.Add(tableName, index);
            return index;
        }

        internal void ValidatePrimaryIndexes(Schema schema)
        {
            foreach (var (tableName, tableDefinition) in schema.Tables)
            {
                if (!tableDefinition.Columns.Any(column => column.IsPrimaryKey))
                    throw new InvalidDataException(Constants.InvalidFileError);

                var index = OpenPrimaryIndex(tableName);
                index.Validate();
            }
        }

        internal void CreatePrimaryIndex(string tableName, Schema schema)
        {
            if (_primaryIndexMetadataPages.ContainsKey(tableName))
                throw new InvalidDataException(Constants.InvalidFileError);

            var index = PagedPrimaryKeyBPlusTree.CreateOnBufferPool(
                BufferPool,
                Constants.DefaultBPlusTreeMaxKeys);
            _primaryIndexes.Add(tableName, index);
            _primaryIndexMetadataPages.Add(tableName, index.MetadataPageId);
            WriteCatalog(schema);
            BufferPool.Flush();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            foreach (var index in _primaryIndexes.Values)
                index.Dispose();
            _primaryIndexes.Clear();
            _bufferPool?.Dispose();
            _bufferPool = null;
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Storage));
        }

        private void WriteCatalog(Schema schema)
        {
            var catalog = new StorageCatalog
            {
                Schema = schema,
                PrimaryIndexMetadataPages = _primaryIndexMetadataPages
            };
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(catalog);
            if (payload.Length > BufferPool.PageSize - 8)
                throw new InvalidOperationException(Constants.CatalogTooLargeError);

            byte[] page = new byte[BufferPool.PageSize];
            CatalogMagic.CopyTo(page, 0);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(4, sizeof(int)), payload.Length);
            payload.CopyTo(page, 8);
            BufferPool.WritePage(Constants.CatalogPageId, page);
        }

        private sealed class StorageCatalog
        {
            public Schema Schema { get; set; } = new();
            public Dictionary<string, int> PrimaryIndexMetadataPages { get; set; } = new();
        }
    }
}