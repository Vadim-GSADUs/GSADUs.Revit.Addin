using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GSADUs.Revit.Addin
{
    // Per-document cache for category ids and non-template view ids used by audit compute.
    internal static class AuditComputeCache
    {
        internal sealed class CatsViews
        {
            public string SettingsKey = string.Empty;
            public HashSet<ElementId> Seed = new();
            public HashSet<ElementId> Proxy = new();
            public HashSet<ElementId> Blacklist = new();
            public List<ElementId>? ViewIds; // non-template views
        }

        private static readonly ConditionalWeakTable<Document, CatsViews> _cache = new();

        private static string KeyFrom(AppSettings s)
        {
            string Join(IEnumerable<int>? xs) => xs == null ? string.Empty : string.Join(',', xs.OrderBy(i => i));
            return string.Join('|',
                Join(s.SelectionSeedCategories),
                Join(s.SelectionProxyCategories),
                Join(s.CleanupBlacklistCategories));
        }

        public static CatsViews GetOrBuild(Document doc, AppSettings settings)
        {
            if (!_cache.TryGetValue(doc, out var cv))
            {
                cv = new CatsViews();
                _cache.Add(doc, cv);
            }

            var key = KeyFrom(settings);
            if (!string.Equals(cv.SettingsKey, key, StringComparison.Ordinal))
            {
                // Rebuild category id sets
                cv.SettingsKey = key;
                cv.Seed = new HashSet<ElementId>();
                cv.Proxy = new HashSet<ElementId>();
                cv.Blacklist = new HashSet<ElementId>();

                foreach (var i in settings.SelectionSeedCategories ?? Enumerable.Empty<int>())
                {
                    try { var bic = (BuiltInCategory)i; var c = Category.GetCategory(doc, bic); if (c != null) cv.Seed.Add(c.Id); } catch { }
                }
                foreach (var i in settings.SelectionProxyCategories ?? Enumerable.Empty<int>())
                {
                    try { var bic = (BuiltInCategory)i; var c = Category.GetCategory(doc, bic); if (c != null) cv.Proxy.Add(c.Id); } catch { }
                }
                foreach (var i in settings.CleanupBlacklistCategories ?? Enumerable.Empty<int>())
                {
                    try { var bic = (BuiltInCategory)i; var c = Category.GetCategory(doc, bic); if (c != null) cv.Blacklist.Add(c.Id); } catch { }
                }
            }

            // Views list is independent; build once lazily
            if (cv.ViewIds == null)
            {
                try
                {
                    cv.ViewIds = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => v != null && !v.IsTemplate)
                        .Select(v => v.Id)
                        .ToList();
                }
                catch { cv.ViewIds = new List<ElementId>(); }
            }

            return cv;
        }
    }
}
