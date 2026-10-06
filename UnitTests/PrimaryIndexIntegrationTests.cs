using Engine;
using Xunit;

namespace UnitTests;

public sealed class PrimaryIndexIntegrationTests : EngineTestBase
{
    [Fact]
    public void UsesPersistentBPlusTreeForPrimaryKeyCrud()
    {
        using var engine = new DbEngine(DatabasePath);
        engine.Execute(Parser.Parse("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)"));
        Assert.False(File.Exists($"{DatabasePath}.users.pkidx"));
        for (int id = 1; id <= 70; id++)
            engine.Execute(Parser.Parse($"INSERT INTO users VALUES ({id}, 'user-{id}')"));

        var selected = ReadRows(engine.Execute(Parser.Parse("SELECT * FROM users WHERE id = 37")));
        Assert.Equal("user-37", Assert.Single(selected).Values["name"].ToString());

        selected = ReadRows(engine.Execute(Parser.Parse("SELECT * FROM users WHERE id > 67")));
        Assert.Equal(new[] { "user-68", "user-69", "user-70" },
            selected.Select(row => row.Values["name"].ToString()));

        engine.Execute(Parser.Parse("UPDATE users SET id = 170 WHERE id = 70"));
        selected = ReadRows(engine.Execute(Parser.Parse("SELECT * FROM users WHERE id = 170")));
        Assert.Equal("user-70", Assert.Single(selected).Values["name"].ToString());

        engine.Execute(Parser.Parse("DELETE FROM users WHERE id = 170"));
        Assert.Empty(ReadRows(engine.Execute(Parser.Parse("SELECT * FROM users WHERE id = 170"))));

        engine.Dispose();
        using var reloadedEngine = new DbEngine(DatabasePath);
        selected = ReadRows(reloadedEngine.Execute(Parser.Parse("SELECT * FROM users WHERE id = 37")));
        Assert.Equal("user-37", Assert.Single(selected).Values["name"].ToString());
        Assert.Throws<Exception>(() =>
            reloadedEngine.Execute(Parser.Parse("INSERT INTO users VALUES (37, 'duplicate')")));
    }
}