using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin.Workflows.Rvt
{
    internal sealed class ExportRvtAction : IExportAction
    {
        public string Id => "export-rvt";
        public int Order => 300;
        public bool RequiresExternalClone => false; // single-action prototype manages its own document lifecycle

        // Hardcoded template path per temporary requirement
        private const string HardcodedTemplatePath = @"G:\\Shared drives\\GSADUs Projects\\Our Models\\0 - CATALOG\\2 - Revit\\Template\\GSADUs Catalog.rte";

        public bool IsEnabled(AppSettings app, BatchExportSettings request)
        {
            try { return request.ActionIds?.Any(a => string.Equals(a, Id, StringComparison.OrdinalIgnoreCase)) == true; } catch { return false; }
        }

        // Duplicate type handler: always use destination types, avoid UI prompts
        private sealed class DuplicateTypeHandler_UseDestination : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                try { return DuplicateTypeAction.UseDestinationTypes; } catch { return DuplicateTypeAction.UseDestinationTypes; }
            }
        }

        public void Execute(UIApplication uiapp, Document sourceDoc, Document? outDoc, string setName, System.Collections.Generic.IList<string> preserveUids, bool isDryRun)
        {
            if (uiapp == null || sourceDoc == null) return;
            var dialogs = ServiceBootstrap.Provider.GetService(typeof(IDialogService)) as IDialogService ?? new DialogService();
            var settings = AppSettingsStore.Load();
            var selectedIds = new HashSet<string>(settings.SelectedWorkflowIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var workflows = (settings.Workflows ?? new List<WorkflowDefinition>())
                .Where(w => w.Output == OutputType.Rvt && selectedIds.Contains(w.Id) && (w.ActionIds?.Any(a => string.Equals(a, Id, StringComparison.OrdinalIgnoreCase)) ?? false))
                .ToList();
            if (workflows.Count == 0) return;

            // Resolve element ids for the set once (prefer preserveUids -> ElementId)
            var memberIdsRaw = new List<ElementId>();
            if (preserveUids != null && preserveUids.Count > 0)
            {
                foreach (var uid in preserveUids)
                {
                    try { var e = sourceDoc.GetElement(uid); if (e != null) memberIdsRaw.Add(e.Id); } catch { }
                }
            }
            else
            {
                try
                {
                    var sfe = new FilteredElementCollector(sourceDoc)
                        .OfClass(typeof(SelectionFilterElement))
                        .Cast<SelectionFilterElement>()
                        .FirstOrDefault(f => string.Equals(f.Name, setName, StringComparison.OrdinalIgnoreCase));
                    if (sfe != null) memberIdsRaw = sfe.GetElementIds()?.ToList() ?? new List<ElementId>();
                }
                catch { }
            }

            // Partition: doc-level (non-work-plane-based), view-specific, and work-plane-based (handled later per-element)
            var docLevelIds = new List<ElementId>();
            var viewGroups = new Dictionary<ElementId, List<ElementId>>();
            var workPlaneBasedIds = new List<ElementId>();
            foreach (var id in memberIdsRaw)
            {
                try
                {
                    var e = sourceDoc.GetElement(id);
                    if (e == null) continue;

                    // Skip elements we never want to copy
                    if (e is View || e is ViewSheet || e is Viewport) continue;

                    bool isViewSpecific = false;
                    try { isViewSpecific = e.ViewSpecific; } catch { isViewSpecific = false; }

                    // Work plane detection via flag or presence of valid SKETCH_PLANE_PARAM
                    bool hasSketchPlaneParam = false;
                    try
                    {
                        var spParam = e.get_Parameter(BuiltInParameter.SKETCH_PLANE_PARAM);
                        hasSketchPlaneParam = spParam != null && spParam.StorageType == StorageType.ElementId && spParam.AsElementId() != ElementId.InvalidElementId;
                    }
                    catch { hasSketchPlaneParam = false; }

                    bool isWorkPlaneBased = IsWorkPlaneBased(e) || hasSketchPlaneParam;
                    if (isWorkPlaneBased)
                    {
                        workPlaneBasedIds.Add(id);
                        continue;
                    }

                    if (!isViewSpecific)
                    {
                        docLevelIds.Add(id);
                        continue;
                    }

                    var ownerViewId = e.OwnerViewId;
                    if (ownerViewId == ElementId.InvalidElementId) continue;
                    if (!viewGroups.TryGetValue(ownerViewId, out var list))
                    {
                        list = new List<ElementId>();
                        viewGroups[ownerViewId] = list;
                    }
                    list.Add(id);
                }
                catch { }
            }

            // Proceed even if no copyable elements; we'll still create and save an empty deliverable
            var templatePath = HardcodedTemplatePath;
            if (string.IsNullOrWhiteSpace(templatePath) || !System.IO.File.Exists(templatePath))
            {
                dialogs.Info("Export RVT", "Template path missing or invalid. Update the hardcoded path or ensure the file exists.");
                return;
            }

            foreach (var wf in workflows)
            {
                var app = uiapp.Application;
                Document? newDoc = null;
                try
                {
                    try { newDoc = app.NewProjectDocument(templatePath); } catch (Exception ex) { dialogs.Info("Export RVT", $"Failed to create document from template:\n{ex.Message}"); continue; }
                    if (newDoc == null) { dialogs.Info("Export RVT", "Failed to create document from template."); continue; }

                    var opts = new CopyPasteOptions();
                    // NEW: avoid modal duplicate-type prompts
                    opts.SetDuplicateTypeNamesHandler(new DuplicateTypeHandler_UseDestination());

                    // Preflight: ensure required work planes (ReferencePlanes and SketchPlanes) exist in destination for doc-level elements
                    EnsureWorkPlanesForElements(sourceDoc, newDoc, docLevelIds);

                    // 1) Copy doc-level (non-work-plane-based) — best effort bulk then per-element fallback
                    if (docLevelIds.Count > 0)
                    {
                        bool bulkSucceeded = false;
                        using (var tx = new Transaction(newDoc, "Copy Model Elements"))
                        {
                            tx.Start();
                            try
                            {
                                ElementTransformUtils.CopyElements(sourceDoc, docLevelIds, newDoc, Transform.Identity, opts);
                                tx.Commit();
                                bulkSucceeded = true;
                            }
                            catch
                            {
                                try { tx.RollBack(); } catch { }
                                bulkSucceeded = false;
                            }
                        }
                        if (!bulkSucceeded)
                        {
                            using (var tx = new Transaction(newDoc, "Copy Model Elements (fallback)"))
                            {
                                tx.Start();
                                foreach (var id in docLevelIds)
                                {
                                    try { ElementTransformUtils.CopyElements(sourceDoc, new List<ElementId> { id }, newDoc, Transform.Identity, opts); } catch { }
                                }
                                try { tx.Commit(); } catch { try { tx.RollBack(); } catch { } }
                            }
                        }
                    }

                    // 2) Copy view-specific groups view-to-view by matching ViewType+Name
                    if (viewGroups.Count > 0)
                    {
                        var dstViewMap = BuildDestViewMap(newDoc);
                        foreach (var kv in viewGroups)
                        {
                            View? srcView = null;
                            try { srcView = sourceDoc.GetElement(kv.Key) as View; } catch { srcView = null; }
                            if (srcView == null || srcView.IsTemplate) continue;

                            var key = ViewKey(srcView);
                            if (string.IsNullOrWhiteSpace(key)) continue;
                            if (!dstViewMap.TryGetValue(key!, out var dstView))
                            {
                                // No matching view in template — skip these annotations
                                continue;
                            }

                            var ids = kv.Value;
                            using (var tx = new Transaction(newDoc, $"Copy View Items -> {dstView.Name}"))
                            {
                                tx.Start();
                                try
                                {
                                    ElementTransformUtils.CopyElements(srcView, ids, dstView, Transform.Identity, opts);
                                    tx.Commit();
                                }
                                catch
                                {
                                    try { tx.RollBack(); } catch { }
                                    // Fallback per-element within views
                                    using (var tx2 = new Transaction(newDoc, $"Copy View Items (fallback) -> {dstView.Name}"))
                                    {
                                        tx2.Start();
                                        foreach (var id in ids)
                                        {
                                            try { ElementTransformUtils.CopyElements(srcView, new List<ElementId> { id }, dstView, Transform.Identity, opts); } catch { }
                                        }
                                        try { tx2.Commit(); } catch { try { tx2.RollBack(); } catch { } }
                                    }
                                }
                            }
                        }
                    }

                    // 3) Per-element work-plane-based pass — view-to-view with explicit SketchPlane
                    if (workPlaneBasedIds.Count > 0)
                    {
                        foreach (var id in workPlaneBasedIds)
                        {
                            Element? e = null; Plane? plane = null; View? srcView = null; View? dstView = null; SketchPlane? sp = null;
                            try { e = sourceDoc.GetElement(id); } catch { e = null; }
                            if (e == null) continue;

                            try { plane = TryGetElementPlane(sourceDoc, e); } catch { plane = null; }
                            if (plane == null)
                            {
                                try { dialogs.Info("Export RVT", $"WorkPlane copy: missing plane for element {id} ({e.Category?.Name ?? "?"})."); } catch { }
                                continue;
                            }

                            // level hint from element level param if present
                            string levelNameHint = string.Empty;
                            try
                            {
                                var lvlId = e.get_Parameter(BuiltInParameter.LEVEL_PARAM)?.AsElementId() ?? ElementId.InvalidElementId;
                                if (lvlId != ElementId.InvalidElementId)
                                {
                                    var lvl = sourceDoc.GetElement(lvlId) as Level; if (lvl != null) levelNameHint = lvl.Name ?? string.Empty;
                                }
                            }
                            catch { levelNameHint = string.Empty; }

                            try { dstView = EnsureDestViewForPlane(newDoc, plane!, levelNameHint); } catch { dstView = null; }
                            if (dstView == null) { try { dialogs.Info("Export RVT", $"WorkPlane copy: failed to get/create destination view for element {id}."); } catch { } continue; }

                            // choose a source view: owner for view-specific else any non-template view
                            try
                            {
                                bool eViewSpecific = false; try { eViewSpecific = e.ViewSpecific; } catch { eViewSpecific = false; }
                                if (eViewSpecific)
                                {
                                    srcView = sourceDoc.GetElement(e.OwnerViewId) as View;
                                }
                                if (srcView == null)
                                {
                                    // Prefer a plan view if plane is horizontal; else any non-template view
                                    var n = plane!.XVec.CrossProduct(plane!.YVec);
                                    bool isHorizontal = Math.Abs(n.Z) > 0.9;
                                    if (isHorizontal)
                                    {
                                        // Try matching level name
                                        if (!string.IsNullOrWhiteSpace(levelNameHint))
                                        {
                                            srcView = new FilteredElementCollector(sourceDoc)
                                                .OfClass(typeof(ViewPlan))
                                                .Cast<ViewPlan>()
                                                .FirstOrDefault(v => !v.IsTemplate && string.Equals((v.GenLevel?.Name) ?? string.Empty, levelNameHint, StringComparison.OrdinalIgnoreCase));
                                        }
                                        if (srcView == null)
                                        {
                                            srcView = new FilteredElementCollector(sourceDoc)
                                                .OfClass(typeof(ViewPlan))
                                                .Cast<ViewPlan>()
                                                .FirstOrDefault(v => !v.IsTemplate);
                                        }
                                    }
                                    if (srcView == null)
                                    {
                                        srcView = new FilteredElementCollector(sourceDoc)
                                            .OfClass(typeof(View))
                                            .Cast<View>()
                                            .FirstOrDefault(v => v != null && !v.IsTemplate);
                                    }
                                }
                            }
                            catch { srcView = null; }

                            if (srcView == null)
                            {
                                try { dialogs.Info("Export RVT", $"WorkPlane copy: no source view available for element {id}."); } catch { }
                                continue;
                            }

                            using (var tx = new Transaction(newDoc, $"Copy WP-based -> {dstView.Name}"))
                            {
                                tx.Start();
                                try
                                {
                                    try { sp = EnsureSketchPlane(newDoc, plane!); } catch { sp = null; }
                                    if (sp != null)
                                    {
                                        try { dstView.SketchPlane = sp; } catch { }
                                    }
                                    ElementTransformUtils.CopyElements(srcView, new List<ElementId> { id }, dstView, Transform.Identity, opts);
                                    tx.Commit();
                                }
                                catch (Exception ex)
                                {
                                    try { tx.RollBack(); } catch { }
                                    try { dialogs.Info("Export RVT", $"WorkPlane copy failed for element {id} ({e.Category?.Name ?? "?"}):\n{ex.Message}"); } catch { }
                                }
                            }
                        }
                    }

                    // Save-as {SetName}.rvt in global output dir
                    var fileSafe = San(setName);
                    if (string.IsNullOrWhiteSpace(fileSafe)) fileSafe = "export";

                    var outDir = AppSettingsStore.GetEffectiveOutputDir(settings);
                    try { System.IO.Directory.CreateDirectory(outDir); } catch { }
                    var fullPath = System.IO.Path.Combine(outDir, fileSafe + ".rvt");

                    // Overwrite policy
                    bool overwrite = settings.DefaultOverwrite;
                    var sao = new SaveAsOptions { OverwriteExistingFile = overwrite };

                    bool saved = false;
                    try { newDoc.SaveAs(fullPath, sao); saved = true; }
                    catch (Exception ex) { dialogs.Info("Export RVT", $"Save failed for '{fullPath}':\n{ex.Message}"); }

                    try { newDoc.Close(false); } catch { }

                    // Always delete backups *.000*.rvt (policy retained)
                    try
                    {
                        var nameNoExt = System.IO.Path.GetFileNameWithoutExtension(fullPath);
                        var dir = System.IO.Path.GetDirectoryName(fullPath) ?? string.Empty;
                        if (System.IO.Directory.Exists(dir))
                        {
                            foreach (var f in System.IO.Directory.GetFiles(dir, nameNoExt + ".000*.rvt", System.IO.SearchOption.TopDirectoryOnly))
                            {
                                try { System.IO.File.Delete(f); } catch { }
                            }
                        }
                    }
                    catch { }

                    // Optional: journal success note
                    try { if (saved) newDoc?.Application?.WriteJournalComment($"ExportRvtAction: saved '{fullPath}'", false); } catch { }
                }
                finally
                {
                    try { if (newDoc != null && newDoc.IsModifiable) { /* ensure closed above */ } } catch { }
                }
            }
        }

        private static void EnsureWorkPlanesForElements(Document source, Document dest, IList<ElementId> elementIds)
        {
            if (elementIds == null || elementIds.Count == 0) return;

            var required = new Dictionary<string, Plane>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in elementIds)
            {
                try
                {
                    var e = source.GetElement(id);
                    if (e == null) continue;
                    var p = e.get_Parameter(BuiltInParameter.SKETCH_PLANE_PARAM);
                    if (p == null || p.StorageType != StorageType.ElementId) continue;
                    var spId = p.AsElementId();
                    if (spId == ElementId.InvalidElementId) continue;
                    var sp = source.GetElement(spId) as SketchPlane;
                    if (sp == null) continue;
                    var plane = TryGetPlane(sp);
                    string name = string.Empty; try { name = sp.Name ?? string.Empty; } catch { name = string.Empty; }
                    if (string.IsNullOrWhiteSpace(name) || plane == null) continue;
                    if (!required.ContainsKey(name)) required.Add(name, plane);
                }
                catch { }
            }
            if (required.Count == 0) return;

            // Build existing name sets in destination
            var existingSketch = new HashSet<string>(new FilteredElementCollector(dest)
                .OfClass(typeof(SketchPlane))
                .Cast<SketchPlane>()
                .Select(sp => sp.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);

            var existingRefPlane = new HashSet<string>(new FilteredElementCollector(dest)
                .OfClass(typeof(ReferencePlane))
                .Cast<ReferencePlane>()
                .Select(rp => rp.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);

            using (var tx = new Transaction(dest, "Ensure Work Planes"))
            {
                tx.Start();
                try
                {
                    foreach (var kv in required)
                    {
                        var name = kv.Key;
                        var plane = kv.Value;
                        // Create a SketchPlane if missing (names matter for Revit mapping)
                        if (!existingSketch.Contains(name))
                        {
                            try { var _ = Autodesk.Revit.DB.SketchPlane.Create(dest, plane); } catch { }
                            existingSketch.Add(name);
                        }
                        // Also create a ReferencePlane with same name if missing (some elements map by ref plane name)
                        if (!existingRefPlane.Contains(name))
                        {
                            try
                            {
                                var origin = plane.Origin;
                                var x = plane.XVec; var y = plane.YVec;
                                var bubbleEnd = origin + x; var freeEnd = origin - x; var cutVec = y;
                                var anyView = new FilteredElementCollector(dest).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => v != null && !v.IsTemplate);
                                if (anyView != null)
                                {
                                    var rp = dest.Create.NewReferencePlane(bubbleEnd, freeEnd, cutVec, anyView);
                                    try { rp.Name = name; } catch { }
                                }
                            }
                            catch { }
                            existingRefPlane.Add(name);
                        }
                    }
                    tx.Commit();
                }
                catch { try { tx.RollBack(); } catch { } }
            }
        }

        private static Plane? TryGetPlane(SketchPlane sp)
        {
            try { return sp.GetPlane(); } catch { return null; }
        }

        private static string? ViewKey(View v)
        {
            try { return v?.ViewType.ToString() + "|" + (v?.Name ?? string.Empty); } catch { return null; }
        }

        private static Dictionary<string, View> BuildDestViewMap(Document dest)
        {
            var dict = new Dictionary<string, View>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var views = new FilteredElementCollector(dest)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v != null && !v.IsTemplate);
                foreach (var v in views)
                {
                    var key = ViewKey(v);
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (!dict.ContainsKey(key!)) dict[key!] = v; // first wins
                }
            }
            catch { }
            return dict;
        }

        private static string San(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Replace('/', '_').Replace('\\', '_');
            return name.Trim();
        }

        // ---- New helpers for Work Plane handling ----
        private static bool IsWorkPlaneBased(Element e)
        {
            try
            {
                if (e is FamilyInstance fi)
                {
                    try { return fi.Symbol?.Family?.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased; } catch { }
                }
            }
            catch { }
            return false;
        }

        private static Plane? TryGetElementPlane(Document doc, Element e)
        {
            try
            {
                var p = e.get_Parameter(BuiltInParameter.SKETCH_PLANE_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var spId = p.AsElementId();
                    if (spId != ElementId.InvalidElementId)
                    {
                        var sp = doc.GetElement(spId) as SketchPlane;
                        if (sp != null)
                        {
                            var pl = TryGetPlane(sp);
                            if (pl != null) return pl;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static View EnsureDestViewForPlane(Document dest, Plane p, string levelNameHint)
        {
            // Determine horizontal vs vertical using plane normal
            var n = p.XVec.CrossProduct(p.YVec);
            bool isHorizontal = Math.Abs(n.Z) > 0.9; // tolerant

            if (isHorizontal)
            {
                // Pick level by name or closest elevation to plane origin Z
                Level? level = null;
                try
                {
                    var levels = new FilteredElementCollector(dest).OfClass(typeof(Level)).Cast<Level>().ToList();
                    if (!string.IsNullOrWhiteSpace(levelNameHint))
                    {
                        level = levels.FirstOrDefault(l => string.Equals(l.Name ?? string.Empty, levelNameHint, StringComparison.OrdinalIgnoreCase));
                    }
                    if (level == null)
                    {
                        level = levels.OrderBy(l => Math.Abs((l.Elevation) - p.Origin.Z)).FirstOrDefault();
                    }
                }
                catch { level = null; }

                // Find existing floor plan for that level
                if (level != null)
                {
                    try
                    {
                        var existingPlan = new FilteredElementCollector(dest)
                            .OfClass(typeof(ViewPlan))
                            .Cast<ViewPlan>()
                            .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && (v.GenLevel?.Id == level.Id));
                        if (existingPlan != null) return existingPlan;
                    }
                    catch { }

                    // Create a new floor plan
                    try
                    {
                        using (var tx = new Transaction(dest, "Create Floor Plan for WP"))
                        {
                            tx.Start();
                            var vft = new FilteredElementCollector(dest)
                                .OfClass(typeof(ViewFamilyType))
                                .Cast<ViewFamilyType>()
                                .FirstOrDefault(t => t.ViewFamily == ViewFamily.FloorPlan);
                            if (vft != null)
                            {
                                var vp = ViewPlan.Create(dest, vft.Id, level.Id);
                                tx.Commit();
                                return vp;
                            }
                            tx.RollBack();
                        }
                    }
                    catch { }
                }

                // Fallback: any non-template view
                try { return new FilteredElementCollector(dest).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(v => !v.IsTemplate); } catch { }
                try { return new FilteredElementCollector(dest).OfClass(typeof(View)).Cast<View>().First(v => !v.IsTemplate); } catch { }
            }
            else
            {
                // Create a section aligned to the plane using a bounding box with transform aligned to plane axes
                try
                {
                    using (var tx = new Transaction(dest, "Create Section for WP"))
                    {
                        tx.Start();
                        var vft = new FilteredElementCollector(dest)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(t => t.ViewFamily == ViewFamily.Section);
                        if (vft != null)
                        {
                            var box = new BoundingBoxXYZ();
                            var tr = Transform.Identity;
                            tr.BasisX = p.XVec;
                            tr.BasisY = p.YVec;
                            tr.BasisZ = p.XVec.CrossProduct(p.YVec);
                            tr.Origin = p.Origin;
                            box.Transform = tr;
                            double w = 100.0, h = 100.0, d = 10.0;
                            box.Min = new XYZ(-w / 2, -h / 2, -d / 2);
                            box.Max = new XYZ(w / 2, h / 2, d / 2);
                            var vs = ViewSection.CreateSection(dest, vft.Id, box);
                            tx.Commit();
                            return vs;
                        }
                        tx.RollBack();
                    }
                }
                catch { }

                // Fallback: any non-template view
                try { return new FilteredElementCollector(dest).OfClass(typeof(ViewSection)).Cast<ViewSection>().First(v => !v.IsTemplate); } catch { }
                try { return new FilteredElementCollector(dest).OfClass(typeof(View)).Cast<View>().First(v => !v.IsTemplate); } catch { }
            }

            throw new InvalidOperationException("Unable to ensure destination view for plane.");
        }

        private static SketchPlane EnsureSketchPlane(Document dest, Plane p)
        {
            // Minimal: create a new SketchPlane on demand
            try { return SketchPlane.Create(dest, p); } catch { }
            // Last resort: attempt with a slightly nudged plane
            return SketchPlane.Create(dest, Plane.CreateByOriginAndBasis(p.Origin, p.XVec, p.YVec));
        }
    }
}
