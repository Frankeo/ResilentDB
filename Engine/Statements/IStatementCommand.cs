using Engine;

namespace Engine.Statements;

public interface IStatementCommand
{
    bool CanHandle(string sql);
    Statement Parse(string sql);
}
