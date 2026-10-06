namespace Engine.Executors;

public interface IExecutor
{
    bool CanExecute(Statement statement);
    ExecutionResult Execute(IExecutionContext engine, Statement statement);
}
