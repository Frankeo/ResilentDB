using System.Text.RegularExpressions;
using Engine;

namespace Engine.Statements;

public abstract class StatementCommandBase : IStatementCommand
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
            if (c == '\'' && !inQuotes)
            {
                inQuotes = true;
                current += c;
            }
            else if (c == '\'' && inQuotes)
            {
                inQuotes = false;
                current += c;
            }
            else if (c == ',' && !inQuotes)
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
        if (v.StartsWith("'") && v.EndsWith("'"))
            return v.Substring(1, v.Length - 2);

        if (int.TryParse(v, out var i))
            return i;

        return v;
    }
}
