using Engine;

var engine = new DbEngine(Constants.DatabaseFileName);

Console.WriteLine(Constants.StartupBanner);
Console.WriteLine(Constants.CliCommands);
Console.WriteLine(Constants.CliExitInstructions);

while (true)
{
    Console.Write(Constants.CliPrompt);
    var line = Console.ReadLine();
    if (line == null) break;
    line = line.Trim();
    if (line.Length == 0) continue;
    if (line == Constants.CliExitCommand)
        break;

    try
    {
        foreach (var res in engine.Execute(line))
        {
            if (res is List<Row> rows)
            {
                if (rows.Count == 0)
                {
                    Console.WriteLine(Constants.CliEmptyResult);
                }
                else
                {
                    // Imprimir cabecera
                    var cols = rows[0].Values.Keys.ToList();
                    Console.WriteLine(string.Join(Constants.CliColumnSeparator, cols));
                    Console.WriteLine(string.Join(
                        Constants.CliRuleSeparator,
                        cols.Select(c => new string(Constants.CliRuleCharacter, c.Length + Constants.CliColumnPadding))));

                    foreach (var row in rows)
                    {
                        var vals = cols.Select(c => row.Values[c].ToString() ?? Constants.CliNullValue);
                        Console.WriteLine(string.Join(Constants.CliColumnSeparator, vals));
                    }
                }
            }
            else if (res is string msg)
            {
                Console.WriteLine(msg);
            }
            else
            {
                Console.WriteLine(res);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(Constants.CliErrorPrefix + ex.Message);
    }
}