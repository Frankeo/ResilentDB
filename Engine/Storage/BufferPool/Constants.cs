namespace Engine.BufferPool;

public static class Constants
{
    public const int DefaultBufferPoolCapacity = 256;
    internal const string InvalidFileError = "Archivo no válido";
    internal const string PageDataTooLargeError = "Los datos no caben en una página";
    internal const string BufferPoolNoUnpinnedPageError = "No hay páginas disponibles sin fijar en el buffer pool";
}