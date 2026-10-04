using Engine.Executors;

namespace Engine
{
    public class DbEngine : IDisposable
    {
        private readonly ExecutionDispatcher _executionDispatcher = new();
        private readonly Storage _storage;
        private Schema _schema;

        internal Schema Schema => _schema;

        public DbEngine(string filePath)
        {
            _storage = new Storage(filePath);
            if (!_storage.Exists)
            {
                _storage.CreateFile();
            }
            _schema = _storage.Load();
            _storage.ValidatePrimaryIndexes(_schema);
        }

        public void Save() => _storage.Save(_schema);

        public object Execute(Statement stmt) => _executionDispatcher.Execute(this, stmt);

        public void Dispose() => _storage.Dispose();

        internal PagedPrimaryKeyBPlusTree OpenPrimaryIndex(string tableName) =>
            _storage.OpenPrimaryIndex(tableName);

        internal void CreatePrimaryIndex(string tableName) =>
            _storage.CreatePrimaryIndex(tableName, _schema);

        public IReadOnlyList<object> Execute(string sql)
        {
            return Parser.ParseStatements(sql)
                .Select(Execute)
                .ToList();
        }
    }
}