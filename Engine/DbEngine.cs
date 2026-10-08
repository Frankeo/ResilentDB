using Engine.Executors;

namespace Engine
{
    public class DbEngine : IDisposable, IExecutionContext
    {
        private readonly ExecutionDispatcher _executionDispatcher = new();
        private readonly Storage _storage;
        public IEngineTraceSink? TraceSink { get; }
        private Schema _schema;
        private bool _disposed;

        internal Schema Schema => _schema;
        Schema IExecutionContext.Schema => _schema;

        public DbEngine(string filePath, IEngineTraceSink? traceSink = null)
        {
            TraceSink = traceSink;
            _storage = new Storage(filePath, traceSink);
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

        void IExecutionContext.Save() => Save();

        public ExecutionResult Execute(Statement stmt)
        {
            ThrowIfDisposed();
            Trace("Engine", "StatementStarted", stmt.GetType().Name);
            try
            {
                var result = _executionDispatcher.Execute(this, stmt);
                Trace("Engine", "StatementCompleted", $"{stmt.GetType().Name}: {result}");
                return result;
            }
            catch (Exception exception)
            {
                Trace("Engine", "StatementFailed", $"{stmt.GetType().Name}: {exception.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _storage.Dispose();
            _disposed = true;
        }

        internal BPlusTree OpenPrimaryIndex(string tableName)
        {
            ThrowIfDisposed();
            return _storage.OpenPrimaryIndex(tableName);
        }

        IPrimaryKeyIndex IExecutionContext.OpenPrimaryIndex(string tableName)
        {
            ThrowIfDisposed();
            return _storage.OpenPrimaryIndex(tableName);
        }

        internal void CreatePrimaryIndex(string tableName)
        {
            ThrowIfDisposed();
            _storage.CreatePrimaryIndex(tableName, _schema);
        }

        void IExecutionContext.CreatePrimaryIndex(string tableName)
        {
            ThrowIfDisposed();
            _storage.CreatePrimaryIndex(tableName, _schema);
        }

        public IReadOnlyList<ExecutionResult> Execute(string sql)
        {
            ThrowIfDisposed();
            var statements = Parser.ParseStatements(sql).ToList();
            Trace("Parser", "StatementsParsed", $"count={statements.Count}");
            return statements.Select(Execute).ToList();
        }

        private void Trace(string component, string operation, string detail) =>
            TraceSink?.Write(new EngineTraceEvent(component, operation, detail));

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DbEngine));
        }
    }
}