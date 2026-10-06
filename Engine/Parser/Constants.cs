namespace Engine.Parsing;

internal static class Constants
{
    internal const char SqlStringDelimiter = '\'';
    internal const char SqlValueSeparator = ',';
    internal const char SqlStatementTerminator = ';';
    internal const string UnsupportedCommandError = "Comando no soportado";
    internal const string InvalidCreateTableError = "CREATE TABLE inválido";
    internal const string InvalidColumnDefinitionError = "Definición de columna inválida: {0}";
    internal const string InvalidInsertError = "INSERT inválido";
    internal const string InvalidSelectError = "SELECT inválido";
    internal const string InvalidDeleteError = "DELETE inválido";
    internal const string InvalidUpdateError = "UPDATE inválido";
    internal const string DuplicateAssignmentColumnError = "La columna '{0}' aparece más de una vez";
    internal const string MultiplePrimaryKeysError = "Solo se permite una PRIMARY KEY";
    internal const string PrimaryKeyRequiredError = "La tabla debe definir una PRIMARY KEY";
    internal const string PrimaryKeyMustBeIntegerError = "La PRIMARY KEY debe ser de tipo INTEGER";
    internal const string UnsupportedWhereError = "WHERE no soportado completamente";
}