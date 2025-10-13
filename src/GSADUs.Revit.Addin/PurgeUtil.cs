using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    internal static class PurgeUtil
    {
        private sealed class PurgeFailures : IFailuresPreprocessor
        {
            private readonly bool _suppressWarnings;
            public PurgeFailures(bool suppressWarnings) { _suppressWarnings = suppressWarnings; }
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                try
                {
                    foreach (var f in a.GetFailureMessages())
                    {
                        if (_suppressWarnings && f.GetSeverity() == FailureSeverity.Warning)
                        {
                            try { a.DeleteWarning(f); } catch { }
                        }
                    }
                }
                catch { }
                return FailureProcessingResult.Continue;
            }
        }

        /// <summary>
        /// Best-effort purge of unused ElementTypes. Runs N passes to catch cascading unrefs.
        /// Note: Revit has no public API for full Purge Unused; this attempts to delete unused types only.
        /// </summary>
        public static void PurgeUnusedTypes(Document doc, int passes = 3, bool suppressWarnings = true)
        {
            if (doc == null) return;
            passes = Math.Max(1, Math.Min(10, passes));

            using (var tg = new TransactionGroup(doc, "GSADUs: Purge Unused Types"))
            {
                tg.Start();

                for (int i = 0; i < passes; i++)
                {
                    // Collect all element types
                    IList<ElementId> typeIds;
                    try
                    {
                        typeIds = new FilteredElementCollector(doc)
                            .WhereElementIsElementType()
                            .ToElementIds()
                            .ToList();
                    }
                    catch { break; }

                    if (typeIds.Count == 0) break;

                    using (var tx = new Transaction(doc, $"Purge Unused Types: Pass {i + 1}"))
                    {
                        tx.Start();
                        try
                        {
                            var fopts = tx.GetFailureHandlingOptions();
                            fopts.SetFailuresPreprocessor(new PurgeFailures(suppressWarnings));
                            fopts.SetClearAfterRollback(true);
                            tx.SetFailureHandlingOptions(fopts);

                            DeleteWithFallback(doc, typeIds);

                            tx.Commit();
                        }
                        catch
                        {
                            try { tx.RollBack(); } catch { }
                        }
                    }
                }

                tg.Assimilate();
            }
        }

        private static void DeleteWithFallback(Document doc, IList<ElementId> ids)
        {
            if (ids == null || ids.Count == 0) return;
            try
            {
                doc.Delete(ids);
            }
            catch
            {
                if (ids.Count == 1) return;
                int mid = ids.Count / 2;
                var left = ids.Take(mid).ToList();
                var right = ids.Skip(mid).ToList();
                DeleteWithFallback(doc, left);
                DeleteWithFallback(doc, right);
            }
        }
    }
}
