using System.Text.RegularExpressions;
using Engine;

namespace Engine.Statements;

public sealed class InsertStatementCommand : StatementCommandBase
{
    public override bool CanHandle(string sql) =>
        sql.TrimStart().StartsWith("INSERT INTO", StringComparison.OrdinalIgnoreCase);

    public override Statement Parse(string sql)
    {
        var m = Regex.Match(
            sql,
            @"INSERT\s+INTO\s+(\w+)\s+VALUES\s*\((.*)\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        if (!m.Success)
            throw new Exception("INSERT inválido");

        var tableName = m.Groups[1].Value;
        var valsStr = m.Groups[2].Value;

        var values = new List<object>();
        foreach (var v in SplitValues(valsStr))
        {
            values.Add(ParseValue(v));
        }

        return new InsertStatement
        {
            TableName = tableName,
            Values = values
        };
    }
}
