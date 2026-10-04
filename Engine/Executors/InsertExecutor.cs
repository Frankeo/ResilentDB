using Engine;

namespace Engine.Executors;

public sealed class InsertExecutor : ExecutorBase
{
    public override bool CanExecute(Statement statement) => statement is InsertStatement;

    public override object Execute(DbEngine engine, Statement statement)
    {
        var stmt = (InsertStatement)statement;

        if (!engine.Schema.Tables.TryGetValue(stmt.TableName, out var tableDef))
            throw new Exception(string.Format(Constants.TableNotFoundError, stmt.TableName));

        var cols = tableDef.Columns.Select(c => c.Name).ToList();
        if (cols.Count != stmt.Values.Count)
            throw new Exception(Constants.IncorrectValueCountError);

        var row = new Row();
        for (int i = 0; i < cols.Count; i++)
        {
            row.Values[cols[i]] = stmt.Values[i];
        }

        var primaryKey = tableDef.Columns.SingleOrDefault(column => column.IsPrimaryKey);
        if (primaryKey is not null)
        {
            if (!row.Values.TryGetValue(primaryKey.Name, out var keyValue))
                throw new Exception(Constants.InsertMissingPrimaryKeyError);

            if (keyValue is not int)
                throw new Exception(Constants.PrimaryKeyMustBeIntegerError);

            if (engine.Tables[stmt.TableName].Any(existing =>
                    ValuesEqual(existing.Values[primaryKey.Name], keyValue)))
                throw new Exception(Constants.DuplicatePrimaryKeyError);
        }

        engine.Tables[stmt.TableName].Add(row);
        engine.Save();
        return Constants.InsertSuccessMessage;
    }
}
