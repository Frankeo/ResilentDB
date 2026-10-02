using Engine;

namespace Engine.Executors;

public sealed class SelectExecutor : ExecutorBase
{
    public override bool CanExecute(Statement statement) => statement is SelectStatement;

    public override object Execute(DbEngine engine, Statement statement)
    {
        var stmt = (SelectStatement)statement;

        if (!engine.Tables.TryGetValue(stmt.TableName, out var rows))
            throw new Exception(string.Format(Constants.TableNotFoundError, stmt.TableName));

        if (stmt.Where != null)
        {
            rows = rows.Where(r => CumpleWhere(r, stmt.Where)).ToList();
        }

        if (stmt.Columns.Count == 1 && stmt.Columns[0] == "*")
        {
            return rows;
        }

        return rows
            .Select(r =>
            {
                var newRow = new Row();
                foreach (var col in stmt.Columns)
                {
                    newRow.Values[col] = r.Values[col];
                }
                return newRow;
            })
            .ToList();
    }

    private static bool CumpleWhere(Row row, WhereClause w)
    {
        if (!row.Values.TryGetValue(w.Column, out var v))
            return false;

        return w.Op switch
        {
            "=" => Equals(v, w.Value),
            ">" => Compare(v, w.Value) > 0,
            "<" => Compare(v, w.Value) < 0,
            ">=" => Compare(v, w.Value) >= 0,
            "<=" => Compare(v, w.Value) <= 0,
            _ => throw new NotSupportedException(string.Format(Constants.UnsupportedOperatorError, w.Op))
        };
    }

    private static int Compare(object a, object b)
    {
        if (a is int ai && b is int bi)
            return ai.CompareTo(bi);

        var sa = a?.ToString() ?? "";
        var sb = b?.ToString() ?? "";
        return string.Compare(sa, sb, StringComparison.Ordinal);
    }
}
