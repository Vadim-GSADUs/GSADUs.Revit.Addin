// FILE: src/GSADUs.Revit.Addin/Logging/LogSyncService.cs
// PURPOSE: Sync selection sets into CSV log with deterministic audit fields and legacy cleanup + ModelGroup population
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    internal sealed class LogSyncService : ILogSyncService
    {
        public void EnsureSync(Document doc, IBatchLog log, IBatchLogFactory factory)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (log == null) throw new ArgumentNullException(nameof(log));

            var existing = (log.GetRows() ?? Array.Empty<IReadOnlyDictionary<string, string>>()).ToList();
            var byId = existing
                .Where(r => r != null && r.TryGetValue("SetId", out var id) && !string.IsNullOrWhiteSpace(id))
                .ToDictionary(r => r["SetId"], StringComparer.OrdinalIgnoreCase);

            var filters = new FilteredElementCollector(doc)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .ToList();

            var nameToIds = filters
                .GroupBy(s => s.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.UniqueId).ToList(), StringComparer.OrdinalIgnoreCase);

            // Collect element -> SetName for Valid sets (first wins)
            var elemToName = new Dictionary<int, string>();

            foreach (var s in filters)
            {
                var setId = s.UniqueId;
                var setName = s.Name ?? string.Empty;

                var memberIds = (s.GetElementIds() ?? new List<ElementId>())
                    .Select(id => ToInt(id))
                    .OrderBy(x => x)
                    .ToList();

                var memberCount = memberIds.Count;
                var membersHash = HashUtil.Fnv1a64Hex(memberIds);

                byId.TryGetValue(setId, out var prev);
                var hadIgnore = prev != null && prev.TryGetValue("IgnoreFlag", out var ig) && ig.Equals("true", StringComparison.OrdinalIgnoreCase);

                var duplicateName = nameToIds.TryGetValue(setName, out var idsForName) && idsForName.Count > 1;
                var emptySet = memberCount == 0;
                var isAmbiguous = duplicateName || emptySet;

                string auditStatus = hadIgnore ? "Ignored"
                    : isAmbiguous ? "Ambiguous"
                    : "Valid";

                // Collect ModelGroup targets only for Valid sets (first wins)
                if (auditStatus == "Valid")
                {
                    try
                    {
                        var origIds = s.GetElementIds() ?? new List<ElementId>();
                        foreach (var eid in origIds)
                        {
                            int key = ToInt(eid);
                            if (key == 0) continue;
                            if (!elemToName.ContainsKey(key)) elemToName[key] = setName; // first wins
                        }
                    }
                    catch { }
                }

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SetName"] = setName,
                    ["MemberCount"] = memberCount.ToString(CultureInfo.InvariantCulture),
                    ["MembersHash"] = membersHash,
                    ["AuditDate"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    ["AuditStatus"] = auditStatus,
                    ["AmbiguityNote"] = duplicateName ? "Duplicate SetName" : (emptySet ? "Empty set" : string.Empty)
                };

                log.Upsert(setId, values);

                // Keep model SelectionFilterElement name synchronized with logged SetName
                try
                {
                    using (var tx = new Transaction(doc, "Rename SelectionFilterElement"))
                    {
                        tx.Start();
                        try
                        {
                            var sfe = doc.GetElement(setId) as SelectionFilterElement;
                            if (sfe != null && sfe.Name != setName) sfe.Name = setName;
                            tx.Commit();
                        }
                        catch { try { tx.RollBack(); } catch { } }
                    }
                }
                catch { }

                var legacyId = HashUtil.Fnv1a64Hex("legacy:" + setName);
                if (!string.IsNullOrWhiteSpace(legacyId)) _ = log.Remove(legacyId);
            }

            // Single transaction to write ModelGroup parameter values for Valid set members
            if (elemToName.Count > 0)
            {
                try
                {
                    using (var tx = new Transaction(doc, "Update ModelGroup"))
                    {
                        tx.Start();
                        try
                        {
                            foreach (var kv in elemToName)
                            {
                                Element? el = null; try { el = doc.GetElement(new ElementId(kv.Key)); } catch { }
                                if (el == null) continue;
                                Parameter? p = null; try { p = el.LookupParameter("ModelGroup"); } catch { p = null; }
                                if (p == null || p.StorageType != StorageType.String) continue;
                                string curr = string.Empty; try { curr = p.AsString() ?? string.Empty; } catch { }
                                if (string.Equals(curr, kv.Value, StringComparison.Ordinal)) continue; // already correct
                                try { p.Set(kv.Value); } catch { }
                            }
                            tx.Commit();
                        }
                        catch { try { tx.RollBack(); } catch { } }
                    }
                }
                catch { }
            }

            var liveIds = new HashSet<string>(filters.Select(s => s.UniqueId), StringComparer.OrdinalIgnoreCase);
            foreach (var row in existing)
            {
                if (!row.TryGetValue("SetId", out var sid) || string.IsNullOrWhiteSpace(sid)) continue;
                if (liveIds.Contains(sid)) continue;
                var update = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AuditStatus"] = "Deleted",
                    ["AmbiguityNote"] = "Set missing in model",
                    ["AuditDate"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                };
                log.Upsert(sid, update);
            }
        }

        private static int ToInt(ElementId id)
        {
            if (id == null) return 0;
            try
            {
                var prop = id.GetType().GetProperty("IntegerValue");
                if (prop != null)
                {
                    var val = prop.GetValue(id);
                    if (val is int i) return i;
                }
            }
            catch { }
            try { return id.GetHashCode(); } catch { return 0; }
        }
    }
}
