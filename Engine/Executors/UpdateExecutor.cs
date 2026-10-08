using Engine;

namespace Engine.Executors;

public sealed class UpdateExecutor : ExecutorBase
{
    public override bool CanExecute(Statement statement) => statement is UpdateStatement;

    public override ExecutionResult Execute(IExecutionContext engine, Statement statement)
    {
        var update = (UpdateStatement)statement;
        Trace(engine, "UpdateStarted", $"table={update.TableName}, where={FormatWhere(update.Where)}");
        foreach (var assignment in update.Assignments)
            Trace(engine, "FieldAssignment", $"field={assignment.Key}, newValue={FormatValue(assignment.Value)}", 1);
        if (!engine.Schema.Tables.TryGetValue(update.TableName, out var table))
            throw new Exception(string.Format(Constants.TableNotFoundError, update.TableName));

        foreach (var column in update.Assignments.Keys)
        {
            if (!table.Columns.Any(definition => definition.Name == column))
                throw new Exception(string.Format(Constants.ColumnNotFoundError, column));
        }

        ValidateWhereClause(table, update.Where);

        var affectedRows = FindMatchingRows(engine, update.TableName, table, update.Where);
        Trace(engine, "RowsMatched", $"count={affectedRows.Count}");
        var primaryKey = table.Columns.SingleOrDefault(column => column.IsPrimaryKey);
        if (primaryKey is null)
            throw new Exception(Constants.PrimaryKeyRequiredError);

        var index = engine.OpenPrimaryIndex(update.TableName);
        bool changesPrimaryKey = update.Assignments.TryGetValue(primaryKey.Name, out var newKey);
        if (changesPrimaryKey && newKey is not int)
            throw new Exception(Constants.PrimaryKeyMustBeIntegerError);

        if (changesPrimaryKey && affectedRows.Count > 1)
            throw new Exception(Constants.UpdateDuplicatePrimaryKeyError);

        if (changesPrimaryKey && affectedRows.Count == 1)
        {
            var row = affectedRows[0];
            if (!TryConvertPrimaryKey(row.Values[primaryKey.Name], out var oldKey))
                throw new InvalidDataException(Constants.InvalidFileError);

            long updatedKey = Convert.ToInt64(newKey);
            if (updatedKey != oldKey)
            {
                if (index.ContainsKey(updatedKey))
                    throw new Exception(Constants.UpdateDuplicatePrimaryKeyError);

                Trace(engine, "PrimaryKeyChange", $"delete oldKey={oldKey}, insert newKey={updatedKey}");
                index.Delete(oldKey);
                foreach (var assignment in update.Assignments)
                {
                    Trace(engine, "FieldUpdated", $"key={updatedKey}, field={assignment.Key}, oldValue={FormatValue(row.Values.GetValueOrDefault(assignment.Key))}, newValue={FormatValue(assignment.Value)}", 1);
                    row.Values[assignment.Key] = assignment.Value;
                }
                index.InsertRecord(updatedKey, row);
            }
        }

        foreach (var row in affectedRows)
        {
            if (changesPrimaryKey && affectedRows.Count == 1)
            {
                if (TryConvertPrimaryKey(row.Values[primaryKey.Name], out var currentKey) &&
                    update.Assignments.TryGetValue(primaryKey.Name, out var value) &&
                    TryConvertPrimaryKey(value, out var targetKey) && currentKey == targetKey)
                {
                    foreach (var assignment in update.Assignments)
                    {
                        Trace(engine, "FieldUpdated", $"key={targetKey}, field={assignment.Key}, oldValue={FormatValue(row.Values.GetValueOrDefault(assignment.Key))}, newValue={FormatValue(assignment.Value)}", 1);
                        row.Values[assignment.Key] = assignment.Value;
                    }
                    index.UpdateRecord(targetKey, row);
                }

                continue;
            }

            foreach (var assignment in update.Assignments)
            {
                Trace(engine, "FieldUpdated", $"key={row.Values.GetValueOrDefault(primaryKey.Name)}, field={assignment.Key}, oldValue={FormatValue(row.Values.GetValueOrDefault(assignment.Key))}, newValue={FormatValue(assignment.Value)}", 1);
                row.Values[assignment.Key] = assignment.Value;
            }

            if (TryConvertPrimaryKey(row.Values[primaryKey.Name], out var key))
                index.UpdateRecord(key, row);
        }

        Trace(engine, "Persist", "saving dirty pages and catalog");
        engine.Save();
        return new CommandResult(
            affectedRows.Count,
            string.Format(Constants.UpdatedRowsMessage, affectedRows.Count));
    }
}