using System.Text.RegularExpressions;
using Engine;

namespace Engine.Parsing;

public abstract class StatementParserBase : IStatementParser
{
    public abstract bool CanHandle(string sql);
    public abstract Statement Parse(string sql);

    protected static IEnumerable<string> SplitValues(string valsStr)
    {
        var result = new List<string>();
        var current = "";
        var inQuotes = false;

        foreach (var c in valsStr)
        {
            if (c == Constants.SqlStringDelimiter && !inQuotes)
            {
                inQuotes = true;
                current += c;
            }
            else if (c == Constants.SqlStringDelimiter && inQuotes)
            {
                inQuotes = false;
                current += c;
            }
            else if (c == Constants.SqlValueSeparator && !inQuotes)
            {
                result.Add(current.Trim());
                current = "";
            }
            else
            {
                current += c;
            }
        }

        if (!string.IsNullOrEmpty(current))
            result.Add(current.Trim());

        return result;
    }

    protected static object ParseValue(string v)
    {
        if (v.Length >= 2 &&
            v[0] == Constants.SqlStringDelimiter &&
            v[^1] == Constants.SqlStringDelimiter)
            return v.Substring(1, v.Length - 2);

        if (int.TryParse(v, out var i))
            return i;

        return v;
    }

    protected static WhereClause ParseWhereClause(string whereSql)
    {
        var match = Regex.Match(
            whereSql,
            @"^(\w+)\s*(>=|<=|=|>|<)\s*(.+)$",
            RegexOptions.Singleline);

        if (!match.Success)
            throw new Exception(Constants.UnsupportedWhereError);

        return new WhereClause
        {
            Column = match.Groups[1].Value,
            Op = match.Groups[2].Value,
            Value = ParseValue(match.Groups[3].Value.Trim())
        };
    }
}
