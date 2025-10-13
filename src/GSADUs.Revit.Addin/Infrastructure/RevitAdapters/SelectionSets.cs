using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    public static class SelectionSets
    {
        public static IReadOnlyList<(string Name, ICollection<ElementId> Ids)> Get(Document doc) =>
          new FilteredElementCollector(doc)
            .OfClass(typeof(SelectionFilterElement))
            .Cast<SelectionFilterElement>()
            .Select(s => (s.Name, s.GetElementIds()))
            .OrderBy(t => t.Name)
            .ToList();

        // --- v2 helpers ---
        public static string ComputeSetId(Document doc, SelectionFilterElement s)
            => s?.UniqueId ?? throw new ArgumentNullException(nameof(s));

        private static int ToInt(ElementId id)
        {
            if (id == null) return 0;
            try
            {
                // Revit API ElementId exposes IntegerValue in most versions; guard via reflection for safety.
                var prop = typeof(ElementId).GetProperty("IntegerValue");
                if (prop != null) return (int)prop.GetValue(id)!;
            }
            catch { }
            try { return id.GetHashCode(); } catch { return 0; }
        }

        public static List<int> GetMemberIdsSorted(Document doc, SelectionFilterElement s)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (s == null) throw new ArgumentNullException(nameof(s));
            var ids = s.GetElementIds() ?? new List<ElementId>();
            var ints = ids.Select(ToInt).OrderBy(x => x).ToList();
            return ints;
        }

        public static string ComputeMemberIdsJoined(Document doc, SelectionFilterElement s)
            => string.Join(";", GetMemberIdsSorted(doc, s));

        public static string ComputeMembersHash(Document doc, SelectionFilterElement s)
            => HashUtil.Fnv1a64Hex(GetMemberIdsSorted(doc, s));
    }
}
