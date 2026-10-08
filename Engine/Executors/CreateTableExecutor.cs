using Engine;

namespace Engine.Executors;

public sealed class CreateTableExecutor : ExecutorBase
{
    public override bool CanExecute(Statement statement) => statement is CreateTableStatement;

    public override ExecutionResult Execute(IExecutionContext engine, Statement statement)
    {
        var stmt = (CreateTableStatement)statement;
        Trace(engine, "CreateTableStarted", $"table={stmt.TableName}, columns={stmt.Columns.Count}");

        if (engine.Schema.Tables.ContainsKey(stmt.TableName))
            throw new Exception(string.Format(Constants.TableAlreadyExistsError, stmt.TableName));

        var tableDef = new TableDef
        {
            Name = stmt.TableName,
            Columns = stmt.Columns
        };

        foreach (var column in tableDef.Columns)
            Trace(engine, "ColumnDefined", $"table={stmt.TableName}, name={column.Name}, type={column.Type}, primaryKey={column.IsPrimaryKey}", 1);

        engine.Schema.Tables[stmt.TableName] = tableDef;
        Trace(engine, "PrimaryIndexCreationStarted", $"table={stmt.TableName}");
        engine.CreatePrimaryIndex(stmt.TableName);
        engine.Save();
        Trace(engine, "CreateTableCompleted", $"table={stmt.TableName}");
        return new CommandResult(0, Constants.TableCreatedMessage);
    }
}
