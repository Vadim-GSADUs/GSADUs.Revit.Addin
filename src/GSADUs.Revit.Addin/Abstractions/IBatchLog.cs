using System.Collections.Generic;

namespace GSADUs.Revit.Addin
{
    public interface IBatchLog
    {
        // Primary v2 upsert API
        void Upsert(string setId, IReadOnlyDictionary<string, string> values);

        // Legacy alias (default implementation routes to Upsert)
        void Update(string setId, IReadOnlyDictionary<string, string> values) => Upsert(setId, values);

        // Remove row by SetId; returns true if a row was deleted
        bool Remove(string setId);

        // Ensure missing columns exist; never remove existing ones
        void EnsureColumns(IEnumerable<string> headers);

        // Current header list after any ensures
        IReadOnlyList<string> Headers { get; }

        // Row accessors
        IReadOnlyDictionary<string, string>? GetRow(string setId);
        IEnumerable<IReadOnlyDictionary<string, string>> GetRows();

        // Persist
        void Save(string path);
    }

    public interface IBatchLogFactory
    {
        IBatchLog Load(string path);
        IReadOnlyList<string> ReadHeadersOrDefaults(string path);
        // Legacy timestamp (migration period)
        string NowStamp();
        // New ISO helper for v2 schema
        string NowIso();
    }
}
