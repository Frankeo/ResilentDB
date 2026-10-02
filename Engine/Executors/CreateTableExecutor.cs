using Engine;

namespace Engine.Executors;

public sealed class CreateTableExecutor : ExecutorBase
{
    public override bool CanExecute(Statement statement) => statement is CreateTableStatement;

    public override object Execute(DbEngine engine, Statement statement)
    {
        var stmt = (CreateTableStatement)statement;

        if (engine.Schema.Tables.ContainsKey(stmt.TableName))
            throw new Exception(string.Format(Constants.TableAlreadyExistsError, stmt.TableName));

        var tableDef = new TableDef
        {
            Name = stmt.TableName,
            Columns = stmt.Columns
        };

        engine.Schema.Tables[stmt.TableName] = tableDef;
        engine.Tables[stmt.TableName] = new List<Row>();

        engine.Save();
        return "Tabla creada";
    }
}
