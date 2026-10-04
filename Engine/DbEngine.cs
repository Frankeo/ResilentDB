using Engine.Executors;

namespace Engine
{
    public class DbEngine
    {
        private readonly ExecutionDispatcher _executionDispatcher = new();
        private readonly Storage _storage;
        private Schema _schema;
        private Dictionary<string, List<Row>> _tables;

        internal Schema Schema => _schema;
        internal Dictionary<string, List<Row>> Tables => _tables;

        public DbEngine(string filePath)
        {
            _storage = new Storage(filePath);
            if (!_storage.Exists)
            {
                _storage.CreateFile();
            }
            var (schema, tables) = _storage.Load();
            _schema = schema;
            _tables = tables;
        }

        public void Save()
        {
            _storage.Save(_schema, _tables);
        }

        public object Execute(Statement stmt) => _executionDispatcher.Execute(this, stmt);

        public IReadOnlyList<object> Execute(string sql)
        {
            return Parser.ParseStatements(sql)
                .Select(Execute)
                .ToList();
        }
    }
}