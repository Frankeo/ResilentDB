namespace Engine
{
	public static class Constants
	{
		internal const string FileMagic = "ResilentDB";
		internal const int FileMagicReadLength = 4;
		internal const int FileVersion = 1;
		internal const int InitialTableCount = 0;
		internal const int TruncatedFileLength = 0;
		internal const string InvalidFileError = "Archivo no válido";
		internal const string UnsupportedCommandError = "Comando no soportado";
		internal const string TableAlreadyExistsError = "La tabla {0} ya existe";
		internal const string TableNotFoundError = "La tabla {0} no existe";
		internal const string IncorrectValueCountError = "Número de valores incorrecto";
		internal const string UnsupportedOperatorError = "Operador {0} no soportado";
		internal const string InsertSuccessMessage = "1 fila insertada";
		public const string DatabaseFileName = "basededatos.mdb";
		public static readonly string StartupBanner = $"{FileMagic} v{FileVersion} - archivo: {DatabaseFileName}";
		public const string CliCommands = "Comandos: CREATE TABLE, INSERT, SELECT";
		public const string CliExitInstructions = $"Escribe {Constants.CliExitCommand} para salir.\n";
		public const string CliPrompt = "mdb> ";
		public const string CliExitCommand = ".exit";
		public const string CliEmptyResult = "(0 filas)";
		public const string CliColumnSeparator = " | ";
		public const string CliRuleSeparator = "-";
		public const char CliRuleCharacter = '-';
		public const int CliColumnPadding = 2;
		public const string CliNullValue = "NULL";
		public const string CliErrorPrefix = "Error: ";
	}
}
