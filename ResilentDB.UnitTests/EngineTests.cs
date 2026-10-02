using Engine;
using Xunit;

namespace ResilentDB.UnitTests;

public sealed class EngineTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ResilentDB-{Guid.NewGuid():N}.mdb");

    [Fact]
    public void Parse_CreateTableStatement_ReturnsTableAndColumns()
    {
        var statement = Assert.IsType<CreateTableStatement>(
            Parser.Parse("CREATE TABLE users (id INTEGER, name TEXT)"));

        Assert.Equal("users", statement.TableName);
        Assert.Collection(
            statement.Columns,
            column =>
            {
                Assert.Equal("id", column.Name);
                Assert.Equal("INTEGER", column.Type);
            },
            column =>
            {
                Assert.Equal("name", column.Name);
                Assert.Equal("TEXT", column.Type);
            });
    }

    [Fact]
    public void Parse_InsertStatement_PreservesCommasInsideQuotedValues()
    {
        var statement = Assert.IsType<InsertStatement>(
            Parser.Parse("INSERT INTO users VALUES (1, 'Smith, Jane')"));

        Assert.Equal("users", statement.TableName);
        Assert.Equal(new object[] { 1, "Smith, Jane" }, statement.Values);
    }

    [Fact]
    public void Parse_SelectStatement_ParsesProjectionAndWhereClause()
    {
        var statement = Assert.IsType<SelectStatement>(
            Parser.Parse("SELECT name FROM users WHERE age > 18"));

        Assert.Equal("users", statement.TableName);
        Assert.Equal(new[] { "name" }, statement.Columns);
        var where = Assert.IsType<WhereClause>(statement.Where);
        Assert.Equal("age", where.Column);
        Assert.Equal(">", where.Op);
        Assert.Equal(18, where.Value);
    }

    [Fact]
    public void Execute_CreateInsertAndSelect_ReturnsMatchingRows()
    {
        var engine = new DbEngine(_databasePath);
        engine.Execute(Parser.Parse("CREATE TABLE users (id INTEGER, name TEXT, age INTEGER)"));
        engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'Ana', 28)"));
        engine.Execute(Parser.Parse("INSERT INTO users VALUES (2, 'Luis', 16)"));

        var rows = Assert.IsType<List<Row>>(
            engine.Execute(Parser.Parse("SELECT name FROM users WHERE age > 18")));

        var row = Assert.Single(rows);
        Assert.Equal("Ana", row.Values["name"]);
    }

    [Fact]
    public void Execute_MultipleSemicolonSeparatedStatements_RunsAllStatementsInOrder()
    {
        var engine = new DbEngine(_databasePath);
        const string sql = "CREATE TABLE users (id INTEGER, name TEXT); " +
            "INSERT INTO users VALUES (1, 'Ana'); " +
            "INSERT INTO users VALUES (2, 'Luis'); " +
            "INSERT INTO users VALUES (3, 'Sam; Smith'); " +
            "SELECT * FROM users";

        var results = engine.Execute(sql);

        Assert.Equal(5, results.Count);
        Assert.Equal("Tabla creada", results[0]);
        Assert.Equal("1 fila insertada", results[1]);
        Assert.Equal("1 fila insertada", results[2]);
        Assert.Equal("1 fila insertada", results[3]);

        var rows = Assert.IsType<List<Row>>(results[4]);
        Assert.Equal(3, rows.Count);
        Assert.Equal("Sam; Smith", rows[2].Values["name"]);
    }

    [Fact]
    public void Execute_RejectsDuplicateTablesAndIncorrectValueCounts()
    {
        var engine = new DbEngine(_databasePath);
        var createTable = Parser.Parse("CREATE TABLE users (id INTEGER)");
        engine.Execute(createTable);

        Assert.Throws<Exception>(() => engine.Execute(createTable));
        Assert.Throws<Exception>(() =>
            engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'extra')")));
    }

    [Fact]
    public void Execute_PersistsRowsForANewEngineInstance()
    {
        var engine = new DbEngine(_databasePath);
        engine.Execute(Parser.Parse("CREATE TABLE users (id INTEGER, name TEXT)"));
        engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'Ana')"));

        var reloadedEngine = new DbEngine(_databasePath);
        var rows = Assert.IsType<List<Row>>(
            reloadedEngine.Execute(Parser.Parse("SELECT * FROM users")));

        var row = Assert.Single(rows);
        Assert.Equal("1", row.Values["id"].ToString());
        Assert.Equal("Ana", row.Values["name"].ToString());
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }
}