using Engine;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Diagnostics <database-file> <script.sql>");
    return 2;
}

var databasePath = Path.GetFullPath(args[0]);
var scriptPath = Path.GetFullPath(args[1]);
if (!File.Exists(scriptPath))
{
    Console.Error.WriteLine($"SQL script not found: {scriptPath}");
    return 2;
}

try
{
    var traceSink = new ConsoleTraceSink();
    using var engine = new DbEngine(databasePath, traceSink);
    var statements = Parser.ParseStatements(File.ReadAllText(scriptPath)).ToList();
    traceSink.Write(new EngineTraceEvent("Parser", "StatementsParsed", $"count={statements.Count}"));

    for (int index = 0; index < statements.Count; index++)
    {
        var statement = statements[index];
        Console.WriteLine();
        Console.WriteLine($"=== STEP {index + 1}/{statements.Count}: {DescribeStatement(statement)} ===");
        var result = engine.Execute(statement);
        PrintResult(result);
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Execution failed: {exception.Message}");
    return 1;
}

return 0;

static string DescribeStatement(Statement statement) => statement switch
{
    CreateTableStatement create => $"CREATE TABLE {create.TableName}",
    InsertStatement insert => $"INSERT INTO {insert.TableName}",
    SelectStatement select => $"SELECT FROM {select.TableName}",
    UpdateStatement update => $"UPDATE {update.TableName}",
    DeleteStatement delete => $"DELETE FROM {delete.TableName}",
    _ => statement.GetType().Name
};

static void PrintResult(ExecutionResult result)
{
    if (result is QueryResult query)
    {
        Console.WriteLine($"RESULT columns=[{string.Join(",", query.Columns)}] rows={query.Rows.Count}");
        foreach (var row in query.Rows)
            Console.WriteLine($"  {string.Join(" | ", row)}");
    }
    else if (result is CommandResult command)
    {
        Console.WriteLine($"RESULT {command.Message}");
    }
}

internal sealed class ConsoleTraceSink : IEngineTraceSink
{
    private long _sequence;

    public void Write(EngineTraceEvent traceEvent)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var indentation = new string(' ', Math.Clamp(traceEvent.Depth, 0, 32) * 2);
        Console.WriteLine(
            $"{sequence:000000} {indentation}[{traceEvent.Component}] {traceEvent.Operation}: {traceEvent.Detail}");
    }
}