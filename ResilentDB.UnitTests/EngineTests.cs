using Engine;
using Engine.BufferPool;
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

    [Fact]
    public void Pager_AllocatesAndReadsFixedSizePages()
    {
        using var pager = new Pager(_databasePath, pageSize: 64);

        var header = pager.ReadHeader();
        Assert.Equal("RDBP", header.Magic);
        Assert.Equal(64, header.PageSize);
        Assert.Equal(1, header.PageCount);

        var pageId = pager.AllocatePage();
        pager.WritePage(pageId, new byte[] { 1, 2, 3 });

        var page = pager.ReadPage(pageId);
        Assert.Equal(new byte[] { 1, 2, 3 }, page[..3]);
        Assert.All(page[3..], value => Assert.Equal(0, value));
        Assert.Equal(2, pager.ReadHeader().PageCount);
        Assert.Equal(128, new FileInfo(_databasePath).Length);
    }

    [Fact]
    public void Pager_RejectsHeaderPageAndOversizedWrites()
    {
        using var pager = new Pager(_databasePath, pageSize: 64);

        Assert.Throws<ArgumentOutOfRangeException>(() => pager.ReadPage(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => pager.WritePage(0, Array.Empty<byte>()));
        Assert.Throws<ArgumentException>(() => pager.WritePage(1, new byte[65]));
    }

    [Fact]
    public void LruBufferPool_EvictsLeastRecentlyUsedAndPersistsDirtyPages()
    {
        int firstPageId;
        int secondPageId;
        int thirdPageId;

        using (var store = new LruBufferPool(_databasePath, capacity: 2))
        {
            firstPageId = store.AllocatePage();
            secondPageId = store.AllocatePage();
            thirdPageId = store.AllocatePage();

            store.WritePage(firstPageId, new byte[] { 1 });
            store.WritePage(secondPageId, new byte[] { 2 });
            Assert.Equal(1, store.ReadPage(firstPageId)[0]);
            store.WritePage(thirdPageId, new byte[] { 3 });

            Assert.Equal(2, store.CachedPageCount);
            Assert.Equal(2, store.ReadPage(secondPageId)[0]);
            Assert.True(store.CachedPageCount <= store.Capacity);
            store.Flush();
        }

        using var pager = new Pager(_databasePath);
        Assert.Equal(1, pager.ReadPage(firstPageId)[0]);
        Assert.Equal(2, pager.ReadPage(secondPageId)[0]);
        Assert.Equal(3, pager.ReadPage(thirdPageId)[0]);
    }

    [Fact]
    public void FullCacheBufferPool_LoadsAllPagesAndFlushesDirtyData()
    {
        int pageId;
        using (var pager = new Pager(_databasePath))
        {
            pageId = pager.AllocatePage();
            pager.WritePage(pageId, new byte[] { 1 });
        }

        using (var store = new FullCacheBufferPool(_databasePath))
        {
            Assert.Equal(store.PageCount - 1, store.CachedPageCount);
            Assert.Equal(1, store.ReadPage(pageId)[0]);
            store.WritePage(pageId, new byte[] { 7 });
            store.Flush();
        }

        using var reloadedPager = new Pager(_databasePath);
        Assert.Equal(7, reloadedPager.ReadPage(pageId)[0]);
    }

    [Fact]
    public void ClockBufferPool_GivesSecondChanceAndPersistsDirtyEvictions()
    {
        int firstPageId;
        int secondPageId;
        int thirdPageId;
        using (var pager = new Pager(_databasePath))
        {
            firstPageId = pager.AllocatePage();
            secondPageId = pager.AllocatePage();
            thirdPageId = pager.AllocatePage();
            pager.WritePage(firstPageId, new byte[] { 1 });
            pager.WritePage(secondPageId, new byte[] { 2 });
            pager.WritePage(thirdPageId, new byte[] { 3 });
        }

        using (var bufferPool = new ClockBufferPool(_databasePath, capacity: 2))
        {
            Assert.Equal(1, bufferPool.ReadPage(firstPageId)[0]);
            Assert.Equal(2, bufferPool.ReadPage(secondPageId)[0]);
            Assert.Equal(1, bufferPool.ReadPage(firstPageId)[0]);
            Assert.Equal(3, bufferPool.ReadPage(thirdPageId)[0]);

            bufferPool.WritePage(secondPageId, new byte[] { 22 });
            Assert.Equal(1, bufferPool.ReadPage(firstPageId)[0]);
            Assert.True(bufferPool.CachedPageCount <= bufferPool.Capacity);
            bufferPool.Flush();
        }

        using var verifyPager = new Pager(_databasePath);
        Assert.Equal(1, verifyPager.ReadPage(firstPageId)[0]);
        Assert.Equal(22, verifyPager.ReadPage(secondPageId)[0]);
        Assert.Equal(3, verifyPager.ReadPage(thirdPageId)[0]);
    }

    [Fact]
    public void Storage_ReloadsDataSpanningMultiplePages()
    {
        var engine = new DbEngine(_databasePath);
        engine.Execute(Parser.Parse("CREATE TABLE documents (id INTEGER, body TEXT)"));
        var expectedBody = new string('x', Constants.DefaultPageSize * 3);
        engine.Execute(new InsertStatement
        {
            TableName = "documents",
            Values = new List<object> { 1, expectedBody }
        });

        var reloadedEngine = new DbEngine(_databasePath);
        var rows = Assert.IsType<List<Row>>(
            reloadedEngine.Execute(Parser.Parse("SELECT * FROM documents")));

        var row = Assert.Single(rows);
        Assert.Equal(expectedBody, row.Values["body"].ToString());
        Assert.True(new FileInfo(_databasePath).Length > Constants.DefaultPageSize * 3);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }
}