using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GSADUs.Revit.Addin
{
    internal static class HashUtil
    {
        // FNV-1a 64-bit constants
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        // One-time logging flags for legacy methods
        private static bool _loggedLegacyFnvRaw;
        private static bool _loggedLegacyUids;
        private static bool _loggedLegacyAnno;

        // LEGACY: original implementation retained for migration until removed.
        [Obsolete("Use Fnv1a64Hex instead. Pending removal.", false)]
        public static ulong Fnv1a64(string s)
        {
            if (!_loggedLegacyFnvRaw)
            {
                _loggedLegacyFnvRaw = true; try { PerfLogger.Write("Legacy.HashUtil", "Fnv1a64 invoked", TimeSpan.Zero); } catch { }
            }
            if (s == null) s = string.Empty;
            ulong hash = FnvOffset;
            foreach (var b in System.Text.Encoding.UTF8.GetBytes(s))
            {
                hash ^= b;
                hash *= FnvPrime;
            }
            return hash;
        }

        // New canonical hex helper (uppercase, fixed 16 chars)
        public static string Fnv1a64Hex(string input)
        {
            if (input == null) input = string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
            ulong h = FnvOffset;
            foreach (var b in bytes) { h ^= b; h *= FnvPrime; }
            return h.ToString("X16");
        }

        // Overload for integer collections (sort ascending, join with ';')
        public static string Fnv1a64Hex(IEnumerable<int> ints)
        {
            if (ints == null) return Fnv1a64Hex(string.Empty);
            var joined = string.Join(";", ints.OrderBy(x => x));
            return Fnv1a64Hex(joined);
        }

        // Legacy helper (will be superseded by MembersHash in v2). Uses UID list.
        [Obsolete("Use MembersHash (Fnv1a64Hex of int ids) instead. Pending removal.", false)]
        public static string ComputeHashFromUids(IEnumerable<string> uids)
        {
            if (!_loggedLegacyUids)
            {
                _loggedLegacyUids = true; try { PerfLogger.Write("Legacy.HashUtil", "ComputeHashFromUids invoked", TimeSpan.Zero); } catch { }
            }
            if (uids == null) return "";
            var joined = string.Join("|", uids.Where(u => !string.IsNullOrWhiteSpace(u)).OrderBy(u => u, StringComparer.Ordinal));
            return Fnv1a64Hex(joined);
        }

        // Deep annotation hash retained temporarily (may be removed in schema v2 cleanup)
        [Obsolete("Annotation deep hash retained temporarily; pending removal if unused.", false)]
        public static string ComputeDeepAnnoHash(Document doc, IEnumerable<ElementId> ids)
        {
            if (!_loggedLegacyAnno)
            {
                _loggedLegacyAnno = true; try { PerfLogger.Write("Legacy.HashUtil", "ComputeDeepAnnoHash invoked", TimeSpan.Zero); } catch { }
            }
            if (doc == null || ids == null) return string.Empty;
            var parts = new List<string>();
            foreach (var id in ids)
            {
                Element el = null;
                try { el = doc.GetElement(id); } catch { }
                if (el == null) continue;
                var cat = el.Category;
                if (cat == null || cat.CategoryType != CategoryType.Annotation) continue;

                var sb = new StringBuilder();
                sb.Append(el.UniqueId ?? el.Id.ToString());
                sb.Append("|"); sb.Append(cat.Name ?? "");

                try
                {
                    var pars = el.Parameters;
                    foreach (Parameter p in pars)
                    {
                        if (p == null || p.Definition == null) continue;
                        var name = p.Definition.Name;
                        string val = null;
                        try
                        {
                            switch (p.StorageType)
                            {
                                case StorageType.String:
                                    val = p.AsString();
                                    break;
                                case StorageType.Double:
                                case StorageType.Integer:
                                    val = p.AsValueString();
                                    break;
                                default:
                                    val = null;
                                    break;
                            }
                        }
                        catch { val = null; }
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(val))
                        {
                            sb.Append("|"); sb.Append(name); sb.Append("="); sb.Append(val);
                        }
                    }
                }
                catch { }

                parts.Add(sb.ToString());
            }

            if (parts.Count == 0) return string.Empty;
            parts.Sort(StringComparer.Ordinal);
            var joined2 = string.Join("\n", parts);
            return Fnv1a64Hex(joined2);
        }
    }
}
