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
    using var engine = new DbEngine(databasePath, new ConsoleTraceSink());
    var results = engine.Execute(File.ReadAllText(scriptPath));

    foreach (var result in results)
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
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Execution failed: {exception.Message}");
    return 1;
}

return 0;

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