using Engine;
using Engine.BufferPool;
using Xunit;

namespace UnitTests;

public sealed class BufferPoolTests : EngineTestBase
{
    [Fact]
    public void PagerAllocatesAndReadsFixedSizePages()
    {
        using var pager = new Pager(DatabasePath, pageSize: 64);

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
        Assert.Equal(128, new FileInfo(DatabasePath).Length);
    }

    [Fact]
    public void PagerRejectsHeaderPageAndOversizedWrites()
    {
        using var pager = new Pager(DatabasePath, pageSize: 64);

        Assert.Throws<ArgumentOutOfRangeException>(() => pager.ReadPage(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => pager.WritePage(0, Array.Empty<byte>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => pager.WritePage(1, new byte[] { 1 }));
        int pageId = pager.AllocatePage();
        Assert.Throws<ArgumentException>(() => pager.WritePage(pageId, new byte[65]));
    }

    [Fact]
    public void LruEvictsLeastRecentlyUsedAndPersistsDirtyPages()
    {
        int firstPageId;
        int secondPageId;
        int thirdPageId;

        using (var store = new LruBufferPool(DatabasePath, capacity: 2))
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

        using var pager = new Pager(DatabasePath);
        Assert.Equal(1, pager.ReadPage(firstPageId)[0]);
        Assert.Equal(2, pager.ReadPage(secondPageId)[0]);
        Assert.Equal(3, pager.ReadPage(thirdPageId)[0]);
    }

    [Fact]
    public void LruSerializesConcurrentAllocationsAndWrites()
    {
        const int pageCount = 32;
        var pageIds = new int[pageCount];

        using (var bufferPool = new LruBufferPool(DatabasePath, capacity: 4))
        {
            Parallel.For(0, pageCount, index => pageIds[index] = bufferPool.AllocatePage());
            Assert.Equal(pageCount + 1, bufferPool.PageCount);
            Assert.Equal(pageCount, pageIds.Distinct().Count());

            Parallel.For(0, pageCount, index =>
                bufferPool.WritePage(pageIds[index], new[] { (byte)(index + 1) }));

            Assert.True(bufferPool.CachedPageCount <= bufferPool.Capacity);
            bufferPool.Flush();
        }

        using var pager = new Pager(DatabasePath);
        for (var index = 0; index < pageCount; index++)
            Assert.Equal(index + 1, pager.ReadPage(pageIds[index])[0]);
    }

    [Fact]
    public void FullCacheLoadsAllPagesAndFlushesDirtyData()
    {
        int pageId;
        using (var pager = new Pager(DatabasePath))
        {
            pageId = pager.AllocatePage();
            pager.WritePage(pageId, new byte[] { 1 });
        }

        using (var store = new FullCacheBufferPool(DatabasePath))
        {
            Assert.Equal(store.PageCount - 1, store.CachedPageCount);
            Assert.Equal(1, store.ReadPage(pageId)[0]);
            store.WritePage(pageId, new byte[] { 7 });
            store.Flush();
        }

        using var reloadedPager = new Pager(DatabasePath);
        Assert.Equal(7, reloadedPager.ReadPage(pageId)[0]);
    }

    [Fact]
    public void ClockGivesSecondChanceAndPersistsDirtyEvictions()
    {
        int firstPageId;
        int secondPageId;
        int thirdPageId;
        using (var pager = new Pager(DatabasePath))
        {
            firstPageId = pager.AllocatePage();
            secondPageId = pager.AllocatePage();
            thirdPageId = pager.AllocatePage();
            pager.WritePage(firstPageId, new byte[] { 1 });
            pager.WritePage(secondPageId, new byte[] { 2 });
            pager.WritePage(thirdPageId, new byte[] { 3 });
        }

        using (var bufferPool = new ClockBufferPool(DatabasePath, capacity: 2))
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

        using var verifyPager = new Pager(DatabasePath);
        Assert.Equal(1, verifyPager.ReadPage(firstPageId)[0]);
        Assert.Equal(22, verifyPager.ReadPage(secondPageId)[0]);
        Assert.Equal(3, verifyPager.ReadPage(thirdPageId)[0]);
    }

    [Fact]
    public void ClockDoesNotEvictPinnedPages()
    {
        int firstPageId;
        int secondPageId;
        using (var pager = new Pager(DatabasePath))
        {
            firstPageId = pager.AllocatePage();
            secondPageId = pager.AllocatePage();
            pager.WritePage(firstPageId, new byte[] { 1 });
            pager.WritePage(secondPageId, new byte[] { 2 });
        }

        using (var bufferPool = new ClockBufferPool(DatabasePath, capacity: 1))
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

        using var verifyPager = new Pager(DatabasePath);
        Assert.Equal(9, verifyPager.ReadPage(firstPageId)[0]);
    }

    [Fact]
    public void ClockPageHandleUnpinsAndPersistsDirtyData()
    {
        int pageId;
        using (var pager = new Pager(DatabasePath))
        {
            pageId = pager.AllocatePage();
            pager.WritePage(pageId, new byte[] { 1 });
        }

        using (var bufferPool = new ClockBufferPool(DatabasePath, capacity: 1))
        {
            using (var handle = bufferPool.FetchPageHandle(pageId))
            {
                handle.Data[0] = 7;
                handle.MarkDirty();
                Assert.Throws<InvalidOperationException>(() => bufferPool.Dispose());
            }

            Assert.Equal(7, bufferPool.ReadPage(pageId)[0]);
        }

        var disposedPool = new ClockBufferPool(DatabasePath);
        disposedPool.Dispose();
        disposedPool.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposedPool.ReadPage(pageId));
    }

    [Fact]
    public void PagerDisposePreventsReadsEvenWhenHeaderWasCached()
    {
        var pager = new Pager(DatabasePath);
        Assert.Equal(1, pager.ReadHeader().PageCount);
        pager.Dispose();
        pager.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pager.ReadHeader());
        Assert.Throws<ObjectDisposedException>(() => pager.AllocatePage());
    }

    [Fact]
    public void ClockSerializesConcurrentPageAccess()
    {
        var pageIds = new int[8];
        using (var pager = new Pager(DatabasePath))
        {
            for (var index = 0; index < pageIds.Length; index++)
                pageIds[index] = pager.AllocatePage();
        }

        using (var bufferPool = new ClockBufferPool(DatabasePath, capacity: 3))
        {
            Parallel.For(0, pageIds.Length, index =>
                bufferPool.WritePage(pageIds[index], new[] { (byte)(index + 1) }));

            Assert.True(bufferPool.CachedPageCount <= bufferPool.Capacity);
            bufferPool.Flush();
        }

        using var verifyPager = new Pager(DatabasePath);
        for (var index = 0; index < pageIds.Length; index++)
            Assert.Equal(index + 1, verifyPager.ReadPage(pageIds[index])[0]);
    }
}