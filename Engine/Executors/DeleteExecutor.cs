using Engine;

namespace Engine.Executors;

public sealed class DeleteExecutor : ExecutorBase
{
    public override bool CanExecute(Statement statement) => statement is DeleteStatement;

    public override ExecutionResult Execute(IExecutionContext engine, Statement statement)
    {
        var delete = (DeleteStatement)statement;
        if (!engine.Schema.Tables.TryGetValue(delete.TableName, out var table))
            throw new Exception(string.Format(Constants.TableNotFoundError, delete.TableName));

        ValidateWhereClause(table, delete.Where);
        var rowsToDelete = FindMatchingRows(engine, delete.TableName, table, delete.Where);
        var primaryKey = table.Columns.SingleOrDefault(column => column.IsPrimaryKey);
        if (primaryKey is null)
            throw new Exception(Constants.PrimaryKeyRequiredError);

        var index = engine.OpenPrimaryIndex(delete.TableName);
        foreach (var row in rowsToDelete)
        {
            if (TryConvertPrimaryKey(row.Values[primaryKey.Name], out var key))
                index.Delete(key);
        }

        engine.Save();
        return new CommandResult(
            rowsToDelete.Count,
            string.Format(Constants.DeletedRowsMessage, rowsToDelete.Count));
    }

}