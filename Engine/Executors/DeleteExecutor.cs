using Engine;

namespace Engine.Executors;

public sealed class DeleteExecutor : ExecutorBase
{
    public override bool CanExecute(Statement statement) => statement is DeleteStatement;

    public override ExecutionResult Execute(IExecutionContext engine, Statement statement)
    {
        var delete = (DeleteStatement)statement;
        Trace(engine, "DeleteStarted", $"table={delete.TableName}, where={FormatWhere(delete.Where)}");
        if (!engine.Schema.Tables.TryGetValue(delete.TableName, out var table))
            throw new Exception(string.Format(Constants.TableNotFoundError, delete.TableName));

        ValidateWhereClause(table, delete.Where);
        var rowsToDelete = FindMatchingRows(engine, delete.TableName, table, delete.Where);
        Trace(engine, "RowsMatched", $"count={rowsToDelete.Count}");
        var primaryKey = table.Columns.SingleOrDefault(column => column.IsPrimaryKey);
        if (primaryKey is null)
            throw new Exception(Constants.PrimaryKeyRequiredError);

        var index = engine.OpenPrimaryIndex(delete.TableName);
        foreach (var row in rowsToDelete)
        {
            if (TryConvertPrimaryKey(row.Values[primaryKey.Name], out var key))
            {
                var fields = string.Join(", ", row.Values.Select(value =>
                    $"{value.Key}={FormatValue(value.Value)}"));
                Trace(engine, "RowDelete", $"key={key}, fields=[{fields}]");
                index.Delete(key);
            }
        }

        Trace(engine, "Persist", "saving dirty pages and catalog");
        engine.Save();
        return new CommandResult(
            rowsToDelete.Count,
            string.Format(Constants.DeletedRowsMessage, rowsToDelete.Count));
    }

}