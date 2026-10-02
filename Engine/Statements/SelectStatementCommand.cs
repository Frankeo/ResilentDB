using System.Text.RegularExpressions;
using Engine;

namespace Engine.Statements;

public sealed class SelectStatementCommand : StatementCommandBase
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
            : colsStr.Split(',').Select(c => c.Trim()).ToList();

        WhereClause? where = null;
        if (whereStr != null)
        {
            var wm = Regex.Match(whereStr, @"(\w+)\s*(=|>|<|>=|<=)\s*(.+)");
            if (!wm.Success)
                    throw new Exception(Constants.UnsupportedWhereError);

            var col = wm.Groups[1].Value;
            var op = wm.Groups[2].Value;
            var valStr = wm.Groups[3].Value.Trim();

            where = new WhereClause
            {
                Column = col,
                Op = op,
                Value = ParseValue(valStr)
            };
        }

        return new SelectStatement
        {
            TableName = tableName,
            Columns = columns,
            Where = where
        };
    }
}
