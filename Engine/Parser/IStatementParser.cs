using Engine;

namespace Engine.Parsing;

public interface IStatementParser
{
    bool CanHandle(string sql);
    Statement Parse(string sql);
}
