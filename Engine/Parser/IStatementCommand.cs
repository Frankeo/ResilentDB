using Engine;

namespace Engine.Parsing;

public interface IStatementCommand
{
    bool CanHandle(string sql);
    Statement Parse(string sql);
}
