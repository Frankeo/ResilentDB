namespace ResilentDB.Cli;

internal static class Constants
{
    internal const string DatabaseFileName = "basededatos.mdb";
    internal static readonly string StartupBanner = $"{Engine.PagerConfig.Constants.FileMagic} v{Engine.PagerConfig.Constants.FileVersion} - archivo: {DatabaseFileName}";
    internal const string CliCommands = "Comandos: CREATE TABLE, INSERT, SELECT, .read <archivo.sql>";
    internal const string CliExitInstructions = $"Escribe {CliExitCommand} para salir.\n";
    internal const string CliPrompt = "mdb> ";
    internal const string CliExitCommand = ".exit";
    internal const string CliReadCommand = ".read";
    internal const string CliReadUsageError = "Uso: .read <archivo.sql>";
    internal const string CliEmptyResult = "(0 filas)";
    internal const string CliColumnSeparator = " | ";
    internal const string CliRuleSeparator = "-";
    internal const char CliRuleCharacter = '-';
    internal const int CliColumnPadding = 2;
    internal const string CliNullValue = "NULL";
    internal const string CliErrorPrefix = "Error: ";
}