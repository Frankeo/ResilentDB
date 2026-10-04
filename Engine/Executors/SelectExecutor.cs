using Engine;

namespace Engine.Executors;

public sealed class SelectExecutor : ExecutorBase
{
    public override bool CanExecute(Statement statement) => statement is SelectStatement;

    public override object Execute(DbEngine engine, Statement statement)
    {
        var stmt = (SelectStatement)statement;

        if (!engine.Schema.Tables.TryGetValue(stmt.TableName, out var table))
            throw new Exception(string.Format(Constants.TableNotFoundError, stmt.TableName));

        ValidateWhereClause(table, stmt.Where);

        var rows = FindMatchingRows(engine, stmt.TableName, table, stmt.Where);

        if (stmt.Columns.Count == 1 && stmt.Columns[0] == "*")
        {
            return rows;
        }

        return rows
            .Select(r =>
            {
                var newRow = new Row();
                foreach (var col in stmt.Columns)
                {
                    newRow.Values[col] = r.Values[col];
                }
                return newRow;
            })
            .ToList();
    }
}
