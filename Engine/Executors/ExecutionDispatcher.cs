using Engine;

namespace Engine.Executors;

public sealed class ExecutionDispatcher
{
    private readonly IExecutor[] _executors =
    {
        new CreateTableExecutor(),
        new InsertExecutor(),
        new SelectExecutor(),
        new DeleteExecutor(),
        new UpdateExecutor()
    };

    public object Execute(DbEngine engine, Statement statement)
    {
        foreach (var executor in _executors)
        {
            if (executor.CanExecute(statement))
                return executor.Execute(engine, statement);
        }

        throw new NotSupportedException(Constants.UnsupportedCommandError);
    }
}