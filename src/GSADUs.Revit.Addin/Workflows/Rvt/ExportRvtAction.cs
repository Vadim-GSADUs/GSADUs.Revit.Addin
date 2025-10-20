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

            // Partition: model (doc-level) vs view-specific (grouped by OwnerViewId)
            var modelIds = new List<ElementId>();
            var viewGroups = new Dictionary<ElementId, List<ElementId>>();
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

                    if (!isViewSpecific)
                    {
                        modelIds.Add(id);
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

                    // Preflight: ensure required work planes (ReferencePlanes and SketchPlanes) exist in destination
                    EnsureWorkPlanesForElements(sourceDoc, newDoc, modelIds);

                    // 1) Copy model (doc-level) — best effort bulk then per-element
                    if (modelIds.Count > 0)
                    {
                        bool bulkSucceeded = false;
                        using (var tx = new Transaction(newDoc, "Copy Model Elements"))
                        {
                            tx.Start();
                            try
                            {
                                ElementTransformUtils.CopyElements(sourceDoc, modelIds, newDoc, Transform.Identity, opts);
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
                                foreach (var id in modelIds)
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
    }
}
