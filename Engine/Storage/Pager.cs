using System.Text;

namespace Engine;

public sealed class Pager : IDisposable
{
    private readonly string _filePath;
    private readonly int _pageSize;
    private FileStream? _stream;
    private HeaderPage? _header;
    private bool _disposed;

    public Pager(string filePath, int pageSize = Constants.DefaultPageSize)
    {
        if (pageSize < Constants.HeaderSize)
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                string.Format(Constants.PageSizeTooSmallError, Constants.HeaderSize));

        _filePath = filePath;
        _pageSize = pageSize;

        if (!File.Exists(filePath))
            CreateFile();

        _stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var header = ReadHeader();
            if (header.PageSize != _pageSize)
                throw new InvalidDataException(Constants.PageSizeMismatchError);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int PageSize
    {
        get
        {
            ThrowIfDisposed();
            return _pageSize;
        }
    }

    public int PageCount => ReadHeader().PageCount;

    public void Flush() => GetStream().Flush(flushToDisk: true);

    public HeaderPage ReadHeader()
    {
        ThrowIfDisposed();
        if (_header is not null)
            return CopyHeader(_header);

        var stream = GetStream();
        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        var header = new HeaderPage
        {
            Magic = Encoding.ASCII.GetString(reader.ReadBytes(4)),
            Version = reader.ReadInt32(),
            PageSize = reader.ReadInt32(),
            PageCount = reader.ReadInt32()
        };

        if (header.Magic != Constants.FileMagic || header.Version != Constants.FileVersion ||
            header.PageSize < Constants.HeaderSize ||
            header.PageCount < 1 || stream.Length < (long)header.PageCount * header.PageSize)
        {
            throw new InvalidDataException(Constants.InvalidFileError);
        }

        _header = header;
        return CopyHeader(header);
    }

    public byte[] ReadPage(int pageId)
    {
        var header = ReadHeader();
        if (pageId <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageId), Constants.HeaderPageAccessError);
        if (pageId >= header.PageCount)
            throw new ArgumentOutOfRangeException(
                nameof(pageId),
                string.Format(Constants.PageIdOutOfRangeError, pageId));

        var data = new byte[_pageSize];
        var stream = GetStream();
        stream.Seek((long)pageId * _pageSize, SeekOrigin.Begin);
        stream.ReadExactly(data);
        return data;
    }

    public void WritePage(int pageId, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (pageId <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageId), Constants.HeaderPageAccessError);
        if (pageId >= PageCount)
            throw new ArgumentOutOfRangeException(
                nameof(pageId),
                string.Format(Constants.PageIdOutOfRangeError, pageId));
        if (data.Length > _pageSize)
            throw new ArgumentException(Constants.PageDataTooLargeError, nameof(data));

        var stream = GetStream();
        var page = new byte[_pageSize];
        data.CopyTo(page, 0);
        stream.Seek((long)pageId * _pageSize, SeekOrigin.Begin);
        stream.Write(page);
    }

    public int AllocatePage()
    {
        var header = ReadHeader();
        var pageId = header.PageCount;
        var stream = GetStream();
        stream.SetLength((long)(pageId + 1) * _pageSize);

        header.PageCount++;
        WriteHeader(header);
        return pageId;
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _stream?.Flush(flushToDisk: true);
        }
        finally
        {
            _stream?.Dispose();
            _stream = null;
            _disposed = true;
        }
    }

    private void CreateFile()
    {
        using var stream = File.Create(_filePath);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        WriteHeader(writer, new HeaderPage
        {
            Magic = Constants.FileMagic,
            Version = Constants.FileVersion,
            PageSize = _pageSize,
            PageCount = 1
        });
        stream.SetLength(_pageSize);
    }

    private void WriteHeader(HeaderPage header)
    {
        var stream = GetStream();
        stream.Seek(0, SeekOrigin.Begin);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        WriteHeader(writer, header);
        stream.Flush(flushToDisk: true);
        _header = CopyHeader(header);
    }

    private static void WriteHeader(BinaryWriter writer, HeaderPage header)
    {
        writer.Write(Encoding.ASCII.GetBytes(header.Magic));
        writer.Write(header.Version);
        writer.Write(header.PageSize);
        writer.Write(header.PageCount);
    }

    private FileStream GetStream() =>
        _disposed || _stream is null
            ? throw new ObjectDisposedException(nameof(Pager))
            : _stream;

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Pager));
    }

    private static HeaderPage CopyHeader(HeaderPage header) => new()
    {
        Magic = header.Magic,
        Version = header.Version,
        PageSize = header.PageSize,
        PageCount = header.PageCount
    };
}

public sealed class HeaderPage
{
    public string Magic { get; set; } = "";
    public int Version { get; set; }
    public int PageSize { get; set; }
    public int PageCount { get; set; }
}
