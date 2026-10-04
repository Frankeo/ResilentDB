using Engine;

namespace Engine.Executors;

public sealed class DeleteExecutor : ExecutorBase
{
    public override bool CanExecute(Statement statement) => statement is DeleteStatement;

    public override object Execute(DbEngine engine, Statement statement)
    {
        var delete = (DeleteStatement)statement;
        if (!engine.Schema.Tables.TryGetValue(delete.TableName, out var table))
            throw new Exception(string.Format(Constants.TableNotFoundError, delete.TableName));

        ValidateWhereClause(table, delete.Where);
        var rows = engine.Tables[delete.TableName];
        var deletedCount = rows.RemoveAll(row => MatchesWhere(row, delete.Where));
        engine.Save();
        return string.Format(Constants.DeletedRowsMessage, deletedCount);
    }

}