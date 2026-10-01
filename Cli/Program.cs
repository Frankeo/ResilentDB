using Engine;

var dbPath = "basededatos.mdb";
var engine = new DbEngine(dbPath);

Console.WriteLine("MiniDB v0.1 - archivo: " + dbPath);
Console.WriteLine("Comandos: CREATE TABLE, INSERT, SELECT");
Console.WriteLine("Escribe .exit para salir.\n");

while (true)
{
    Console.Write("mdb> ");
    var line = Console.ReadLine();
    if (line == null) break;
    line = line.Trim();
    if (line == "") continue;
    if (line == ".exit" || line == ".quit")
        break;

    try
    {
        var stmt = Parser.Parse(line);
        var res = engine.Execute(stmt);

        if (res is List<Row> rows)
        {
            if (rows.Count == 0)
            {
                Console.WriteLine("(0 filas)");
            }
            else
            {
                // Imprimir cabecera
                var cols = rows[0].Values.Keys.ToList();
                Console.WriteLine(string.Join(" | ", cols));
                Console.WriteLine(string.Join("-", cols.Select(c => new string('-', c.Length + 2))));

                foreach (var row in rows)
                {
                    var vals = cols.Select(c => row.Values[c].ToString() ?? "NULL");
                    Console.WriteLine(string.Join(" | ", vals));
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
    catch (Exception ex)
    {
        Console.WriteLine("Error: " + ex.Message);
    }
}