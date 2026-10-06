using Engine;
using CliConstants = ResilentDB.Cli.Constants;

using var engine = new DbEngine(CliConstants.DatabaseFileName);

Console.WriteLine(CliConstants.StartupBanner);
Console.WriteLine(CliConstants.CliCommands);
Console.WriteLine(CliConstants.CliExitInstructions);

while (true)
{
    Console.Write(CliConstants.CliPrompt);
    var line = Console.ReadLine();
    if (line == null) break;
    line = line.Trim();
    if (line.Length == 0) continue;
    if (line == CliConstants.CliExitCommand)
        break;

    try
    {
        if (line.Equals(CliConstants.CliReadCommand, StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith(CliConstants.CliReadCommand + " ", StringComparison.OrdinalIgnoreCase))
        {
            var sqlFilePath = line[CliConstants.CliReadCommand.Length..].Trim();
            if (sqlFilePath.Length == 0)
                throw new ArgumentException(CliConstants.CliReadUsageError);

            if ((sqlFilePath.StartsWith('"') && sqlFilePath.EndsWith('"')) ||
                (sqlFilePath.StartsWith('\'') && sqlFilePath.EndsWith('\'')))
            {
                sqlFilePath = sqlFilePath[1..^1];
            }

            line = File.ReadAllText(sqlFilePath);
        }

        foreach (var res in engine.Execute(line))
        {
            if (res is QueryResult query)
            {
                if (query.Rows.Count == 0)
                {
                    Console.WriteLine(CliConstants.CliEmptyResult);
                }
                else
                {
                    var cols = query.Columns;
                    Console.WriteLine(string.Join(CliConstants.CliColumnSeparator, cols));
                    Console.WriteLine(string.Join(
                        CliConstants.CliRuleSeparator,
                        cols.Select(c => new string(CliConstants.CliRuleCharacter, c.Length + CliConstants.CliColumnPadding))));

                    foreach (var row in query.Rows)
                    {
                        var vals = row.Select(value => value?.ToString() ?? CliConstants.CliNullValue);
                        Console.WriteLine(string.Join(CliConstants.CliColumnSeparator, vals));
                    }
                }
            }
            else if (res is CommandResult command)
            {
                Console.WriteLine(command.Message);
            }
            else
            {
                Console.WriteLine(res);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(CliConstants.CliErrorPrefix + ex.Message);
    }
}