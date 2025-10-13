using System;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin.Logging
{
    // IBatchLog decorator: strips legacy columns on every call
    internal sealed class GuardedBatchLog : IBatchLog
    {
        private readonly IBatchLog _inner;
        private GuardedBatchLog(IBatchLog inner) { _inner = inner; }

        public static IBatchLog Wrap(IBatchLog inner) => inner is GuardedBatchLog ? inner : new GuardedBatchLog(inner);

        public IReadOnlyList<string> Headers => _inner.Headers.Where(h => !LegacyGuards.IsBanned(h)).ToList();

        public IEnumerable<IReadOnlyDictionary<string, string>> GetRows()
        {
            foreach (var r in _inner.GetRows())
                yield return r.Where(kv => !LegacyGuards.IsBanned(kv.Key))
                              .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, string>? GetRow(string setId)
        {
            var row = _inner.GetRow(setId);
            if (row == null) return null;
            return row.Where(kv => !LegacyGuards.IsBanned(kv.Key))
                      .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        public void EnsureColumns(IEnumerable<string> headers) =>
            _inner.EnsureColumns(headers.Where(h => !LegacyGuards.IsBanned(h)));

        public void Upsert(string setId, IReadOnlyDictionary<string, string> values)
        {
            var filtered = values.Where(kv => !LegacyGuards.IsBanned(kv.Key))
                                 .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            if (filtered.Count != values.Count)
            {
                try { PerfLogger.Write("BatchExport.LegacyBlocked", string.Join(",", values.Keys), TimeSpan.Zero); } catch { }
            }
            _inner.Upsert(setId, filtered);
        }

        public void Update(string setId, IReadOnlyDictionary<string, string> values) => Upsert(setId, values);

        public bool Remove(string setId) => _inner.Remove(setId);

        public void Save(string path)
        {
            var banned = new HashSet<string>(LegacyGuards.BannedHeaders, StringComparer.OrdinalIgnoreCase);
            _inner.EnsureColumns(_inner.Headers.Where(h => !banned.Contains(h)));
            _inner.Save(path);
        }
    }
}
