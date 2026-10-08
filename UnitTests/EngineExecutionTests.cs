using Engine;
using Xunit;

namespace UnitTests;

public sealed class EngineExecutionTests : EngineTestBase
{
    [Fact]
    public void UpdateAndDeleteApplyWhereAndPreservePrimaryKeyUniqueness()
    {
        using var engine = new DbEngine(DatabasePath);
        engine.Execute(Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)"));
        engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'Ana', 28)"));
        engine.Execute(Parser.Parse("INSERT INTO users VALUES (2, 'Luis', 16)"));

        var updateResult = Assert.IsType<CommandResult>(
            engine.Execute(Parser.Parse("UPDATE users SET name = 'Carlos', age = 30 WHERE id = 1")));
        Assert.Equal(1, updateResult.AffectedRows);
        Assert.Equal("Filas actualizadas: 1", updateResult.Message);
        Assert.Throws<Exception>(() =>
            engine.Execute(Parser.Parse("UPDATE users SET id = 2 WHERE id = 1")));
        Assert.Throws<Exception>(() =>
            engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'Duplicado', 20)")));

        var deleteResult = Assert.IsType<CommandResult>(
            engine.Execute(Parser.Parse("DELETE FROM users WHERE age < 18")));
        Assert.Equal(1, deleteResult.AffectedRows);
        Assert.Equal("Filas eliminadas: 1", deleteResult.Message);
        var row = Assert.Single(ReadRows(engine.Execute(Parser.Parse("SELECT * FROM users"))));
        Assert.Equal(1, row.Values["id"]);
        Assert.Equal("Carlos", row.Values["name"]);
    }

    [Fact]
    public void InsertRequiresAndPersistsPrimaryKeyDefinition()
    {
        using var engine = new DbEngine(DatabasePath);
        engine.Execute(Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)"));
        engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'Ana')"));

        engine.Dispose();
        using var reloadedEngine = new DbEngine(DatabasePath);
        Assert.Throws<Exception>(() =>
            reloadedEngine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'Duplicado')")));
    }

    [Fact]
    public void RejectsSecondEngineWhileDatabaseIsOpen()
    {
        using var engine = new DbEngine(DatabasePath);

        Assert.Throws<IOException>(() => new DbEngine(DatabasePath));
    }

    [Fact]
    public void SelectDeleteAndUpdateValidateWhereBeforeScanningRows()
    {
        using var engine = new DbEngine(DatabasePath);
        engine.Execute(Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY)"));

        Assert.Throws<Exception>(() =>
            engine.Execute(Parser.Parse("SELECT * FROM users WHERE missing = 1")));
        Assert.Throws<Exception>(() =>
            engine.Execute(Parser.Parse("DELETE FROM users WHERE missing = 1")));
        Assert.Throws<Exception>(() =>
            engine.Execute(Parser.Parse("UPDATE users SET id = 1 WHERE missing = 1")));

        var invalidOperator = new SelectStatement
        {
            TableName = "users",
            Columns = new List<string> { "*" },
            Where = new WhereClause { Column = "id", Op = "!=", Value = 1 }
        };
        Assert.Throws<NotSupportedException>(() => engine.Execute(invalidOperator));
    }

    [Fact]
    public void CreateInsertAndSelectReturnsMatchingRows()
    {
        using var engine = new DbEngine(DatabasePath);
        engine.Execute(Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)"));
        engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'Ana', 28)"));
        engine.Execute(Parser.Parse("INSERT INTO users VALUES (2, 'Luis', 16)"));

        var query = Assert.IsType<QueryResult>(
            engine.Execute(Parser.Parse("SELECT name FROM users WHERE age > 18")));

        Assert.Equal(new[] { "name" }, query.Columns);
        var row = Assert.Single(query.Rows);
        Assert.Equal("Ana", row[0]);
    }

    [Fact]
    public void MultipleSemicolonSeparatedStatementsRunInOrder()
    {
        using var engine = new DbEngine(DatabasePath);
        const string sql = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT); " +
            "INSERT INTO users VALUES (1, 'Ana'); " +
            "INSERT INTO users VALUES (2, 'Luis'); " +
            "INSERT INTO users VALUES (3, 'Sam; Smith'); " +
            "SELECT * FROM users";

        var results = engine.Execute(sql);

        Assert.Equal(5, results.Count);
        Assert.Equal("Tabla creada", Assert.IsType<CommandResult>(results[0]).Message);
        Assert.Equal("1 fila insertada", Assert.IsType<CommandResult>(results[1]).Message);
        Assert.Equal("1 fila insertada", Assert.IsType<CommandResult>(results[2]).Message);
        Assert.Equal("1 fila insertada", Assert.IsType<CommandResult>(results[3]).Message);

        var rows = ReadRows(results[4]);
        Assert.Equal(3, rows.Count);
        Assert.Equal("Sam; Smith", rows[2].Values["name"]);
    }

    [Fact]
    public void RejectsDuplicateTablesAndIncorrectValueCounts()
    {
        using var engine = new DbEngine(DatabasePath);
        var createTable = Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY)");
        engine.Execute(createTable);

        Assert.Throws<Exception>(() => engine.Execute(createTable));
        Assert.Throws<Exception>(() =>
            engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'extra')")));
    }

    [Fact]
    public void PersistsRowsForANewEngineInstance()
    {
        using var engine = new DbEngine(DatabasePath);
        engine.Execute(Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)"));
        engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'Ana')"));

        engine.Dispose();
        using var reloadedEngine = new DbEngine(DatabasePath);
        var row = Assert.Single(ReadRows(
            reloadedEngine.Execute(Parser.Parse("SELECT * FROM users"))));

        Assert.Equal("1", row.Values["id"].ToString());
        Assert.Equal("Ana", row.Values["name"]);
    }

    [Fact]
    public void DisposePreventsFurtherOperations()
    {
        var engine = new DbEngine(DatabasePath);
        engine.Dispose();
        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() => engine.Save());
        Assert.Throws<ObjectDisposedException>(() => engine.Execute("SELECT * FROM users"));
    }

    [Fact]
    public void TraceSinkShowsFieldChangesAndBPlusTreeTraversal()
    {
        var traceSink = new RecordingTraceSink();
        using var engine = new DbEngine(DatabasePath, traceSink);
        engine.Execute("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
        for (int id = 1; id <= 40; id++)
            engine.Execute($"INSERT INTO users VALUES ({id}, 'User {id}')");

        engine.Execute("UPDATE users SET name = 'Updated' WHERE id = 35");
        engine.Execute("SELECT * FROM users WHERE id = 35");
        engine.Execute("DELETE FROM users WHERE id = 35");

        Assert.Contains(traceSink.Events, traceEvent =>
            traceEvent.Component == "Executor" && traceEvent.Operation == "FieldAdded");
        Assert.Contains(traceSink.Events, traceEvent =>
            traceEvent.Component == "Executor" && traceEvent.Operation == "FieldUpdated");
        Assert.Contains(traceSink.Events, traceEvent =>
            traceEvent.Component == "Executor" && traceEvent.Operation == "RowDelete" &&
            traceEvent.Detail.Contains("name="));
        Assert.Contains(traceSink.Events, traceEvent =>
            traceEvent.Component == "BPlusTree" && traceEvent.Operation == "LeafSplit");
        Assert.Contains(traceSink.Events, traceEvent =>
            traceEvent.Component == "BPlusTree" && traceEvent.Operation == "InternalNodeVisited");
        Assert.Contains(traceSink.Events, traceEvent =>
            traceEvent.Component == "BPlusTree" && traceEvent.Operation == "LookupCompleted");
        Assert.Contains(traceSink.Events, traceEvent =>
            traceEvent.Component == "BPlusTree" && traceEvent.Operation == "DeleteCompleted");
    }

    private sealed class RecordingTraceSink : IEngineTraceSink
    {
        public List<EngineTraceEvent> Events { get; } = new();

        public void Write(EngineTraceEvent traceEvent) => Events.Add(traceEvent);
    }
}