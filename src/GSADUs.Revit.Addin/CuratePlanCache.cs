using Autodesk.Revit.DB;
using System;
using System.Runtime.CompilerServices;

namespace GSADUs.Revit.Addin
{
    internal static class CuratePlanCache
    {
        private sealed class Holder
        {
            public CuratePlan? Plan;
            public DateTime When;
        }

        private static readonly ConditionalWeakTable<Document, Holder> _byDoc = new();

        public static void Store(Document? doc, CuratePlan? plan)
        {
            if (doc == null || plan == null) return;
            try { _byDoc.Remove(doc); } catch { }
            _byDoc.Add(doc, new Holder { Plan = plan, When = DateTime.UtcNow });
        }

        public static CuratePlan? Get(Document? doc)
        {
            if (doc == null) return null;
            return _byDoc.TryGetValue(doc, out var h) ? h.Plan : null;
        }

        public static void Invalidate(Document? doc)
        {
            if (doc == null) return;
            try { _byDoc.Remove(doc); } catch { }
        }
    }
}
