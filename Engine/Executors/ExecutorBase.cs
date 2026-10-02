using Engine;

namespace Engine.Executors;

public abstract class ExecutorBase : IExecutor
{
    public abstract bool CanExecute(Statement statement);
    public abstract object Execute(DbEngine engine, Statement statement);
}
