using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin.Workflows.Rvt
{
    // Basic cleanup skeleton. Focuses on safe deletion ordering and simple preserve logic.
    // TODO: add JSON persistence, detailed logging, UI-configurable whitelist/blacklist, BB/IBB spatial logic.
    public sealed class CleanupOptions
    {
        public bool DeleteAreasFirst { get; set; } = true; // kept for compatibility; we delete areas in a first pass
        public int DeleteBatchSize { get; set; } = 20000; // larger default to minimize calls
        // Hardcoded for first iteration; move to Settings later
        public HashSet<BuiltInCategory> NeverDelete { get; } = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_ProjectBasePoint,
            BuiltInCategory.OST_SharedBasePoint,
            BuiltInCategory.OST_ProjectInformation,
            BuiltInCategory.OST_Levels,
            // Grids intentionally NOT listed here: treat as deletable unless preserved by selection set
            BuiltInCategory.OST_Views,
            BuiltInCategory.OST_Sheets,
            BuiltInCategory.OST_ScheduleGraphics,
            BuiltInCategory.OST_TitleBlocks,
            BuiltInCategory.OST_RvtLinks,
            BuiltInCategory.OST_Cameras,
            BuiltInCategory.OST_VolumeOfInterest, // Scope Boxes
            BuiltInCategory.OST_AreaSchemes,
        };

        // Optional: skip elements inside groups to avoid many delete failures (groups often block delete)
        public bool SkipElementsInGroups { get; set; } = true;
    }

    public sealed class CleanupReport
    {
        public int PreservedFound { get; set; }
        public int AreasDeleted { get; set; }
        public int AreaBoundariesDeleted { get; set; }
        public int ModelAndAnnoDeleted { get; set; }
        public int Errors { get; set; }
        public bool SkippedDueToNoPreserve { get; set; }
        // New diagnostic fields
        public string? FirstErrorMessage { get; set; }
        public string? FirstFailureMessage { get; set; }
        public int FailureMessagesCount { get; set; }
        public int FailureWarningsSuppressed { get; set; }
    }

    // Optional diagnostics used by dry-run to understand scale/timing
    public sealed class CleanupDiagnostics
    {
        public int Candidates { get; set; }
        public int Batches { get; set; }
    }

    // Precomputed plan of what can be deleted; store UniqueIds so it can be reused on file copies.
    public sealed class DeletePlan
    {
        public HashSet<string> AreaUids { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OtherUids { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int CandidateCount => AreaUids.Count + OtherUids.Count;
    }

    internal sealed class CleanupFailures : IFailuresPreprocessor
    {
        private readonly List<string> _messages;
        private readonly bool _suppressWarnings;
        public int WarningsSuppressed { get; private set; }
        public CleanupFailures(List<string> messages, bool suppressWarnings)
        {
            _messages = messages ?? new List<string>();
            _suppressWarnings = suppressWarnings;
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            try
            {
                var failIds = a.GetFailureMessages();
                foreach (var f in failIds)
                {
                    try
                    {
                        var txt = f.GetDescriptionText();
                        if (!string.IsNullOrWhiteSpace(txt) && _messages.Count < 200)
                            _messages.Add(txt);
                    }
                    catch { }

                    if (_suppressWarnings && f.GetSeverity() == FailureSeverity.Warning)
                    {
                        try { a.DeleteWarning(f); } catch { }
                        WarningsSuppressed++;
                    }
                }
            }
            catch { }
            return FailureProcessingResult.Continue;
        }
    }

    public static class ExportCleanup
    {
        // Names that should be excluded from deletion to avoid breaking host sketches and room/area bounds
        private static readonly HashSet<string> ExcludedCategoryNames = new HashSet<string>(StringComparer.Ordinal)
        {
            // Seen in UI depending on locale/version
            "<Sketch>",
            "<Area Boundary>",
            "Area Boundary Lines",
            "Room Separation Lines",
        };

        // Build a delete plan on the SOURCE document to be reused across exported copies.
        // Includes only Model elements and view-specific Annotation elements.
        // Excludes blacklisted categories and sketch/boundary lines.
        // Special-case: include Grids (non-view-specific annotation) so they can be deleted unless preserved.
        public static DeletePlan BuildDeletePlan(Document doc, CleanupOptions? options = null)
        {
            options ??= new CleanupOptions();
            var plan = new DeletePlan();

            // Cache never-delete CategoryId's for quick checks
            var neverDeleteCatIds = new HashSet<ElementId>();
            foreach (var bic in options.NeverDelete)
            {
                var cat = Category.GetCategory(doc, bic);
                if (cat != null) neverDeleteCatIds.Add(cat.Id);
            }
            var areaCat = Category.GetCategory(doc, BuiltInCategory.OST_Areas);
            var areaCatId = areaCat != null ? areaCat.Id : ElementId.InvalidElementId;
            var roomCat = Category.GetCategory(doc, BuiltInCategory.OST_Rooms);
            var roomCatId = roomCat != null ? roomCat.Id : ElementId.InvalidElementId;
            var gridCat = Category.GetCategory(doc, BuiltInCategory.OST_Grids);
            var gridCatId = gridCat?.Id;

            var all = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var e in all)
            {
                if (e == null) continue;
                var cat = e.Category;
                if (cat == null) continue;

                // Exclude blacklisted categories
                if (neverDeleteCatIds.Contains(cat.Id)) continue;

                // Exclude roots and non-target types
                if (e is View || e is ViewSchedule || e is ViewSheet) continue;

                // Only model and annotation; for annotation prefer view-specific (has OwnerViewId)
                var ctype = cat.CategoryType;
                if (ctype != CategoryType.Model && ctype != CategoryType.Annotation) continue;
                if (ctype == CategoryType.Annotation)
                {
                    try
                    {
                        // If not view-specific and not a Grid, skip (keep other global annotations like levels)
                        if (e.OwnerViewId == ElementId.InvalidElementId && (gridCatId == null || !cat.Id.Equals(gridCatId)))
                            continue;
                    }
                    catch { continue; }
                }

                // Exclude sketch-related and boundary categories by localized UI names
                try { if (!string.IsNullOrEmpty(cat.Name) && ExcludedCategoryNames.Contains(cat.Name)) continue; } catch { }

                // Optionally skip elements that belong to a group
                if (options.SkipElementsInGroups)
                {
                    try { if (e.GroupId != ElementId.InvalidElementId) continue; } catch { }
                }

                var uid = e.UniqueId;
                if (string.IsNullOrWhiteSpace(uid)) continue;

                // First pass: Areas and Rooms
                if ((areaCatId != ElementId.InvalidElementId && cat.Id.Equals(areaCatId)) ||
                    (roomCatId != ElementId.InvalidElementId && cat.Id.Equals(roomCatId)))
                {
                    plan.AreaUids.Add(uid);
                }
                else
                {
                    plan.OtherUids.Add(uid);
                }
            }

            return plan;
        }

        public static CleanupReport Run(
            Document doc,
            IEnumerable<string> preserveUniqueIds,
            CleanupOptions? options = null,
            CleanupDiagnostics? diagnostics = null,
            DeletePlan? plan = null,
            bool suppressWarnings = true)
        {
            options ??= new CleanupOptions();
            var report = new CleanupReport();

            // Map preserve UniqueIds -> ElementIds in this document
            var preserveIds = new HashSet<ElementId>();
            foreach (var uid in preserveUniqueIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(uid)) continue;
                var el = doc.GetElement(uid);
                if (el != null) preserveIds.Add(el.Id);
            }
            report.PreservedFound = preserveIds.Count;

            using (var tg = new TransactionGroup(doc, "Export Cleanup"))
            {
                tg.Start();

                var toDeleteAreas = new List<ElementId>();
                var toDeleteOthers = new List<ElementId>();

                if (plan != null)
                {
                    // Use cached plan: subtract preserved and map to ElementIds in this document
                    foreach (var uid in plan.AreaUids)
                    {
                        var el = doc.GetElement(uid);
                        if (el == null) continue;
                        if (preserveIds.Contains(el.Id)) continue;
                        toDeleteAreas.Add(el.Id);
                    }
                    foreach (var uid in plan.OtherUids)
                    {
                        var el = doc.GetElement(uid);
                        if (el == null) continue;
                        if (preserveIds.Contains(el.Id)) continue;
                        toDeleteOthers.Add(el.Id);
                    }
                }
                else
                {
                    // Build on the fly (fallback)
                    var built = BuildDeletePlan(doc, options);
                    foreach (var uid in built.AreaUids)
                    {
                        var el = doc.GetElement(uid);
                        if (el == null) continue;
                        if (preserveIds.Contains(el.Id)) continue;
                        toDeleteAreas.Add(el.Id);
                    }
                    foreach (var uid in built.OtherUids)
                    {
                        var el = doc.GetElement(uid);
                        if (el == null) continue;
                        if (preserveIds.Contains(el.Id)) continue;
                        toDeleteOthers.Add(el.Id);
                    }
                }

                if (diagnostics != null) diagnostics.Candidates = toDeleteAreas.Count + toDeleteOthers.Count;

                // Safety fence: if nothing preserved could be resolved AND there is no precomputed plan work, skip
                if (report.PreservedFound == 0 && (toDeleteAreas.Count + toDeleteOthers.Count) == 0)
                {
                    report.SkippedDueToNoPreserve = true;
                    tg.Assimilate();
                    return report;
                }

                using (var tx = new Transaction(doc, "Export Cleanup: Delete Elements"))
                {
                    tx.Start();
                    try
                    {
                        // Collect failure messages from Revit during the transaction
                        var failureMessages = new List<string>();
                        var preproc = new CleanupFailures(failureMessages, suppressWarnings);
                        var fopts = tx.GetFailureHandlingOptions();
                        fopts.SetFailuresPreprocessor(preproc);
                        fopts.SetClearAfterRollback(true);
                        tx.SetFailureHandlingOptions(fopts);

                        int batches = 0;
                        int deletedAreas = 0;
                        int deletedOthers = 0;

                        // 1) Delete Areas and Rooms first
                        if (toDeleteAreas.Count > 0)
                        {
                            try
                            {
                                var res = doc.Delete(toDeleteAreas);
                                deletedAreas += (res?.Count ?? 0);
                                batches++;
                            }
                            catch (Exception ex)
                            {
                                if (string.IsNullOrEmpty(report.FirstErrorMessage)) report.FirstErrorMessage = "Areas/Rooms: " + ex.Message;
                                deletedAreas += DeleteWithFallback(doc, toDeleteAreas, ref batches, report, options.DeleteBatchSize);
                            }
                        }

                        // 2) Delete remaining elements
                        if (toDeleteOthers.Count > 0)
                        {
                            try
                            {
                                var res = doc.Delete(toDeleteOthers);
                                deletedOthers += (res?.Count ?? 0);
                                batches++;
                            }
                            catch (Exception ex)
                            {
                                if (string.IsNullOrEmpty(report.FirstErrorMessage)) report.FirstErrorMessage = "Others: " + ex.Message;
                                deletedOthers += DeleteWithFallback(doc, toDeleteOthers, ref batches, report, options.DeleteBatchSize);
                            }
                        }

                        if (diagnostics != null) diagnostics.Batches = batches;

                        report.AreasDeleted = deletedAreas; // now includes Rooms as well
                        report.ModelAndAnnoDeleted = deletedOthers;
                        report.FailureMessagesCount = failureMessages.Count;
                        report.FirstFailureMessage = failureMessages.Count > 0 ? failureMessages[0] : null;
                        report.FailureWarningsSuppressed = preproc.WarningsSuppressed;

                        tx.Commit();
                    }
                    catch
                    {
                        try { tx.RollBack(); } catch { }
                        throw;
                    }
                }

                tg.Assimilate();
            }

            return report;
        }

        // Recursively split the list and delete halves to minimize exceptions cost. Caps leaf size by batchSize.
        private static int DeleteWithFallback(Document doc, IList<ElementId> ids, ref int batches, CleanupReport report, int batchSize)
        {
            if (ids == null || ids.Count == 0) return 0;
            if (ids.Count <= batchSize)
            {
                try
                {
                    var res = doc.Delete(ids);
                    batches++;
                    return res?.Count ?? 0;
                }
                catch (Exception ex)
                {
                    if (string.IsNullOrEmpty(report.FirstErrorMessage)) report.FirstErrorMessage = ex.Message;
                    // If even this batch fails, try singletons
                    int sum = 0;
                    for (int i = 0; i < ids.Count; i++)
                    {
                        var single = ids[i];
                        try
                        {
                            var res1 = doc.Delete(single);
                            batches++;
                            sum += (res1?.Count ?? 0);
                        }
                        catch (Exception ex1) { report.Errors++; if (string.IsNullOrEmpty(report.FirstErrorMessage)) report.FirstErrorMessage = ex1.Message; }
                    }
                    return sum;
                }
            }
            else
            {
                // Split and conquer
                int mid = ids.Count / 2;
                var left = ids.Take(mid).ToList();
                var right = ids.Skip(mid).ToList();
                int sum = 0;
                sum += DeleteWithFallback(doc, left, ref batches, report, batchSize);
                sum += DeleteWithFallback(doc, right, ref batches, report, batchSize);
                return sum;
            }
        }
    }
}
