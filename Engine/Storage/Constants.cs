namespace Engine.StorageConfig;

public static class Constants
{
    internal const int CatalogPageId = 1;
    internal const int FirstAllocatablePageId = 2;
    internal const int InvalidPageId = -1;
    internal const int DefaultBPlusTreeMaxKeys = 32;
    internal const string InvalidFileError = "Archivo no válido";
    internal const string CatalogTooLargeError = "El catálogo no cabe en una página";
}