using Engine.Parsing;

namespace Engine
{
    public static class Parser
    {
        private static readonly IStatementParser[] StatementParsers =
        {
            new CreateTableStatementParser(),
            new InsertStatementParser(),
            new SelectStatementParser(),
            new DeleteStatementParser(),
            new UpdateStatementParser()
        };

        public static Statement Parse(string sql)
        {
            var s = sql.Trim();

            foreach (var parser in StatementParsers)
            {
                if (parser.CanHandle(s))
                    return parser.Parse(s);
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
                if (character == Constants.SqlStringDelimiter)
                    inQuotes = !inQuotes;

                if (character == Constants.SqlStatementTerminator && !inQuotes)
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