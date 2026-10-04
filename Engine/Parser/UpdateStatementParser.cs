using System.Text.RegularExpressions;
using Engine;

namespace Engine.Parsing;

public sealed class UpdateStatementParser : StatementParserBase
{
    public override bool CanHandle(string sql) =>
        sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase);

    public override Statement Parse(string sql)
    {
        var match = Regex.Match(
            sql,
            @"^UPDATE\s+(\w+)\s+SET\s+(.+?)(?:\s+WHERE\s+(.+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!match.Success)
            throw new Exception(Constants.InvalidUpdateError);

        var assignments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in SplitValues(match.Groups[2].Value))
        {
            var equalsIndex = assignment.IndexOf('=');
            if (equalsIndex <= 0 || equalsIndex == assignment.Length - 1)
                throw new Exception(Constants.InvalidUpdateError);

            var column = assignment[..equalsIndex].Trim();
            var value = ParseValue(assignment[(equalsIndex + 1)..].Trim());
            if (!assignments.TryAdd(column, value))
                throw new Exception(string.Format(Constants.DuplicateAssignmentColumnError, column));
        }

        return new UpdateStatement
        {
            TableName = match.Groups[1].Value,
            Assignments = assignments,
            Where = match.Groups[3].Success
                ? ParseWhereClause(match.Groups[3].Value.Trim())
                : null
        };
    }
}