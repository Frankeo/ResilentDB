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
            Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)"));

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
    public void Parse_CreateTableStatement_RequiresPrimaryKey()
    {
        Assert.Throws<Exception>(() =>
            Parser.Parse("CREATE TABLE users (id INTEGER, name TEXT)"));
    }

        [Fact]
        public void Parse_CreateTableStatement_MarksIntegerPrimaryKey()
        {
            var statement = Assert.IsType<CreateTableStatement>(
                Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)"));

            Assert.True(statement.Columns[0].IsPrimaryKey);
            Assert.False(statement.Columns[1].IsPrimaryKey);
        }

        [Fact]
        public void Execute_UpdateAndDeleteApplyWhereAndPreservePrimaryKeyUniqueness()
        {
            using var engine = new DbEngine(_databasePath);
            engine.Execute(Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)"));
            engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'Ana', 28)"));
            engine.Execute(Parser.Parse("INSERT INTO users VALUES (2, 'Luis', 16)"));

            Assert.Equal(
                "Filas actualizadas: 1",
                engine.Execute(Parser.Parse("UPDATE users SET name = 'Carlos', age = 30 WHERE id = 1")));
            Assert.Throws<Exception>(() =>
                engine.Execute(Parser.Parse("UPDATE users SET id = 2 WHERE id = 1")));
            Assert.Throws<Exception>(() =>
                engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'Duplicado', 20)")));

            Assert.Equal("Filas eliminadas: 1", engine.Execute(Parser.Parse("DELETE FROM users WHERE age < 18")));
            var rows = Assert.IsType<List<Row>>(engine.Execute(Parser.Parse("SELECT * FROM users")));
            var row = Assert.Single(rows);
            Assert.Equal(1, row.Values["id"]);
            Assert.Equal("Carlos", row.Values["name"]);
        }

        [Fact]
        public void Execute_RequiresPrimaryKeyAndPersistsItsDefinition()
        {
            using var engine = new DbEngine(_databasePath);
            engine.Execute(Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)"));
            engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'Ana')"));

            engine.Dispose();
            using var reloadedEngine = new DbEngine(_databasePath);
            Assert.Throws<Exception>(() =>
                reloadedEngine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'Duplicado')")));
        }

            [Fact]
            public void Execute_RejectsSecondEngineWhileDatabaseIsOpen()
            {
                using var engine = new DbEngine(_databasePath);

                Assert.Throws<IOException>(() => new DbEngine(_databasePath));
            }

        [Fact]
        public void Execute_SelectDeleteAndUpdateValidateWhereBeforeScanningRows()
        {
            using var engine = new DbEngine(_databasePath);
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
        public void PagedPrimaryKeyBPlusTree_SplitsScansDeletesAndReloads()
        {
            var indexPath = $"{_databasePath}.tree.idx";
            try
            {
                using (var tree = new PagedPrimaryKeyBPlusTree(
                           indexPath,
                           maxKeys: 3,
                           bufferPoolCapacity: 2))
                {
                    foreach (long key in Enumerable.Range(0, 64)
                                 .OrderBy(value => (value * 37) % 64)
                                 .Select(value => (long)value + 1))
                    {
                        tree.Insert(key, key * 10);
                    }

                    Assert.True(tree.RootPageId.Value > 2);
                    Assert.True(tree.TryGetValue(17, out var value));
                    Assert.Equal(170, value);
                    Assert.Throws<InvalidOperationException>(() => tree.Insert(17, 999));
                    Assert.Equal(new long[] { 20, 21, 22 },
                        tree.Scan(20, 22).Select(entry => entry.Key));

                    for (long key = 2; key <= 64; key += 2)
                        Assert.True(tree.Delete(key));

                    Assert.False(tree.Delete(2));
                    tree.Validate();
                    Assert.Equal(Enumerable.Range(1, 64).Where(key => key % 2 == 1),
                        tree.Scan().Select(entry => (int)entry.Key));
                }

                using var reloaded = new PagedPrimaryKeyBPlusTree(
                    indexPath,
                    maxKeys: 32,
                    bufferPoolCapacity: 2);
                reloaded.Validate();
                Assert.Equal(32, reloaded.Count);
                Assert.True(reloaded.TryGetValue(63, out var persistedValue));
                Assert.Equal(630, persistedValue);
            }
            finally
            {
                if (File.Exists(indexPath))
                    File.Delete(indexPath);
            }
        }

        [Fact]
        public void PagedPrimaryKeyBPlusTree_StoresRowsInlineAndInOverflowPages()
        {
            var indexPath = $"{_databasePath}.row-tree.idx";
            try
            {
                var largeBody = new string('x', Constants.DefaultPageSize * 2);
                using (var tree = new PagedPrimaryKeyBPlusTree(indexPath, maxKeys: 3, bufferPoolCapacity: 2))
                {
                    tree.InsertRecord(1, new Row
                    {
                        Values = new Dictionary<string, object>
                        {
                            ["id"] = 1,
                            ["name"] = "inline"
                        }
                    });
                    tree.InsertRecord(2, new Row
                    {
                        Values = new Dictionary<string, object>
                        {
                            ["id"] = 2,
                            ["body"] = largeBody
                        }
                    });

                    Assert.True(tree.TryGetRecord(1, out var inlineRow));
                    Assert.Equal("inline", inlineRow!.Values["name"].ToString());
                    Assert.True(tree.TryGetRecord(2, out var overflowRow));
                    Assert.Equal(largeBody, overflowRow!.Values["body"].ToString());
                    tree.Validate();
                }

                using var reloaded = new PagedPrimaryKeyBPlusTree(indexPath, maxKeys: 3, bufferPoolCapacity: 2);
                Assert.True(reloaded.TryGetRecord(2, out var persistedRow));
                Assert.Equal(largeBody, persistedRow!.Values["body"].ToString());
            }
            finally
            {
                if (File.Exists(indexPath))
                    File.Delete(indexPath);
            }
        }

        [Fact]
        public void PagedPrimaryKeyBPlusTree_RebalancesAndReusesFreedPages()
        {
            var indexPath = $"{_databasePath}.rebalance.idx";
            try
            {
                using var tree = new PagedPrimaryKeyBPlusTree(indexPath, maxKeys: 3, bufferPoolCapacity: 2);
                for (long key = 1; key <= 64; key++)
                    tree.Insert(key, key);

                int allocatedPageCount = tree.PageCount;
                for (long key = 64; key >= 1; key--)
                    Assert.True(tree.Delete(key));

                tree.Validate();
                Assert.Equal(0, tree.Count);
                Assert.True(tree.FreePageCount > 0);

                for (long key = 1; key <= 64; key++)
                    tree.Insert(key, key);

                tree.Validate();
                Assert.Equal(64, tree.Count);
                Assert.Equal(allocatedPageCount, tree.PageCount);
            }
            finally
            {
                if (File.Exists(indexPath))
                    File.Delete(indexPath);
            }
        }

        [Fact]
        public void PagedPrimaryKeyBPlusTree_MatchesRandomizedSortedDictionaryOperations()
        {
            var indexPath = $"{_databasePath}.random-tree.idx";
            var expected = new SortedDictionary<long, long>();
            var random = new Random(84521);
            try
            {
                using (var tree = new PagedPrimaryKeyBPlusTree(
                           indexPath,
                           maxKeys: 3,
                           bufferPoolCapacity: 2))
                {
                    for (int operation = 0; operation < 500; operation++)
                    {
                        long key = random.Next(0, 120);
                        switch (random.Next(4))
                        {
                            case 0:
                                if (expected.TryAdd(key, operation))
                                    tree.Insert(key, operation);
                                else
                                    Assert.Throws<InvalidOperationException>(() => tree.Insert(key, operation));
                                break;
                            case 1:
                                Assert.Equal(expected.Remove(key), tree.Delete(key));
                                break;
                            case 2:
                                Assert.Equal(
                                    expected.TryGetValue(key, out long expectedValue),
                                    tree.TryGetValue(key, out long actualValue));
                                if (expected.ContainsKey(key))
                                    Assert.Equal(expectedValue, actualValue);
                                break;
                            default:
                                long maximum = key + random.Next(0, 20);
                                Assert.Equal(
                                    expected.Where(pair => pair.Key >= key && pair.Key <= maximum)
                                        .Select(pair => pair.Key),
                                    tree.Scan(key, maximum).Select(pair => pair.Key));
                                break;
                        }

                        if (operation % 25 == 0)
                        {
                            tree.Validate();
                            Assert.Equal(expected.Count, tree.Count);
                            Assert.Equal(expected, tree.Scan().ToDictionary(pair => pair.Key, pair => pair.Value));
                        }
                    }
                }

                using var reloaded = new PagedPrimaryKeyBPlusTree(
                    indexPath,
                    maxKeys: 3,
                    bufferPoolCapacity: 2);
                reloaded.Validate();
                Assert.Equal(expected, reloaded.Scan().ToDictionary(pair => pair.Key, pair => pair.Value));
            }
            finally
            {
                if (File.Exists(indexPath))
                    File.Delete(indexPath);
            }
        }

        [Fact]
        public void PagedPrimaryKeyBPlusTree_ReusesOverflowPagesAfterDelete()
        {
            var indexPath = $"{_databasePath}.overflow.idx";
            try
            {
                var largeBody = new string('x', Constants.DefaultPageSize * 2);
                using var tree = new PagedPrimaryKeyBPlusTree(indexPath, maxKeys: 3, bufferPoolCapacity: 2);
                tree.InsertRecord(1, new Row
                {
                    Values = new Dictionary<string, object> { ["id"] = 1, ["body"] = largeBody }
                });
                Assert.Equal(0, tree.FreePageCount);
                Assert.True(tree.TryGetRecord(1, out var insertedRow));
                Assert.Equal(largeBody, insertedRow!.Values["body"].ToString());
                int pageCountWithOverflow = tree.PageCount;
                Assert.True(tree.Delete(1));
                int freePagesAfterDelete = tree.FreePageCount;
                Assert.True(freePagesAfterDelete >= 2);

                tree.InsertRecord(2, new Row
                {
                    Values = new Dictionary<string, object> { ["id"] = 2, ["body"] = largeBody }
                });

                Assert.Equal(pageCountWithOverflow, tree.PageCount);
                Assert.True(tree.FreePageCount < freePagesAfterDelete);
                tree.Validate();
            }
            finally
            {
                if (File.Exists(indexPath))
                    File.Delete(indexPath);
            }
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
        using var engine = new DbEngine(_databasePath);
        engine.Execute(Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)"));
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
        using var engine = new DbEngine(_databasePath);
        const string sql = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT); " +
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
        using var engine = new DbEngine(_databasePath);
        var createTable = Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY)");
        engine.Execute(createTable);

        Assert.Throws<Exception>(() => engine.Execute(createTable));
        Assert.Throws<Exception>(() =>
            engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'extra')")));
    }

    [Fact]
    public void Execute_PersistsRowsForANewEngineInstance()
    {
        using var engine = new DbEngine(_databasePath);
        engine.Execute(Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)"));
        engine.Execute(Parser.Parse("INSERT INTO users VALUES (1, 'Ana')"));

        engine.Dispose();
        using var reloadedEngine = new DbEngine(_databasePath);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => pager.WritePage(1, new byte[] { 1 }));
        int pageId = pager.AllocatePage();
        Assert.Throws<ArgumentException>(() => pager.WritePage(pageId, new byte[65]));
    }

    [Fact]
    public void LruBufferPool_EvictsLeastRecentlyUsedAndPersistsDirtyPages()
    {
        int firstPageId;
        int secondPageId;
        int thirdPageId;

        using (var store = new LruBufferPool(_databasePath, capacity: 2))
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => store.ReadPage(0));
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
    public void LruBufferPool_SerializesConcurrentAllocationsAndWrites()
    {
        const int pageCount = 32;
        var pageIds = new int[pageCount];

        using (var bufferPool = new LruBufferPool(_databasePath, capacity: 4))
        {
            Parallel.For(0, pageCount, index => pageIds[index] = bufferPool.AllocatePage());
            Assert.Equal(pageCount + 1, bufferPool.PageCount);
            Assert.Equal(pageCount, pageIds.Distinct().Count());

            Parallel.For(0, pageCount, index =>
                bufferPool.WritePage(pageIds[index], new[] { (byte)(index + 1) }));

            Assert.True(bufferPool.CachedPageCount <= bufferPool.Capacity);
            bufferPool.Flush();
        }

        using var pager = new Pager(_databasePath);
        for (var index = 0; index < pageCount; index++)
            Assert.Equal(index + 1, pager.ReadPage(pageIds[index])[0]);
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
            var detachedCopy = bufferPool.ReadPage(firstPageId);
            detachedCopy[0] = 99;
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
    public void ClockBufferPool_DoesNotEvictPinnedPages()
    {
        int firstPageId;
        int secondPageId;
        using (var pager = new Pager(_databasePath))
        {
            firstPageId = pager.AllocatePage();
            secondPageId = pager.AllocatePage();
            pager.WritePage(firstPageId, new byte[] { 1 });
            pager.WritePage(secondPageId, new byte[] { 2 });
        }

        using (var bufferPool = new ClockBufferPool(_databasePath, capacity: 1))
        {
            byte[] pinnedPage = bufferPool.FetchPage(firstPageId);
            Assert.Equal(1, pinnedPage[0]);
            Assert.Throws<InvalidOperationException>(() => bufferPool.FetchPage(secondPageId));

            pinnedPage[0] = 9;
            Assert.True(bufferPool.UnpinPage(firstPageId, dirty: true));
            Assert.Equal(2, bufferPool.ReadPage(secondPageId)[0]);
            Assert.False(bufferPool.UnpinPage(firstPageId, dirty: false));
            bufferPool.Flush();
        }

        using var verifyPager = new Pager(_databasePath);
        Assert.Equal(9, verifyPager.ReadPage(firstPageId)[0]);
    }

    [Fact]
    public void ClockBufferPool_PageHandleUnpinsAndPersistsDirtyData()
    {
        int pageId;
        using (var pager = new Pager(_databasePath))
        {
            pageId = pager.AllocatePage();
            pager.WritePage(pageId, new byte[] { 1 });
        }

        using (var bufferPool = new ClockBufferPool(_databasePath, capacity: 1))
        {
            using (var handle = bufferPool.FetchPageHandle(pageId))
            {
                handle.Data[0] = 7;
                handle.MarkDirty();
                Assert.Throws<InvalidOperationException>(() => bufferPool.Dispose());
            }

            Assert.Equal(7, bufferPool.ReadPage(pageId)[0]);
        }

        var disposedPool = new ClockBufferPool(_databasePath);
        disposedPool.Dispose();
        disposedPool.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposedPool.ReadPage(pageId));
    }

    [Fact]
    public void Pager_DisposePreventsReadsEvenWhenHeaderWasCached()
    {
        var pager = new Pager(_databasePath);
        Assert.Equal(1, pager.ReadHeader().PageCount);
        pager.Dispose();
        pager.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pager.ReadHeader());
        Assert.Throws<ObjectDisposedException>(() => pager.AllocatePage());
    }

    [Fact]
    public void DbEngine_DisposePreventsFurtherOperations()
    {
        var engine = new DbEngine(_databasePath);
        engine.Dispose();
        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() => engine.Save());
        Assert.Throws<ObjectDisposedException>(() => engine.Execute("SELECT * FROM users"));
    }

    [Fact]
    public void ClockBufferPool_SerializesConcurrentPageAccess()
    {
        var pageIds = new int[8];
        using (var pager = new Pager(_databasePath))
        {
            for (var index = 0; index < pageIds.Length; index++)
                pageIds[index] = pager.AllocatePage();
        }

        using (var bufferPool = new ClockBufferPool(_databasePath, capacity: 3))
        {
            Parallel.For(0, pageIds.Length, index =>
                bufferPool.WritePage(pageIds[index], new[] { (byte)(index + 1) }));

            Assert.True(bufferPool.CachedPageCount <= bufferPool.Capacity);
            bufferPool.Flush();
        }

        using var verifyPager = new Pager(_databasePath);
        for (var index = 0; index < pageIds.Length; index++)
            Assert.Equal(index + 1, verifyPager.ReadPage(pageIds[index])[0]);
    }

    [Fact]
    public void Storage_ReloadsDataSpanningMultiplePages()
    {
        using var engine = new DbEngine(_databasePath);
        engine.Execute(Parser.Parse("CREATE TABLE documents (id INTEGER PRIMARY KEY, body TEXT)"));
        var expectedBody = new string('x', Constants.DefaultPageSize * 3);
        engine.Execute(new InsertStatement
        {
            TableName = "documents",
            Values = new List<object> { 1, expectedBody }
        });

        engine.Dispose();
        using var reloadedEngine = new DbEngine(_databasePath);
        var rows = Assert.IsType<List<Row>>(
            reloadedEngine.Execute(Parser.Parse("SELECT * FROM documents")));

        var row = Assert.Single(rows);
        Assert.Equal(expectedBody, row.Values["body"].ToString());
        Assert.True(new FileInfo(_databasePath).Length > Constants.DefaultPageSize * 3);
    }

    [Fact]
    public void Storage_ReloadsDataLargerThanTheClockBufferPool()
    {
        using var engine = new DbEngine(_databasePath);
        engine.Execute(Parser.Parse("CREATE TABLE documents (id INTEGER PRIMARY KEY, body TEXT)"));
        var expectedBody = new string('x', Constants.DefaultPageSize * (Constants.DefaultBufferPoolCapacity + 2));
        engine.Execute(new InsertStatement
        {
            TableName = "documents",
            Values = new List<object> { 1, expectedBody }
        });

        engine.Dispose();
        using var reloadedEngine = new DbEngine(_databasePath);
        var rows = Assert.IsType<List<Row>>(
            reloadedEngine.Execute(Parser.Parse("SELECT * FROM documents")));

        var row = Assert.Single(rows);
        Assert.Equal(expectedBody, row.Values["body"].ToString());
        Assert.True(new FileInfo(_databasePath).Length >
            Constants.DefaultPageSize * Constants.DefaultBufferPoolCapacity);
    }

    [Fact]
    public void Execute_UsesPersistentBPlusTreeForPrimaryKeyCrud()
    {
        using var engine = new DbEngine(_databasePath);
        engine.Execute(Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)"));
        Assert.False(File.Exists($"{_databasePath}.users.pkidx"));
        for (int id = 1; id <= 70; id++)
            engine.Execute(Parser.Parse($"INSERT INTO users VALUES ({id}, 'user-{id}')"));

        var selected = Assert.IsType<List<Row>>(
            engine.Execute(Parser.Parse("SELECT * FROM users WHERE id = 37")));
        Assert.Equal("user-37", Assert.Single(selected).Values["name"].ToString());

        selected = Assert.IsType<List<Row>>(
            engine.Execute(Parser.Parse("SELECT * FROM users WHERE id > 67")));
        Assert.Equal(new[] { "user-68", "user-69", "user-70" },
            selected.Select(row => row.Values["name"].ToString()));

        engine.Execute(Parser.Parse("UPDATE users SET id = 170 WHERE id = 70"));
        selected = Assert.IsType<List<Row>>(
            engine.Execute(Parser.Parse("SELECT * FROM users WHERE id = 170")));
        Assert.Equal("user-70", Assert.Single(selected).Values["name"].ToString());

        engine.Execute(Parser.Parse("DELETE FROM users WHERE id = 170"));
        Assert.Empty(Assert.IsType<List<Row>>(
            engine.Execute(Parser.Parse("SELECT * FROM users WHERE id = 170"))));

        engine.Dispose();
        using var reloadedEngine = new DbEngine(_databasePath);
        selected = Assert.IsType<List<Row>>(
            reloadedEngine.Execute(Parser.Parse("SELECT * FROM users WHERE id = 37")));
        Assert.Equal("user-37", Assert.Single(selected).Values["name"].ToString());
        Assert.Throws<Exception>(() =>
            reloadedEngine.Execute(Parser.Parse("INSERT INTO users VALUES (37, 'duplicate')")));
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

    }
}