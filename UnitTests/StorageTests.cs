using Engine;
using BufferPoolConstants = Engine.BufferPool.Constants;
using PagerConstants = Engine.PagerConfig.Constants;
using Xunit;

namespace UnitTests;

public sealed class StorageTests : EngineTestBase
{
    [Fact]
    public void ReloadsDataSpanningMultiplePages()
    {
        using var engine = new DbEngine(DatabasePath);
        engine.Execute(Parser.Parse("CREATE TABLE documents (id INTEGER PRIMARY KEY, body TEXT)"));
        var expectedBody = new string('x', PagerConstants.DefaultPageSize * 3);
        engine.Execute(new InsertStatement
        {
            TableName = "documents",
            Values = new List<object> { 1, expectedBody }
        });

        engine.Dispose();
        using var reloadedEngine = new DbEngine(DatabasePath);
        var row = Assert.Single(ReadRows(
            reloadedEngine.Execute(Parser.Parse("SELECT * FROM documents"))));

        Assert.Equal(expectedBody, row.Values["body"].ToString());
        Assert.True(new FileInfo(DatabasePath).Length > PagerConstants.DefaultPageSize * 3);
    }

    [Fact]
    public void ReloadsDataLargerThanTheClockBufferPool()
    {
        using var engine = new DbEngine(DatabasePath);
        engine.Execute(Parser.Parse("CREATE TABLE documents (id INTEGER PRIMARY KEY, body TEXT)"));
        var expectedBody = new string('x', PagerConstants.DefaultPageSize * (BufferPoolConstants.DefaultBufferPoolCapacity + 2));
        engine.Execute(new InsertStatement
        {
            TableName = "documents",
            Values = new List<object> { 1, expectedBody }
        });

        engine.Dispose();
        using var reloadedEngine = new DbEngine(DatabasePath);
        var row = Assert.Single(ReadRows(
            reloadedEngine.Execute(Parser.Parse("SELECT * FROM documents"))));

        Assert.Equal(expectedBody, row.Values["body"].ToString());
        Assert.True(new FileInfo(DatabasePath).Length >
            PagerConstants.DefaultPageSize * BufferPoolConstants.DefaultBufferPoolCapacity);
    }
}