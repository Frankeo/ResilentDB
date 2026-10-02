namespace Engine
{

    public class DbEngine
    {
        private readonly Storage _storage;
        private Schema _schema;
        private Dictionary<string, List<Row>> _tables;

        public DbEngine(string filePath)
        {
            _storage = new Storage(filePath);
            if (!_storage.Exists)
            {
                _storage.CreateFile();
            }
            var (schema, tables) = _storage.Load();
            _schema = schema;
            _tables = tables;
        }

        public void Save()
        {
            _storage.Save(_schema, _tables);
        }

        public object Execute(Statement stmt)
        {
            return stmt switch
            {
                CreateTableStatement ct => ExecuteCreateTable(ct),
                InsertStatement ins => ExecuteInsert(ins),
                SelectStatement sel => ExecuteSelect(sel),
                _ => throw new NotSupportedException(Constants.UnsupportedCommandError)
            };
        }

        private object ExecuteCreateTable(CreateTableStatement stmt)
        {
            if (_schema.Tables.ContainsKey(stmt.TableName))
                throw new Exception(string.Format(Constants.TableAlreadyExistsError, stmt.TableName));

            var tableDef = new TableDef
            {
                Name = stmt.TableName,
                Columns = stmt.Columns
            };

            _schema.Tables[stmt.TableName] = tableDef;
            _tables[stmt.TableName] = new List<Row>();

            Save();
            return "Tabla creada";
        }

        private object ExecuteInsert(InsertStatement stmt)
        {
            if (!_schema.Tables.TryGetValue(stmt.TableName, out var tableDef))
                throw new Exception(string.Format(Constants.TableNotFoundError, stmt.TableName));

            var cols = tableDef.Columns.Select(c => c.Name).ToList();
            if (cols.Count != stmt.Values.Count)
                throw new Exception(Constants.IncorrectValueCountError);

            var row = new Row();
            for (int i = 0; i < cols.Count; i++)
            {
                row.Values[cols[i]] = stmt.Values[i];
            }

            _tables[stmt.TableName].Add(row);
            Save();
            return Constants.InsertSuccessMessage;
        }

        private object ExecuteSelect(SelectStatement stmt)
        {
            if (!_tables.TryGetValue(stmt.TableName, out var rows))
                throw new Exception(string.Format(Constants.TableNotFoundError, stmt.TableName));

            // Aplicar WHERE
            if (stmt.Where != null)
            {
                var w = stmt.Where;
                rows = rows.Where(r => CumpleWhere(r, w)).ToList();
            }

            // Proyectar columnas
            if (stmt.Columns.Count == 1 && stmt.Columns[0] == "*")
            {
                return rows; // todas las columnas
            }
            else
            {
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
        }

        private bool CumpleWhere(Row row, WhereClause w)
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

        private int Compare(object a, object b)
        {
            if (a is int ai && b is int bi)
                return ai.CompareTo(bi);

            var sa = a?.ToString() ?? "";
            var sb = b?.ToString() ?? "";
            return string.Compare(sa, sb, StringComparison.Ordinal);
        }
    }
}