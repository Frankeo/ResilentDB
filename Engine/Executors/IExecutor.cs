namespace Engine.Executors;

public interface IExecutor
{
    bool CanExecute(Statement statement);
    object Execute(DbEngine engine, Statement statement);
}
