using System.Text;
using System.Text.Json;

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

        public void CreateFile()
        {
            using var fs = File.Create(_filePath);
            using var bw = new BinaryWriter(fs);

            // Magia
            bw.Write(Encoding.ASCII.GetBytes(Constants.FileMagic));
            // Versión 1
            bw.Write(Constants.FileVersion);

            // Schema vacío
            var schema = new Schema();
            WriteJson(bw, schema);

            // 0 tablas inicialmente
            bw.Write(Constants.InitialTableCount);
        }

        public (Schema schema, Dictionary<string, List<Row>> tables) Load()
        {
            using var fs = File.OpenRead(_filePath);
            using var br = new BinaryReader(fs);

            var magic = br.ReadBytes(Constants.FileMagicReadLength);
            if (Encoding.ASCII.GetString(magic) != Constants.FileMagic)
                throw new Exception(Constants.InvalidFileError);

            var version = br.ReadInt32();

            var schema = ReadJson<Schema>(br);

            var tableCount = br.ReadInt32();
            var tables = new Dictionary<string, List<Row>>();

            for (int i = 0; i < tableCount; i++)
            {
                var nameLen = br.ReadInt32();
                var name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));

                var rowCount = br.ReadInt32();
                var rows = new List<Row>();

                for (int j = 0; j < rowCount; j++)
                {
                    var row = ReadJson<Row>(br);
                    rows.Add(row);
                }

                tables[name] = rows;
            }

            return (schema, tables);
        }

        public void Save(Schema schema, Dictionary<string, List<Row>> tables)
        {
            using var fs = File.OpenWrite(_filePath);
            fs.SetLength(Constants.TruncatedFileLength); // truncar
            using var bw = new BinaryWriter(fs);

            bw.Write(Encoding.ASCII.GetBytes(Constants.FileMagic));
            bw.Write(Constants.FileVersion); // versión

            WriteJson(bw, schema);

            bw.Write(tables.Count);
            foreach (var kvp in tables)
            {
                var name = kvp.Key;
                var rows = kvp.Value;

                var nameBytes = Encoding.UTF8.GetBytes(name);
                bw.Write(nameBytes.Length);
                bw.Write(nameBytes);

                bw.Write(rows.Count);
                foreach (var row in rows)
                {
                    WriteJson(bw, row);
                }
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