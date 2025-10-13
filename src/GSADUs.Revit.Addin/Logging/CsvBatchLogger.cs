using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    internal sealed class CsvBatchLogger : IBatchLog, IBatchLogFactory
    {
        // IBatchLogFactory ----------------------------------------------------
        public IBatchLog Load(string path) => new CsvBatchLoggerProxy(BatchLog.Load(path));
        public IReadOnlyList<string> ReadHeadersOrDefaults(string path) => BatchLog.ReadHeadersOrDefaults(path);
        public string NowStamp() => BatchLog.NowStamp(); // legacy
        public string NowIso() => System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // IBatchLog (root logger is only a factory; these are no-op implementations)
        public void Upsert(string setId, IReadOnlyDictionary<string, string> values) { }
        public void Update(string setId, IReadOnlyDictionary<string, string> values) { }
        public void EnsureColumns(IEnumerable<string> headers) { }
        public IReadOnlyList<string> Headers => new List<string>();
        public void Save(string path) { }
        public IReadOnlyDictionary<string, string>? GetRow(string setId) => null;
        public IEnumerable<IReadOnlyDictionary<string, string>> GetRows() { yield break; }
        public bool Remove(string setId) => false; // root has no rows

        // Proxy that wraps actual BatchLog instance --------------------------------
        private sealed class CsvBatchLoggerProxy : IBatchLog
        {
            private readonly BatchLog _inner;
            public CsvBatchLoggerProxy(BatchLog inner) { _inner = inner; }

            public void Upsert(string setId, IReadOnlyDictionary<string, string> values) => _inner.Update(setId, values);
            public void Update(string setId, IReadOnlyDictionary<string, string> values) => _inner.Update(setId, values);
            public void EnsureColumns(IEnumerable<string> headers)
            {
                if (headers == null) return;
                foreach (var h in headers.Where(h => !string.IsNullOrWhiteSpace(h)))
                {
                    try { _inner.EnsureColumn(h); } catch { }
                }
            }
            public IReadOnlyList<string> Headers => BatchLog.ReadHeadersOrDefaults(""); // placeholder until BatchLog exposes headers
            public IReadOnlyDictionary<string, string>? GetRow(string setId) => _inner.GetRow(setId);
            public IEnumerable<IReadOnlyDictionary<string, string>> GetRows() => _inner.GetRows();
            public void Save(string path) => _inner.Save(path);
            public bool Remove(string setId) => _inner.Remove(setId);
        }
    }
}
