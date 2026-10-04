using System.Text.RegularExpressions;
using Engine;

namespace Engine.Parsing;

public sealed class CreateTableStatementParser : StatementParserBase
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
        foreach (var parte in colsStr.Split(Constants.SqlValueSeparator))
        {
            var p = parte.Trim();
            if (string.IsNullOrEmpty(p)) continue;

            var parts = p.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts.Length == 3 || parts.Length > 4 ||
                (parts.Length == 4 &&
                 (!parts[2].Equals("PRIMARY", StringComparison.OrdinalIgnoreCase) ||
                  !parts[3].Equals("KEY", StringComparison.OrdinalIgnoreCase))))
                throw new Exception(string.Format(Constants.InvalidColumnDefinitionError, p));

            columns.Add(new ColumnDef
            {
                Name = parts[0],
                Type = parts[1].ToUpperInvariant(),
                IsPrimaryKey = parts.Length == 4
            });
        }

        var primaryKeys = columns.Where(column => column.IsPrimaryKey).ToList();
        if (primaryKeys.Count == 0)
            throw new Exception(Constants.PrimaryKeyRequiredError);

        if (primaryKeys.Count > 1)
            throw new Exception(Constants.MultiplePrimaryKeysError);

        if (primaryKeys[0].Type != "INTEGER")
            throw new Exception(Constants.PrimaryKeyMustBeIntegerError);

        return new CreateTableStatement
        {
            TableName = tableName,
            Columns = columns
        };
    }
}
