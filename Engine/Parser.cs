using Engine.Statements;

namespace Engine
{
    public static class Parser
    {
        private static readonly IStatementCommand[] StatementCommands =
        {
            new CreateTableStatementCommand(),
            new InsertStatementCommand(),
            new SelectStatementCommand()
        };

        public static Statement Parse(string sql)
        {
            var s = sql.Trim();

            foreach (var command in StatementCommands)
            {
                if (command.CanHandle(s))
                    return command.Parse(s);
            }

            throw new NotSupportedException(Constants.UnsupportedCommandError);
        }

        public static IReadOnlyList<Statement> ParseStatements(string sql)
        {
            var statements = new List<Statement>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;

            foreach (var character in sql)
            {
                if (character == '\'')
                    inQuotes = !inQuotes;

                if (character == ';' && !inQuotes)
                {
                    AddStatement(current, statements);
                    continue;
                }

                current.Append(character);
            }

            AddStatement(current, statements);
            return statements;
        }

        private static void AddStatement(System.Text.StringBuilder sql, ICollection<Statement> statements)
        {
            var statementSql = sql.ToString().Trim();
            sql.Clear();

            if (statementSql.Length > 0)
                statements.Add(Parse(statementSql));
        }
    }
}