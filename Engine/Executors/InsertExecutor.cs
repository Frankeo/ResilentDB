using Engine;

namespace Engine.Executors;

public sealed class InsertExecutor : ExecutorBase
{
    public override bool CanExecute(Statement statement) => statement is InsertStatement;

    public override ExecutionResult Execute(IExecutionContext engine, Statement statement)
    {
        var stmt = (InsertStatement)statement;
        Trace(engine, "InsertStarted", $"table={stmt.TableName}, suppliedValues={stmt.Values.Count}");

        if (!engine.Schema.Tables.TryGetValue(stmt.TableName, out var tableDef))
            throw new Exception(string.Format(Constants.TableNotFoundError, stmt.TableName));

        var cols = tableDef.Columns.Select(c => c.Name).ToList();
        if (cols.Count != stmt.Values.Count)
            throw new Exception(Constants.IncorrectValueCountError);

        var row = new Row();
        for (int i = 0; i < cols.Count; i++)
        {
            row.Values[cols[i]] = stmt.Values[i];
            Trace(engine, "FieldAdded", $"table={stmt.TableName}, field={cols[i]}, value={FormatValue(stmt.Values[i])}", 1);
        }

        var primaryKey = tableDef.Columns.SingleOrDefault(column => column.IsPrimaryKey);
        if (primaryKey is null)
            throw new Exception(Constants.PrimaryKeyRequiredError);

        if (!row.Values.TryGetValue(primaryKey.Name, out var keyValue))
            throw new Exception(Constants.InsertMissingPrimaryKeyError);

        if (keyValue is not int)
            throw new Exception(Constants.PrimaryKeyMustBeIntegerError);

        long key = Convert.ToInt64(keyValue);
        Trace(engine, "PrimaryKeyResolved", $"table={stmt.TableName}, key={key}");
        var index = engine.OpenPrimaryIndex(stmt.TableName);
        Trace(engine, "DuplicateCheck", $"key={key}");
        if (index.ContainsKey(key))
            throw new Exception(Constants.DuplicatePrimaryKeyError);

        Trace(engine, "IndexInsert", $"key={key}, fields={row.Values.Count}");
        index.InsertRecord(key, row);

        Trace(engine, "Persist", "saving dirty pages and catalog");
        engine.Save();
        return new CommandResult(1, Constants.InsertSuccessMessage);
    }
}
