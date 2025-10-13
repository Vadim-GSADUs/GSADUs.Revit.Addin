using Autodesk.Revit.DB;
using System.Runtime.CompilerServices;

namespace GSADUs.Revit.Addin.Workflows.Rvt
{
    // Per-document cache for DeletePlan with clear APIs.
    internal static class DeletePlanCache
    {
        private static readonly ConditionalWeakTable<Document, DeletePlan> _byDoc = new();

        public static void Store(Document doc, DeletePlan plan)
        {
            if (doc == null || plan == null) return;
            try { _byDoc.Remove(doc); } catch { }
            _byDoc.Add(doc, plan);
        }

        public static DeletePlan? Get(Document doc)
        {
            if (doc == null) return null;
            return _byDoc.TryGetValue(doc, out var plan) ? plan : null;
        }

        public static void Clear(Document doc)
        {
            if (doc == null) return;
            try { _byDoc.Remove(doc); } catch { }
        }

        public static void ClearAll()
        {
            // ConditionalWeakTable has no Clear; rebuild by creating a new instance is not possible here.
            // Best-effort: enumerate known keys and remove them.
            try
            {
                var toRemove = new System.Collections.Generic.List<Document>();
                foreach (var kv in _byDoc)
                {
                    if (kv.Key != null) toRemove.Add(kv.Key);
                }
                foreach (var d in toRemove) { try { _byDoc.Remove(d); } catch { } }
            }
            catch { }
        }
    }
}
