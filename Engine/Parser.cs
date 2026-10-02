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

            throw new NotSupportedException("Comando no soportado");
        }
    }
}