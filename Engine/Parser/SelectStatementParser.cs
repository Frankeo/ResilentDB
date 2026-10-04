using System.Text.RegularExpressions;
using Engine;

namespace Engine.Parsing;

public sealed class SelectStatementParser : StatementParserBase
{
    public override bool CanHandle(string sql) =>
        sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);

    public override Statement Parse(string sql)
    {
        var m = Regex.Match(
            sql,
            @"SELECT\s+(.+?)\s+FROM\s+(\w+)(?:\s+WHERE\s+(.+))?",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        if (!m.Success)
            throw new Exception(Constants.InvalidSelectError);

        var colsStr = m.Groups[1].Value.Trim();
        var tableName = m.Groups[2].Value;
        var whereStr = m.Groups[3].Success ? m.Groups[3].Value.Trim() : null;

        var columns = colsStr == "*"
            ? new List<string> { "*" }
            : colsStr.Split(Constants.SqlValueSeparator).Select(c => c.Trim()).ToList();

        WhereClause? where = null;
        if (whereStr != null)
            where = ParseWhereClause(whereStr);

        return new SelectStatement
        {
            TableName = tableName,
            Columns = columns,
            Where = where
        };
    }
}
