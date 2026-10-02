using Engine.Executors;

namespace Engine
{
    public class DbEngine
    {
        private static readonly IExecutor[] Executors =
        {
            new CreateTableExecutor(),
            new InsertExecutor(),
            new SelectExecutor()
        };

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

        public object Execute(Statement stmt)
        {
            foreach (var executor in Executors)
            {
                if (executor.CanExecute(stmt))
                    return executor.Execute(this, stmt);
            }

            throw new NotSupportedException(Constants.UnsupportedCommandError);
        }

        public IReadOnlyList<object> Execute(string sql)
        {
            return Parser.ParseStatements(sql)
                .Select(Execute)
                .ToList();
        }
    }
}