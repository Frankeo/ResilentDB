using Engine;

namespace Engine.Executors;

public sealed class SelectExecutor : ExecutorBase
{
    public override bool CanExecute(Statement statement) => statement is SelectStatement;

    public override ExecutionResult Execute(IExecutionContext engine, Statement statement)
    {
        var stmt = (SelectStatement)statement;

        if (!engine.Schema.Tables.TryGetValue(stmt.TableName, out var table))
            throw new Exception(string.Format(Constants.TableNotFoundError, stmt.TableName));

        ValidateWhereClause(table, stmt.Where);

        var rows = FindMatchingRows(engine, stmt.TableName, table, stmt.Where);

        var columns = stmt.Columns.Count == 1 && stmt.Columns[0] == "*"
            ? table.Columns.Select(column => column.Name).ToList()
            : stmt.Columns.ToList();
        var resultRows = rows
            .Select(row => (IReadOnlyList<object?>)columns
                .Select(column => row.Values[column])
                .ToArray())
            .ToList();

        return new QueryResult(columns, resultRows);
    }
}
