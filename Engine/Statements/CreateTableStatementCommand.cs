using System.Text.RegularExpressions;
using Engine;

namespace Engine.Statements;

public sealed class CreateTableStatementCommand : StatementCommandBase
{
    public override bool CanHandle(string sql) =>
        sql.TrimStart().StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase);

    public override Statement Parse(string sql)
    {
        var m = Regex.Match(
            sql,
            @"CREATE\s+TABLE\s+(\w+)\s*\((.*)\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        if (!m.Success)
            throw new Exception(Constants.InvalidCreateTableError);

        var tableName = m.Groups[1].Value;
        var colsStr = m.Groups[2].Value;

        var columns = new List<ColumnDef>();
        foreach (var parte in colsStr.Split(','))
        {
            var p = parte.Trim();
            if (string.IsNullOrEmpty(p)) continue;

            var parts = p.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                throw new Exception(string.Format(Constants.InvalidColumnDefinitionError, p));

            columns.Add(new ColumnDef
            {
                Name = parts[0],
                Type = parts[1].ToUpperInvariant()
            });
        }

        return new CreateTableStatement
        {
            TableName = tableName,
            Columns = columns
        };
    }
}
