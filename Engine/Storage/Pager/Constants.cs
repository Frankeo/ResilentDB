namespace Engine.PagerConfig;

public static class Constants
{
    public const int DefaultPageSize = 4096;
    public const string FileMagic = "RDBP";
    public const int FileVersion = 4;

    internal const int HeaderSize = 16;
    internal const string InvalidFileError = "Archivo no válido";
    internal const string PageSizeTooSmallError = "El tamaño de página debe ser al menos {0} bytes";
    internal const string PageSizeMismatchError = "El tamaño de página no coincide con el archivo";
    internal const string HeaderPageAccessError = "La página 0 está reservada para el encabezado";
    internal const string PageIdOutOfRangeError = "PageId {0} fuera de rango";
    internal const string PageDataTooLargeError = "Los datos no caben en una página";
}