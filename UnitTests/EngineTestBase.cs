using Engine;
using Xunit;

namespace UnitTests;

public abstract class EngineTestBase : IDisposable
{
    protected string DatabasePath { get; } = Path.Combine(
        Path.GetTempPath(),
        $"ResilentDB-{Guid.NewGuid():N}.mdb");

    protected static List<Row> ReadRows(ExecutionResult result)
    {
        var query = Assert.IsType<QueryResult>(result);
        return query.Rows.Select(values =>
        {
            var row = new Row();
            for (int columnIndex = 0; columnIndex < query.Columns.Count; columnIndex++)
                row.Values[query.Columns[columnIndex]] = values[columnIndex]!;
            return row;
        }).ToList();
    }

    public void Dispose()
    {
        if (File.Exists(DatabasePath))
            File.Delete(DatabasePath);
    }
}