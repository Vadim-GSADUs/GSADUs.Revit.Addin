using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GSADUs.Revit.Addin
{
    // V2 scaffold implementing new schema + legacy migration (updated lean core schema)
    internal sealed class BatchLog : IBatchLog
    {
        // Lean Core v2 columns (ordered) – removed __SchemaVersion marker (no longer used)
        private static readonly string[] CoreV2 = new[]
        {
            "SetId","SetName","MemberCount","MembersHash","AuditDate","AuditStatus",
            "IgnoreFlag","IgnoreReason","AmbiguityNote"
        };

        private readonly List<Dictionary<string, string>> _rows = new();
        private readonly HashSet<string> _headers = new(StringComparer.OrdinalIgnoreCase);
        private bool _loadedFromLegacy; // signals migration fallback permitted

        public IReadOnlyList<string> Headers => _headers.ToList();

        // --- Added: ID validation (only accept real Revit-style UniqueIds) ---
        // PURPOSE: prevent accidental SetName or short/hash placeholders becoming SetId
        private static bool IsValidId(string setId, string? setName)
        {
            if (string.IsNullOrWhiteSpace(setId)) return false;
            if (setId.Length < 20) return false;          // Revit UniqueIds are long
            if (!setId.Contains('-')) return false;       // Basic pattern presence
            if (!string.IsNullOrEmpty(setName) && string.Equals(setId, setName, StringComparison.OrdinalIgnoreCase)) return false; // reject name-equal ids
            return true;
        }

        // Optional helper to purge any invalid rows (not strictly required but available)
        private void RemoveInvalidRows()
        {
            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                var r = _rows[i];
                if (!r.TryGetValue("SetId", out var sid)) { _rows.RemoveAt(i); continue; }
                r.TryGetValue("SetName", out var sname);
                if (!IsValidId(sid, sname)) _rows.RemoveAt(i);
            }
        }

        // --- Public (v2) helpers ---
        public static string NowIso() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // Legacy timestamp retained for migration callers
        public static string NowStamp() => DateTime.Now.ToString("MM/dd/yy HH:mm", CultureInfo.InvariantCulture);

        // IBatchLog: ensure columns (idempotent – never remove)
        public void EnsureColumns(IEnumerable<string> headers)
        {
            if (headers == null) return;
            foreach (var h in headers)
            {
                if (string.IsNullOrWhiteSpace(h)) continue;
                _headers.Add(h);
            }
            foreach (var core in CoreV2) _headers.Add(core);
        }

        // Upsert by SetId; only supplied fields changed
        public void Upsert(string setId, IReadOnlyDictionary<string, string> values)
        {
            // Validation gate: silently ignore invalid IDs to avoid polluting log with name/hash keys
            string? setName = null;
            try { values?.TryGetValue("SetName", out setName); } catch { }
            if (!IsValidId(setId, setName)) return; // skip entirely

            if (string.IsNullOrWhiteSpace(setId)) throw new ArgumentException("setId required", nameof(setId));
            var row = _rows.FirstOrDefault(r => r.TryGetValue("SetId", out var v) && string.Equals(v, setId, StringComparison.OrdinalIgnoreCase));
            if (row == null)
            {
                row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["SetId"] = setId };
                _rows.Add(row);
            }
            foreach (var kv in values)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                _headers.Add(kv.Key);
                row[kv.Key] = kv.Value ?? string.Empty;
            }
            // Ensure all core columns exist (values may stay blank)
            foreach (var c in CoreV2) _headers.Add(c);
        }

        // Legacy alias (IBatchLog default also maps) – provided explicitly for older proxies
        public void Update(string setId, IReadOnlyDictionary<string, string> values) => Upsert(setId, values);

        public IReadOnlyDictionary<string, string>? GetRow(string setId)
        {
            if (string.IsNullOrWhiteSpace(setId)) return null;
            var row = _rows.FirstOrDefault(r => r.TryGetValue("SetId", out var v) && string.Equals(v, setId, StringComparison.OrdinalIgnoreCase));
            return row == null ? null : new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase);
        }

        public IEnumerable<IReadOnlyDictionary<string, string>> GetRows()
        {
            // Return copies (stable for UI binding)
            foreach (var r in _rows)
                yield return new Dictionary<string, string>(r, StringComparer.OrdinalIgnoreCase);
        }

        public bool Remove(string setId)
        {
            if (string.IsNullOrWhiteSpace(setId)) return false;
            var idx = _rows.FindIndex(r => r.TryGetValue("SetId", out var v) && string.Equals(v, setId, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;
            _rows.RemoveAt(idx);
            return true;
        }

        // CSV SAVE (writes v2 schema). CoreV2 first, then remaining (alphabetical) deterministic.
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            foreach (var c in CoreV2) _headers.Add(c);
            var extra = _headers.Where(h => !CoreV2.Contains(h, StringComparer.OrdinalIgnoreCase))
                                .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            var ordered = CoreV2.Concat(extra).ToList();

            var sb = new StringBuilder();
            sb.AppendLine(ToCsvLine(ordered));
            foreach (var row in _rows.OrderBy(r => r.GetValueOrDefault("SetName") ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                var fields = ordered.Select(h => row.GetValueOrDefault(h, string.Empty)).ToList();
                sb.AppendLine(ToCsvLine(fields));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        // Static: Read headers (legacy aware)
        public static IReadOnlyList<string> ReadHeadersOrDefaults(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var first = File.ReadLines(path, Encoding.UTF8).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(first))
                    {
                        var hdrs = ParseCsvLine(first).Select(h => (h ?? string.Empty).Trim()).Where(h => !string.IsNullOrEmpty(h)).ToList();
                        if (hdrs.Count > 0) return hdrs;
                    }
                }
            }
            catch { }
            return CoreV2;
        }

        // Static: Load + migrate legacy if needed
        public static BatchLog Load(string path)
        {
            var log = new BatchLog();
            if (!File.Exists(path))
            {
                log.EnsureColumns(CoreV2);
                return log;
            }
            var lines = File.ReadAllLines(path, Encoding.UTF8)
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .ToList();
            if (lines.Count == 0)
            {
                log.EnsureColumns(CoreV2);
                return log;
            }
            var headerCells = ParseCsvLine(lines[0]).Select(h => (h ?? string.Empty).Trim()).ToList();
            bool hasSetId = headerCells.Any(h => string.Equals(h, "SetId", StringComparison.OrdinalIgnoreCase));
            bool isLegacy = !hasSetId;
            if (isLegacy) log._loadedFromLegacy = true;

            if (!isLegacy)
            {
                foreach (var h in headerCells) log._headers.Add(h);
            }
            else
            {
                foreach (var h in headerCells)
                {
                    if (string.Equals(h, "Export Date", StringComparison.OrdinalIgnoreCase)) continue; // dropped
                    log._headers.Add(h);
                }
            }

            for (int i = 1; i < lines.Count; i++)
            {
                var cells = ParseCsvLine(lines[i]);
                if (cells.Count == 0) continue;
                var rowMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < cells.Count && c < headerCells.Count; c++)
                {
                    var col = headerCells[c];
                    if (string.IsNullOrWhiteSpace(col)) continue;
                    if (string.Equals(col, "Export Date", StringComparison.OrdinalIgnoreCase)) continue;
                    rowMap[col] = cells[c] ?? string.Empty;
                }

                if (isLegacy)
                {
                    var setName = rowMap.GetValueOrDefault("Key") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(setName)) continue;
                    var syntheticId = HashUtil.Fnv1a64Hex("legacy:" + setName);
                    rowMap["SetId"] = syntheticId;
                    rowMap["SetName"] = setName;
                    if (rowMap.TryGetValue("CurrentHash", out var ch) && !string.IsNullOrWhiteSpace(ch)) rowMap["MembersHash"] = ch;
                    if (rowMap.TryGetValue("Date", out var dt) && !string.IsNullOrWhiteSpace(dt)) rowMap["AuditDate"] = dt;
                    if (rowMap.TryGetValue("Status", out var st) && !string.IsNullOrWhiteSpace(st)) rowMap["AuditStatus"] = st;
                }

                var sid = rowMap.GetValueOrDefault("SetId") ?? string.Empty;
                var sname = rowMap.GetValueOrDefault("SetName");
                if (!IsValidId(sid, sname)) continue;
                log._rows.Add(rowMap);
            }

            log.EnsureColumns(CoreV2);
            log.RemoveInvalidRows();
            return log;
        }

        internal void EnsureColumn(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _headers.Add(name);
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result;
            var cur = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                        else { inQuotes = false; }
                    }
                    else cur.Append(ch);
                }
                else
                {
                    if (ch == ',') { result.Add(cur.ToString()); cur.Clear(); }
                    else if (ch == '"') { inQuotes = true; }
                    else cur.Append(ch);
                }
            }
            result.Add(cur.ToString());
            return result;
        }

        private static string ToCsvLine(IReadOnlyList<string> fields)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(fields[i] ?? string.Empty));
            }
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            bool needQuotes = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
            if (!needQuotes) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
