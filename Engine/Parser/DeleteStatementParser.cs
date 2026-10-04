using System.Text.RegularExpressions;
using Engine;

namespace Engine.Parsing;

public sealed class DeleteStatementParser : StatementParserBase
{
    public override bool CanHandle(string sql) =>
        sql.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase);

    public override Statement Parse(string sql)
    {
        var match = Regex.Match(
            sql,
            @"^DELETE\s+FROM\s+(\w+)(?:\s+WHERE\s+(.+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!match.Success)
            throw new Exception(Constants.InvalidDeleteError);

        return new DeleteStatement
        {
            TableName = match.Groups[1].Value,
            Where = match.Groups[2].Success
                ? ParseWhereClause(match.Groups[2].Value.Trim())
                : null
        };
    }
}