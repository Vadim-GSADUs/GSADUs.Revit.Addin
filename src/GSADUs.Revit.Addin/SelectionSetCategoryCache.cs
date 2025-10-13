using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    internal static class SelectionSetCategoryCache
    {
        private static readonly Dictionary<string, HashSet<int>> _byDoc = new(StringComparer.OrdinalIgnoreCase);

        private static string? SafeDocKey(Document doc)
        {
            try { return doc?.PathName ?? doc?.Title; } catch { return null; }
        }

        public static IReadOnlyCollection<int> GetOrBuild(Document doc)
        {
            var key = SafeDocKey(doc);
            if (key == null) return Array.Empty<int>();
            if (_byDoc.TryGetValue(key, out var cached)) return cached;
            return Refresh(doc);
        }

        public static IReadOnlyCollection<int> Refresh(Document doc)
        {
            var key = SafeDocKey(doc);
            if (key == null) return Array.Empty<int>();

            var result = new HashSet<int>();

            // Build reverse map: Category.Id -> BuiltInCategory (using ToKey compatible with codebase)
            var reverse = new Dictionary<int, BuiltInCategory>();
            foreach (var bic in Enum.GetValues(typeof(BuiltInCategory)).Cast<BuiltInCategory>())
            {
                try
                {
                    var c = Category.GetCategory(doc, bic);
                    if (c != null)
                    {
                        var cid = ToKey(c.Id);
                        if (cid.HasValue && !reverse.ContainsKey(cid.Value)) reverse.Add(cid.Value, bic);
                    }
                }
                catch { }
            }

            try
            {
                var sets = new FilteredElementCollector(doc)
                    .OfClass(typeof(SelectionFilterElement))
                    .Cast<SelectionFilterElement>()
                    .ToList();
                foreach (var s in sets)
                {
                    var ids = s.GetElementIds();
                    foreach (var id in ids)
                    {
                        Element? e = null; Category? cat = null;
                        try { e = doc.GetElement(id); cat = e?.Category; } catch { }
                        if (cat == null) continue;
                        var cid = ToKey(cat.Id);
                        if (cid.HasValue && reverse.TryGetValue(cid.Value, out var bic))
                            result.Add((int)bic);
                    }
                }
            }
            catch { }

            _byDoc[key] = result;
            return result;
        }

        private static int? ToKey(ElementId id)
        {
            if (id == null) return null;
            try
            {
                var prop = id.GetType().GetProperty("IntegerValue");
                if (prop != null)
                {
                    var val = prop.GetValue(id);
                    if (val is int i) return i;
                    if (val is long l) return checked((int)l);
                }
            }
            catch { }
            try
            {
                var s = id.ToString();
                if (int.TryParse(s, out var i)) return i;
            }
            catch { }
            return null;
        }
    }
}
