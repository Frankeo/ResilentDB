namespace Engine;

public interface IExecutionContext
{
    Schema Schema { get; }
    IEngineTraceSink? TraceSink => null;
    void Save();
    IPrimaryKeyIndex OpenPrimaryIndex(string tableName);
    void CreatePrimaryIndex(string tableName);
}

public interface IEngineTraceSink
{
    void Write(EngineTraceEvent traceEvent);
}

public sealed record EngineTraceEvent(
    string Component,
    string Operation,
    string Detail,
    int Depth = 0);

public interface IPrimaryKeyIndex
{
    bool ContainsKey(long key);
    bool TryGetRecord(long key, out Row? row);
    void InsertRecord(long key, Row row);
    bool UpdateRecord(long key, Row row);
    bool Delete(long key);
    IReadOnlyList<KeyValuePair<long, Row>> ScanRecords(long? minimum = null, long? maximum = null);
}