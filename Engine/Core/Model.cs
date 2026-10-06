namespace Engine
{
    public class ColumnDef
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsPrimaryKey { get; set; }
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

    public abstract record ExecutionResult;

    public sealed record CommandResult(int AffectedRows, string Message) : ExecutionResult;

    public sealed record QueryResult(
        IReadOnlyList<string> Columns,
        IReadOnlyList<IReadOnlyList<object?>> Rows) : ExecutionResult;

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
        public List<string> Columns { get; set; } = new();
        public WhereClause? Where { get; set; }
    }

    public class DeleteStatement : Statement
    {
        public string TableName { get; set; } = "";
        public WhereClause? Where { get; set; }
    }

    public class UpdateStatement : Statement
    {
        public string TableName { get; set; } = "";
        public Dictionary<string, object> Assignments { get; set; } = new();
        public WhereClause? Where { get; set; }
    }

    public class WhereClause
    {
        public string Column { get; set; } = "";
        public string Op { get; set; } = "=";
        public object Value { get; set; } = "";
    }
}