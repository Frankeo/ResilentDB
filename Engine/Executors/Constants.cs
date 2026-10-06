namespace Engine.Executors;

internal static class Constants
{
    internal const string InvalidFileError = "Archivo no válido";
    internal const string UnsupportedCommandError = "Comando no soportado";
    internal const string TableAlreadyExistsError = "La tabla {0} ya existe";
    internal const string TableNotFoundError = "La tabla {0} no existe";
    internal const string IncorrectValueCountError = "Número de valores incorrecto";
    internal const string PrimaryKeyRequiredError = "La tabla debe definir una PRIMARY KEY";
    internal const string InsertMissingPrimaryKeyError = "La fila debe incluir la PRIMARY KEY";
    internal const string PrimaryKeyMustBeIntegerError = "La PRIMARY KEY debe ser de tipo INTEGER";
    internal const string DuplicatePrimaryKeyError = "Valor duplicado para PRIMARY KEY";
    internal const string UpdateDuplicatePrimaryKeyError = "El UPDATE produciría una PRIMARY KEY duplicada";
    internal const string ColumnNotFoundError = "La columna '{0}' no existe";
    internal const string UnsupportedOperatorError = "Operador {0} no soportado";
    internal const string TableCreatedMessage = "Tabla creada";
    internal const string InsertSuccessMessage = "1 fila insertada";
    internal const string DeletedRowsMessage = "Filas eliminadas: {0}";
    internal const string UpdatedRowsMessage = "Filas actualizadas: {0}";
}