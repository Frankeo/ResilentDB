using System.Text.RegularExpressions;

namespace Engine
{

    public static class Parser
    {
        public static Statement Parse(string sql)
        {
            var s = sql.Trim();
            var upper = s.ToUpperInvariant();

            if (upper.StartsWith("CREATE TABLE"))
                return ParseCreateTable(s);
            if (upper.StartsWith("INSERT INTO"))
                return ParseInsert(s);
            if (upper.StartsWith("SELECT"))
                return ParseSelect(s);

            throw new NotSupportedException("Comando no soportado");
        }

        private static CreateTableStatement ParseCreateTable(string sql)
        {
            // CREATE TABLE usuarios (id INTEGER, nombre TEXT, edad INTEGER)
            var m = Regex.Match(
                sql,
                @"CREATE\s+TABLE\s+(\w+)\s*\((.*)\)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );
            if (!m.Success)
                throw new Exception("CREATE TABLE inválido");

            var tableName = m.Groups[1].Value;
            var colsStr = m.Groups[2].Value;

            var columns = new List<ColumnDef>();
            foreach (var parte in colsStr.Split(','))
            {
                var p = parte.Trim();
                if (string.IsNullOrEmpty(p)) continue;
                var parts = p.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    throw new Exception($"Definición de columna inválida: {p}");

                columns.Add(new ColumnDef
                {
                    Name = parts[0],
                    Type = parts[1].ToUpperInvariant()
                });
            }

            return new CreateTableStatement
            {
                TableName = tableName,
                Columns = columns
            };
        }

        private static InsertStatement ParseInsert(string sql)
        {
            // INSERT INTO usuarios VALUES (1, 'Ana', 28)
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

        private static SelectStatement ParseSelect(string sql)
        {
            // SELECT * FROM usuarios WHERE edad > 18
            var m = Regex.Match(
                sql,
                @"SELECT\s+(.+?)\s+FROM\s+(\w+)(?:\s+WHERE\s+(.+))?",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );
            if (!m.Success)
                throw new Exception("SELECT inválido");

            var colsStr = m.Groups[1].Value.Trim();
            var tableName = m.Groups[2].Value;
            var whereStr = m.Groups[3].Success ? m.Groups[3].Value.Trim() : null;

            var columns = colsStr == "*"
                ? new List<string> { "*" }
                : colsStr.Split(',').Select(c => c.Trim()).ToList();

            WhereClause? where = null;
            if (whereStr != null)
            {
                var wm = Regex.Match(whereStr, @"(\w+)\s*(=|>|<|>=|<=)\s*(.+)");
                if (!wm.Success)
                    throw new Exception("WHERE no soportado completamente");

                var col = wm.Groups[1].Value;
                var op = wm.Groups[2].Value;
                var valStr = wm.Groups[3].Value.Trim();

                where = new WhereClause
                {
                    Column = col,
                    Op = op,
                    Value = ParseValue(valStr)
                };
            }

            return new SelectStatement
            {
                TableName = tableName,
                Columns = columns,
                Where = where
            };
        }

        private static IEnumerable<string> SplitValues(string valsStr)
        {
            // Muy básico: separa por comas, respeta comillas simples
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

        private static object ParseValue(string v)
        {
            if (v.StartsWith("'") && v.EndsWith("'"))
                return v.Substring(1, v.Length - 2); // string

            if (int.TryParse(v, out var i))
                return i;

            return v; // fallback string
        }
    }
}