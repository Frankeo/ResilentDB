namespace Engine
{

    public class ColumnDef
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = ""; // "INTEGER" o "TEXT"
    }

    public class TableDef
    {
        public string Name { get; set; } = "";
        public List<ColumnDef> Columns { get; set; } = new();
    }

    public class Schema
    {
        public Dictionary<string, TableDef> Tables { get; set; } = new();
    }

    public class Row
    {
        public Dictionary<string, object> Values { get; set; } = new();
    }

    // AST (estructuras para los comandos parseados)

    public abstract class Statement { }

    public class CreateTableStatement : Statement
    {
        public string TableName { get; set; } = "";
        public List<ColumnDef> Columns { get; set; } = new();
    }

    public class InsertStatement : Statement
    {
        public string TableName { get; set; } = "";
        public List<object> Values { get; set; } = new();
    }

    public class SelectStatement : Statement
    {
        public string TableName { get; set; } = "";
        public List<string> Columns { get; set; } = new(); // ["*"] o ["id","nombre"]
        public WhereClause? Where { get; set; }
    }

    public class WhereClause
    {
        public string Column { get; set; } = "";
        public string Op { get; set; } = "="; // "=", ">", "<", etc.
        public object Value { get; set; } = "";
    }
}