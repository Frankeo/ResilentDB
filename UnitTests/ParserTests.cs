using Engine;
using Xunit;

namespace UnitTests;

public sealed class ParserTests : EngineTestBase
{
    [Fact]
    public void CreateTable_ReturnsTableAndColumns()
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
    public void CreateTable_RequiresPrimaryKey()
    {
        Assert.Throws<Exception>(() =>
            Parser.Parse("CREATE TABLE users (id INTEGER, name TEXT)"));
    }

    [Fact]
    public void CreateTable_MarksIntegerPrimaryKey()
    {
        var statement = Assert.IsType<CreateTableStatement>(
            Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)"));

        Assert.True(statement.Columns[0].IsPrimaryKey);
        Assert.False(statement.Columns[1].IsPrimaryKey);
    }

    [Fact]
    public void Insert_PreservesCommasInsideQuotedValues()
    {
        var statement = Assert.IsType<InsertStatement>(
            Parser.Parse("INSERT INTO users VALUES (1, 'Smith, Jane')"));

        Assert.Equal("users", statement.TableName);
        Assert.Equal(new object[] { 1, "Smith, Jane" }, statement.Values);
    }

    [Fact]
    public void Select_ParsesProjectionAndWhereClause()
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
}