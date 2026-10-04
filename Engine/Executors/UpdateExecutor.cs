using Engine;

namespace Engine.Executors;

public sealed class UpdateExecutor : ExecutorBase
{
    public override bool CanExecute(Statement statement) => statement is UpdateStatement;

    public override object Execute(DbEngine engine, Statement statement)
    {
        var update = (UpdateStatement)statement;
        if (!engine.Schema.Tables.TryGetValue(update.TableName, out var table))
            throw new Exception(string.Format(Constants.TableNotFoundError, update.TableName));

        foreach (var column in update.Assignments.Keys)
        {
            if (!table.Columns.Any(definition => definition.Name == column))
                throw new Exception(string.Format(Constants.ColumnNotFoundError, column));
        }

        ValidateWhereClause(table, update.Where);

        var rows = engine.Tables[update.TableName];
        var affectedRows = rows.Where(row => MatchesWhere(row, update.Where)).ToList();
        var primaryKey = table.Columns.SingleOrDefault(column => column.IsPrimaryKey);
        if (primaryKey is not null && update.Assignments.TryGetValue(primaryKey.Name, out var newKey))
        {
            if (newKey is not int)
                throw new Exception(Constants.PrimaryKeyMustBeIntegerError);

            if (affectedRows.Count > 1 || rows.Except(affectedRows).Any(row =>
                    ValuesEqual(row.Values[primaryKey.Name], newKey)))
                throw new Exception(Constants.UpdateDuplicatePrimaryKeyError);
        }

        foreach (var row in affectedRows)
        {
            foreach (var assignment in update.Assignments)
                row.Values[assignment.Key] = assignment.Value;
        }

        engine.Save();
        return string.Format(Constants.UpdatedRowsMessage, affectedRows.Count);
    }
}