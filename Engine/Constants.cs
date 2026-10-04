namespace Engine
{
	public static class Constants
	{
		public const int DefaultPageSize = 4096;
		public const int DefaultBufferPoolCapacity = 256;
		internal const string FileMagic = "RDBP";
		internal const int FileVersion = 1;
		internal const int HeaderSize = 16;
		internal const char SqlStringDelimiter = '\'';
		internal const char SqlValueSeparator = ',';
		internal const char SqlStatementTerminator = ';';
		internal const string InvalidFileError = "Archivo no válido";
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
		internal const string UnsupportedWhereError = "WHERE no soportado completamente";
		internal const string PageSizeTooSmallError = "El tamaño de página debe ser al menos {0} bytes";
		internal const string PageSizeMismatchError = "El tamaño de página no coincide con el archivo";
		internal const string HeaderPageAccessError = "La página 0 está reservada para el encabezado";
		internal const string PageIdOutOfRangeError = "PageId {0} fuera de rango";
		internal const string PageDataTooLargeError = "Los datos no caben en una página";
		internal const string TableAlreadyExistsError = "La tabla {0} ya existe";
		internal const string TableNotFoundError = "La tabla {0} no existe";
		internal const string IncorrectValueCountError = "Número de valores incorrecto";
		internal const string UnsupportedOperatorError = "Operador {0} no soportado";
		internal const string TableCreatedMessage = "Tabla creada";
		internal const string InsertSuccessMessage = "1 fila insertada";
		internal const string InsertMissingPrimaryKeyError = "La fila debe incluir la PRIMARY KEY";
		internal const string PrimaryKeyMustBeIntegerError = "La PRIMARY KEY debe ser de tipo INTEGER";
		internal const string DuplicatePrimaryKeyError = "Valor duplicado para PRIMARY KEY";
		internal const string UpdateDuplicatePrimaryKeyError = "El UPDATE produciría una PRIMARY KEY duplicada";
		internal const string ColumnNotFoundError = "La columna '{0}' no existe";
		internal const string DeletedRowsMessage = "Filas eliminadas: {0}";
		internal const string UpdatedRowsMessage = "Filas actualizadas: {0}";
		public const string DatabaseFileName = "basededatos.mdb";
		public static readonly string StartupBanner = $"{FileMagic} v{FileVersion} - archivo: {DatabaseFileName}";
		public const string CliCommands = "Comandos: CREATE TABLE, INSERT, SELECT, .read <archivo.sql>";
		public const string CliExitInstructions = $"Escribe {Constants.CliExitCommand} para salir.\n";
		public const string CliPrompt = "mdb> ";
		public const string CliExitCommand = ".exit";
		public const string CliReadCommand = ".read";
		public const string CliReadUsageError = "Uso: .read <archivo.sql>";
		public const string CliEmptyResult = "(0 filas)";
		public const string CliColumnSeparator = " | ";
		public const string CliRuleSeparator = "-";
		public const char CliRuleCharacter = '-';
		public const int CliColumnPadding = 2;
		public const string CliNullValue = "NULL";
		public const string CliErrorPrefix = "Error: ";
	}
}
