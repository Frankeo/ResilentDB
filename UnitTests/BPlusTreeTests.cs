using Engine;
using Engine.BufferPool;
using PagerConstants = Engine.PagerConfig.Constants;
using Xunit;

namespace UnitTests;

public sealed class BPlusTreeTests : EngineTestBase
{
    [Fact]
    public void SplitsScansDeletesAndReloads()
    {
        var indexPath = $"{DatabasePath}.tree.idx";
        try
        {
            using (var tree = new BPlusTree(indexPath, maxKeys: 3, bufferPoolCapacity: 2))
            {
                foreach (long key in Enumerable.Range(0, 64)
                             .OrderBy(value => (value * 37) % 64)
                             .Select(value => (long)value + 1))
                    tree.Insert(key, key * 10);

                Assert.True(tree.RootPageId.Value > 2);
                Assert.True(tree.TryGetValue(17, out var value));
                Assert.Equal(170, value);
                Assert.Throws<InvalidOperationException>(() => tree.Insert(17, 999));
                Assert.Equal(new long[] { 20, 21, 22 }, tree.Scan(20, 22).Select(entry => entry.Key));

                for (long key = 2; key <= 64; key += 2)
                    Assert.True(tree.Delete(key));

                Assert.False(tree.Delete(2));
                tree.Validate();
                Assert.Equal(Enumerable.Range(1, 64).Where(key => key % 2 == 1),
                    tree.Scan().Select(entry => (int)entry.Key));
            }

            using var reloaded = new BPlusTree(indexPath, maxKeys: 32, bufferPoolCapacity: 2);
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
    public void StoresRowsInlineAndInOverflowPages()
    {
        var indexPath = $"{DatabasePath}.row-tree.idx";
        try
        {
            var largeBody = new string('x', PagerConstants.DefaultPageSize * 2);
            using (var tree = new BPlusTree(indexPath, maxKeys: 3, bufferPoolCapacity: 2))
            {
                tree.InsertRecord(1, new Row
                {
                    Values = new Dictionary<string, object> { ["id"] = 1, ["name"] = "inline" }
                });
                tree.InsertRecord(2, new Row
                {
                    Values = new Dictionary<string, object> { ["id"] = 2, ["body"] = largeBody }
                });

                Assert.True(tree.TryGetRecord(1, out var inlineRow));
                Assert.Equal("inline", inlineRow!.Values["name"].ToString());
                Assert.True(tree.TryGetRecord(2, out var overflowRow));
                Assert.Equal(largeBody, overflowRow!.Values["body"].ToString());
                tree.Validate();
            }

            using var reloaded = new BPlusTree(indexPath, maxKeys: 3, bufferPoolCapacity: 2);
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
    public void StoresManyMediumRowsWithoutExhaustingLeafPage()
    {
        var indexPath = $"{DatabasePath}.medium-rows.idx";
        try
        {
            using (var tree = new BPlusTree(indexPath, maxKeys: 32, bufferPoolCapacity: 2))
            {
                for (long id = 1; id <= 30; id++)
                {
                    tree.InsertRecord(id, new Row
                    {
                        Values = new Dictionary<string, object>
                        {
                            ["id"] = id,
                            ["message"] = $"Pager test event {id:D3}: {new string('x', 180)}"
                        }
                    });
                }

                tree.Validate();
                Assert.Equal(30, tree.Count);
                Assert.True(tree.TryGetRecord(25, out var row));
                Assert.EndsWith(new string('x', 180), row!.Values["message"].ToString());
            }

            using var reloaded = new BPlusTree(indexPath, maxKeys: 32, bufferPoolCapacity: 2);
            reloaded.Validate();
            Assert.Equal(30, reloaded.Count);
            Assert.True(reloaded.TryGetRecord(30, out _));
        }
        finally
        {
            if (File.Exists(indexPath))
                File.Delete(indexPath);
        }
    }

    [Fact]
    public void RebalancesAndReusesFreedPages()
    {
        var indexPath = $"{DatabasePath}.rebalance.idx";
        try
        {
            using var tree = new BPlusTree(indexPath, maxKeys: 3, bufferPoolCapacity: 2);
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
    public void MatchesRandomizedSortedDictionaryOperations()
    {
        var indexPath = $"{DatabasePath}.random-tree.idx";
        var expected = new SortedDictionary<long, long>();
        var random = new Random(84521);
        try
        {
            using (var tree = new BPlusTree(indexPath, maxKeys: 3, bufferPoolCapacity: 2))
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
                                expected.Where(pair => pair.Key >= key && pair.Key <= maximum).Select(pair => pair.Key),
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

            using var reloaded = new BPlusTree(indexPath, maxKeys: 3, bufferPoolCapacity: 2);
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
    public void ReusesOverflowPagesAfterDelete()
    {
        var indexPath = $"{DatabasePath}.overflow.idx";
        try
        {
            var largeBody = new string('x', PagerConstants.DefaultPageSize * 2);
            using var tree = new BPlusTree(indexPath, maxKeys: 3, bufferPoolCapacity: 2);
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
}