namespace Engine.BPlusTree;

internal static class Constants
{
    internal const int InvalidPageId = -1;
    internal const int FirstAllocatablePageId = 2;
    internal const int DefaultMetadataPageId = 1;
    internal const int DefaultMaxKeys = 32;
    internal const string InvalidFileError = "Archivo no válido";
    internal const string PageDataTooLargeError = "Los datos no caben en una página";
    internal const string DuplicatePrimaryKeyError = "Valor duplicado para PRIMARY KEY";
}