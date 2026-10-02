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

        engine.Tables[stmt.TableName].Add(row);
        engine.Save();
        return Constants.InsertSuccessMessage;
    }
}
