using Engine;
using System.Text.Json;

namespace Engine.Executors;

public abstract class ExecutorBase : IExecutor
{
    public abstract bool CanExecute(Statement statement);
    public abstract object Execute(DbEngine engine, Statement statement);

    protected static void ValidateWhereClause(TableDef table, WhereClause? where)
    {
        if (where is null)
            return;

        if (!table.Columns.Any(column => column.Name == where.Column))
            throw new Exception(string.Format(Constants.ColumnNotFoundError, where.Column));

        if (where.Op is not ("=" or ">" or "<" or ">=" or "<="))
            throw new NotSupportedException(
                string.Format(Constants.UnsupportedOperatorError, where.Op));
    }

    protected static List<Row> FindMatchingRows(
        DbEngine engine,
        string tableName,
        TableDef table,
        WhereClause? where)
    {
        var index = engine.OpenPrimaryIndex(tableName);
        if (where is null)
            return index.ScanRecords().Select(entry => entry.Value).ToList();

        var primaryKey = table.Columns.SingleOrDefault(column => column.IsPrimaryKey);
        if (primaryKey is not null &&
            string.Equals(where.Column, primaryKey.Name, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryConvertPrimaryKey(where.Value, out var key))
                return new List<Row>();

            if (where.Op == "=")
            {
                return index.TryGetRecord(key, out var row) && row is not null
                    ? new List<Row> { row }
                    : new List<Row>();
            }

            long? minimum = null;
            long? maximum = null;
            switch (where.Op)
            {
                case ">":
                    if (key == long.MaxValue)
                        return new List<Row>();
                    minimum = key + 1;
                    break;
                case ">=":
                    minimum = key;
                    break;
                case "<":
                    if (key == long.MinValue)
                        return new List<Row>();
                    maximum = key - 1;
                    break;
                case "<=":
                    maximum = key;
                    break;
            }

            return index.ScanRecords(minimum, maximum)
                .Select(entry => entry.Value)
                .ToList();
        }

        return index.ScanRecords()
            .Select(entry => entry.Value)
            .Where(row => MatchesWhere(row, where))
            .ToList();
    }

    protected static bool TryConvertPrimaryKey(object value, out long key)
    {
        if (value is JsonElement json && json.ValueKind == JsonValueKind.Number)
            return json.TryGetInt64(out key);

        if (value is string or bool or char)
        {
            key = default;
            return false;
        }

        try
        {
            key = Convert.ToInt64(value);
            return true;
        }
        catch (Exception)
        {
            key = default;
            return false;
        }
    }

    protected static bool MatchesWhere(Row row, WhereClause? where)
    {
        if (where is null)
            return true;

        if (!row.Values.TryGetValue(where.Column, out var value))
            return false;

        if (where.Op == "=")
            return ValuesEqual(value, where.Value);

        var comparison = Compare(value, where.Value);
        return where.Op switch
        {
            ">" => comparison > 0,
            "<" => comparison < 0,
            ">=" => comparison >= 0,
            "<=" => comparison <= 0,
            _ => throw new NotSupportedException(
                string.Format(Constants.UnsupportedOperatorError, where.Op))
        };
    }

    private static int Compare(object left, object right)
    {
        left = Normalize(left);
        right = Normalize(right);

        if (TryConvertNumber(left, out var leftNumber) &&
            TryConvertNumber(right, out var rightNumber))
            return leftNumber.CompareTo(rightNumber);

        return string.Compare(
            left.ToString(),
            right.ToString(),
            StringComparison.Ordinal);
    }

    protected static bool ValuesEqual(object left, object right)
    {
        left = Normalize(left);
        right = Normalize(right);

        if (Equals(left, right))
            return true;

        return TryConvertNumber(left, out var leftNumber) &&
            TryConvertNumber(right, out var rightNumber) &&
            leftNumber == rightNumber;
    }

    private static object Normalize(object value)
    {
        if (value is not JsonElement json)
            return value;

        return json.ValueKind switch
        {
            JsonValueKind.String => json.GetString() ?? "",
            JsonValueKind.Number when json.TryGetInt32(out var integer) => integer,
            JsonValueKind.Number when json.TryGetInt64(out var longInteger) => longInteger,
            JsonValueKind.Number => json.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => value
        };
    }

    private static bool TryConvertNumber(object value, out decimal number)
    {
        if (value is string || value is bool || value is char)
        {
            number = default;
            return false;
        }

        try
        {
            number = Convert.ToDecimal(value);
            return true;
        }
        catch (Exception)
        {
            number = default;
            return false;
        }
    }
}
