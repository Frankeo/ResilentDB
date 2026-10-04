using Engine.Executors;

namespace Engine
{
    public class DbEngine : IDisposable
    {
        private readonly ExecutionDispatcher _executionDispatcher = new();
        private readonly Storage _storage;
        private Schema _schema;
        private bool _disposed;

        internal Schema Schema => _schema;

        public DbEngine(string filePath)
        {
            _storage = new Storage(filePath);
            try
            {
                if (!_storage.Exists)
                    _storage.CreateFile();

                _schema = _storage.Load();
                _storage.ValidatePrimaryIndexes(_schema);
            }
            catch
            {
                _storage.Dispose();
                throw;
            }
        }

        public void Save()
        {
            ThrowIfDisposed();
            _storage.Save(_schema);
        }

        public object Execute(Statement stmt)
        {
            ThrowIfDisposed();
            return _executionDispatcher.Execute(this, stmt);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _storage.Dispose();
            _disposed = true;
        }

        internal PagedPrimaryKeyBPlusTree OpenPrimaryIndex(string tableName)
        {
            ThrowIfDisposed();
            return _storage.OpenPrimaryIndex(tableName);
        }

        internal void CreatePrimaryIndex(string tableName)
        {
            ThrowIfDisposed();
            _storage.CreatePrimaryIndex(tableName, _schema);
        }

        public IReadOnlyList<object> Execute(string sql)
        {
            ThrowIfDisposed();
            return Parser.ParseStatements(sql)
                .Select(Execute)
                .ToList();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DbEngine));
        }
    }
}