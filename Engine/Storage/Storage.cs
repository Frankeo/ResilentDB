using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Engine.BufferPool;

namespace Engine
{

    public class Storage
    {
        private readonly string _filePath;

        public Storage(string filePath)
        {
            _filePath = filePath;
        }

        public bool Exists => File.Exists(_filePath);

        public void CreateFile() => Save(new Schema(), new Dictionary<string, List<Row>>());

        public (Schema schema, Dictionary<string, List<Row>> tables) Load()
        {
            using var bufferPool = new ClockBufferPool(_filePath);
            var pageCount = bufferPool.PageCount;
            if (pageCount < 2)
                throw new InvalidDataException(Constants.InvalidFileError);

            using var pageData = new MemoryStream();
            for (var pageId = 1; pageId < pageCount; pageId++)
                pageData.Write(bufferPool.ReadPage(pageId));

            var storedData = pageData.ToArray();
            if (storedData.Length < sizeof(int))
                throw new InvalidDataException(Constants.InvalidFileError);

            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(storedData.AsSpan(0, sizeof(int)));
            if (payloadLength < 0 || payloadLength > storedData.Length - sizeof(int))
                throw new InvalidDataException(Constants.InvalidFileError);

            using var payload = new MemoryStream(storedData, sizeof(int), payloadLength, writable: false);
            using var reader = new BinaryReader(payload, Encoding.UTF8);

            var schema = ReadJson<Schema>(reader);
            var tableCount = reader.ReadInt32();
            var tables = new Dictionary<string, List<Row>>();

            for (var i = 0; i < tableCount; i++)
            {
                var nameLength = reader.ReadInt32();
                var name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));

                var rowCount = reader.ReadInt32();
                var rows = new List<Row>();

                for (var j = 0; j < rowCount; j++)
                    rows.Add(ReadJson<Row>(reader));

                tables[name] = rows;
            }

            return (schema, tables);
        }

        public void Save(Schema schema, Dictionary<string, List<Row>> tables)
        {
            byte[] payload;
            using (var payloadStream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
                {
                    WriteJson(writer, schema);
                    writer.Write(tables.Count);

                    foreach (var (name, rows) in tables)
                    {
                        var nameBytes = Encoding.UTF8.GetBytes(name);
                        writer.Write(nameBytes.Length);
                        writer.Write(nameBytes);
                        writer.Write(rows.Count);

                        foreach (var row in rows)
                            WriteJson(writer, row);
                    }
                }

                payload = payloadStream.ToArray();
            }

            var storedData = new byte[sizeof(int) + payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(storedData.AsSpan(0, sizeof(int)), payload.Length);
            payload.CopyTo(storedData, sizeof(int));

            var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var bufferPool = new ClockBufferPool(temporaryPath))
                {
                    for (var offset = 0; offset < storedData.Length; offset += bufferPool.PageSize)
                    {
                        var pageId = bufferPool.AllocatePage();
                        var length = Math.Min(bufferPool.PageSize, storedData.Length - offset);
                        bufferPool.WritePage(pageId, storedData.AsSpan(offset, length).ToArray());
                    }

                    bufferPool.Flush();
                }

                File.Move(temporaryPath, _filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private void WriteJson(BinaryWriter bw, object obj)
        {
            var json = JsonSerializer.Serialize(obj);
            var bytes = Encoding.UTF8.GetBytes(json);
            bw.Write(bytes.Length);
            bw.Write(bytes);
        }

        private T ReadJson<T>(BinaryReader br)
        {
            var len = br.ReadInt32();
            var bytes = br.ReadBytes(len);
            var json = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<T>(json)!;
        }
    }
}