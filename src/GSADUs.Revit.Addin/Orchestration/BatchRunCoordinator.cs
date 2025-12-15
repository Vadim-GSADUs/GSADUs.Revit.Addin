using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Abstractions;
using GSADUs.Revit.Addin.Infrastructure;
using GSADUs.Revit.Addin.UI;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace GSADUs.Revit.Addin.Orchestration
{
    internal static class BatchRunCoordinator
    {
        internal enum RunOutcome { CanceledBeforeStart, CancelledDuringRun, Completed, Failed }

        internal static Result RunCore(UIApplication uiapp, UIDocument uidoc)
        {
            var dialogs = ServiceBootstrap.Provider.GetService(typeof(IDialogService)) as IDialogService ?? new DialogService();

            if (uidoc == null) { dialogs.Info("Batch Export", "Open a document."); return Result.Cancelled; }
            var doc = uidoc.Document;
            if (string.IsNullOrWhiteSpace(doc.PathName)) { dialogs.Info("Batch Export", "Save the model first."); return Result.Cancelled; }

            // Previously: blocked when no selection filters exist. Now allow window to open; window validation will decide.
            var allSetsInitial = SelectionSets.Get(doc);
            Trace.WriteLine($"COLLECT_SHEETS initial={allSetsInitial.Count} corr={Logging.RunLog.CorrId}");

            // Modeless flow: open or activate BatchExportWindow and let it drive runs via callback.
            var win = BatchExportWindowHost.ShowOrActivate(uidoc, owner: null);
            if (win != null)
            {
                win.RunRequested -= uiOpts => { };
                win.RunRequested += uiOpts => OnRunRequested(uiapp, uidoc, uiOpts);
            }
            return Result.Succeeded;
        }

        public static Result Run(ExternalCommandData commandData)
        {
            var uiapp = commandData.Application;
            var uidoc = uiapp?.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;

            var win = BatchExportWindowHost.ShowOrActivate(uidoc, owner: null);
            if (win != null)
            {
                win.RunRequested -= uiOpts => { };
                win.RunRequested += uiOpts => OnRunRequested(uiapp, uidoc, uiOpts);
            }
            return Result.Succeeded;
        }

        private static bool IsCsvEntireProjectSelected(AppSettings appSettings)
        {
            try
            {
                var sel = new HashSet<string>(appSettings.SelectedWorkflowIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                var wfs = (appSettings.Workflows ?? new List<WorkflowDefinition>()).Where(w => sel.Contains(w.Id)).ToList();
                if (wfs.Count == 0) return false;
                return wfs.All(w => w.Output == OutputType.Csv && string.Equals(w.Scope, "EntireProject", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        internal static RunOutcome RunOnce(UIApplication uiapp, UIDocument uidoc)
        {
            Trace.WriteLine("BEGIN RunOnce corr=" + Logging.RunLog.CorrId);

            var dialogs = ServiceBootstrap.Provider.GetService(typeof(IDialogService)) as IDialogService ?? new DialogService();
            var logFactory = ServiceBootstrap.Provider.GetService(typeof(IBatchLogFactory)) as IBatchLogFactory ?? new CsvBatchLogger();
            var allActions = ServiceBootstrap.Provider.GetServices<IExportAction>().ToList();
            var actionRegistry = ServiceBootstrap.Provider.GetService(typeof(IActionRegistry)) as IActionRegistry ?? new ActionRegistry();
            var projectSettingsProvider = ServiceBootstrap.Provider.GetService(typeof(IProjectSettingsProvider)) as IProjectSettingsProvider
                                          ?? new EsProjectSettingsProvider();

            var doc = uidoc.Document;

            var allSetsForWindow = SelectionSets.Get(doc);
            Trace.WriteLine($"COLLECT_SHEETS requested={allSetsForWindow.Count} corr={Logging.RunLog.CorrId}");

            // Allow dialog even when zero filters exist; window may allow CSV EntireProject runs.
            var win = BatchExportWindowHost.ShowOrActivate(uidoc, owner: null);
            if (win != null)
            {
                win.RunRequested -= uiOpts => { };
                win.RunRequested += uiOpts => OnRunRequested(uiapp, uidoc, uiOpts);
            }

            return RunOutcome.Completed;
        }

        private static void OnRunRequested(UIApplication uiapp, UIDocument uidoc, BatchRunOptions uiOpts)
        {
            try
            {
                Trace.WriteLine("RunRequested received");
                Trace.WriteLine("Starting batch execution");
            }
            catch { }

            try
            {
                var projectSettingsProvider = ServiceBootstrap.Provider.GetService(typeof(IProjectSettingsProvider)) as IProjectSettingsProvider
                                              ?? new EsProjectSettingsProvider();
                var logFactory = ServiceBootstrap.Provider.GetService(typeof(IBatchLogFactory)) as IBatchLogFactory ?? new CsvBatchLogger();
                var actionRegistry = ServiceBootstrap.Provider.GetService(typeof(IActionRegistry)) as IActionRegistry ?? new ActionRegistry();
                var allActions = ServiceBootstrap.Provider.GetServices<IExportAction>().ToList();

                _ = ExecuteRun(uiapp, uidoc, projectSettingsProvider, logFactory, actionRegistry, allActions, uiOpts);
            }
            catch (Exception ex)
            {
                try { Trace.WriteLine(ex); } catch { }
            }
        }

        private static RunOutcome ExecuteRun(UIApplication uiapp, UIDocument uidoc, IProjectSettingsProvider projectSettingsProvider, IBatchLogFactory logFactory, IActionRegistry actionRegistry, List<IExportAction> allActions, BatchRunOptions uiOpts)
        {
            var dialogs = ServiceBootstrap.Provider.GetService(typeof(IDialogService)) as IDialogService ?? new DialogService();
            var doc = uidoc.Document;

            var appSettings = projectSettingsProvider.Load();
            var isDryRun = appSettings.DryrunDiagnostics;

            // Build action ids list from selected workflows into settings DTO
            var selectedWorkflowIds = new HashSet<string>(appSettings.SelectedWorkflowIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var workflows = (appSettings.Workflows ?? new List<WorkflowDefinition>()).Where(w => selectedWorkflowIds.Contains(w.Id)).ToList();
            var chosenActionIds = workflows.SelectMany(w => w.ActionIds ?? new List<string>())
                                           .Distinct(StringComparer.OrdinalIgnoreCase)
                                           .ToList();

            Trace.WriteLine($"WORKFLOWS selected={chosenActionIds.Count} corr=" + Logging.RunLog.CorrId);

            // Safety: auto-add required action ids for selected output types
            if (workflows.Any(w => w.Output == OutputType.Pdf) && !chosenActionIds.Any(a => string.Equals(a, "export-pdf", StringComparison.OrdinalIgnoreCase)))
                chosenActionIds.Add("export-pdf");
            if (workflows.Any(w => w.Output == OutputType.Image) && !chosenActionIds.Any(a => string.Equals(a, "export-image", StringComparison.OrdinalIgnoreCase)))
                chosenActionIds.Add("export-image");

            // BatchExportSettings includes SetIds (optional)
            var request = new BatchExportSettings(
                uiOpts.SetNames,
                uiOpts.OutputDir,
                uiOpts.SaveBefore,
                false,
                appSettings.DefaultRecenterXY,
                uiOpts.Overwrite,
                uiOpts.SetIds)
            { ActionIds = chosenActionIds };

            var orderedDescs = actionRegistry.All().Where(d => chosenActionIds.Contains(d.Id))
                                                  .OrderBy(d => d.Order)
                                                  .ToList();
            var resolved = (from desc in orderedDescs
                            let impl = allActions.FirstOrDefault(a => string.Equals(a.Id, desc.Id, StringComparison.OrdinalIgnoreCase))
                            where impl != null && impl.IsEnabled(appSettings, request)
                            select new { desc, impl }).ToList();

            var internalActions = resolved.Where(a => !a.desc.RequiresExternalClone).ToList();
            var externalActions = resolved.Where(a => a.desc.RequiresExternalClone).ToList();
            bool requiresClone = externalActions.Any();

            if (resolved.Count == 0)
            {
                try { PerfLogger.Write("BatchExport.NoActions", string.Join(";", chosenActionIds), TimeSpan.Zero); } catch { }
                dialogs.Info("Batch Export", "No enabled actions found for selected workflows (missing implementations). Aborting.");
                return RunOutcome.CanceledBeforeStart;
            }

            // Selection resolution by SetId first
            var allFilterElems = new FilteredElementCollector(doc)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .ToList();

            var nameToId = allFilterElems
                .Where(sf => !string.IsNullOrWhiteSpace(sf.Name) && !string.IsNullOrWhiteSpace(sf.UniqueId))
                .GroupBy(sf => sf.Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().UniqueId, StringComparer.OrdinalIgnoreCase);

            var filterById = allFilterElems.Where(f => !string.IsNullOrWhiteSpace(f.UniqueId))
                                           .ToDictionary(f => f.UniqueId, f => f, StringComparer.OrdinalIgnoreCase);
            var filterByName = allFilterElems.GroupBy(f => f.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                                             .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var selectedFilterElems = new List<SelectionFilterElement>();
            var effectiveSetIds = new List<string>();
            var effectiveSetNames = new List<string>();

            if (request.SetIds is { Count: > 0 })
            {
                foreach (var sid in request.SetIds.Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    if (filterById.TryGetValue(sid, out var sfe))
                    {
                        selectedFilterElems.Add(sfe);
                        effectiveSetIds.Add(sfe.UniqueId);
                        effectiveSetNames.Add(sfe.Name ?? string.Empty);
                    }
                }
            }
            else
            {
                foreach (var name in request.SetNames)
                {
                    if (filterByName.TryGetValue(name, out var sfe))
                    {
                        selectedFilterElems.Add(sfe);
                        effectiveSetIds.Add(sfe.UniqueId);
                        effectiveSetNames.Add(sfe.Name ?? string.Empty);
                    }
                }
            }

            if (isDryRun && selectedFilterElems.Count > 1)
            {
                selectedFilterElems = selectedFilterElems.Take(1).ToList();
                effectiveSetIds = effectiveSetIds.Take(1).ToList();
                effectiveSetNames = effectiveSetNames.Take(1).ToList();
            }

            bool isCsvEntireProject = IsCsvEntireProjectSelected(appSettings);

            if (selectedFilterElems.Count == 0 && !isCsvEntireProject)
            {
                dialogs.Info("Batch Export", "No matching Selection Filters for chosen sets.");
                return RunOutcome.CanceledBeforeStart;
            }

            // Detect missing (ids or names)
            var missing = new List<string>();
            if (request.SetIds is { Count: > 0 })
            {
                var foundIds = new HashSet<string>(effectiveSetIds, StringComparer.OrdinalIgnoreCase);
                foreach (var sid in request.SetIds)
                    if (!foundIds.Contains(sid)) missing.Add(sid);
            }
            else
            {
                var foundNames = new HashSet<string>(effectiveSetNames, StringComparer.OrdinalIgnoreCase);
                foreach (var nm in request.SetNames)
                    if (!foundNames.Contains(nm)) missing.Add(nm);
            }
            if (missing.Count > 0)
            {
                try
                {
                    dialogs.Info("Batch Export", "Some selected sets are missing and will be skipped.\n" + string.Join("\n", missing.Take(20)) + (missing.Count > 20 ? $"\n(+{missing.Count - 20} more)" : string.Empty));
                }
                catch { }
            }

            // One-time staging area validation
            using (PerfLogger.Measure("Batch.OneTimePrep", string.Empty))
            {
                if (appSettings.ValidateStagingArea && !isCsvEntireProject)
                {
                    Trace.WriteLine("Validating staging area...");
                    if (!EnsureStagingAreaClear(doc, appSettings, dialogs, projectSettingsProvider))
                    {
                        Trace.WriteLine("Staging area validation failed.");
                        return RunOutcome.CanceledBeforeStart;
                    }
                    Trace.WriteLine("Staging area validated.");
                }
            }

            var logDir = projectSettingsProvider.GetEffectiveLogDir(appSettings);
            var modelName = System.IO.Path.GetFileNameWithoutExtension(doc.PathName) ?? "Model";
            var csvName = San($"{modelName} Batch Export Log.csv");
            var logPath = System.IO.Path.Combine(logDir, csvName);
            var log = logFactory.Load(logPath);

            if (request.SaveBefore && doc.IsModified) doc.Save();

            Directory.CreateDirectory(logDir);
            var modelNameSan = System.IO.Path.GetFileNameWithoutExtension(doc.PathName) ?? "Model";
            var csvNameSan = San($"{modelNameSan} Batch Export Log.csv");
            var logPathSan = System.IO.Path.Combine(logDir, csvNameSan);
            var logSan = GSADUs.Revit.Addin.Logging.GuardedBatchLog.Wrap(logFactory.Load(logPathSan));

            // Pre-compute member element unique ids per setId
            var memberUidsBySetId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var elementIdsBySetId = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);
            foreach (var sf in selectedFilterElems)
            {
                var eidList = sf.GetElementIds()?.ToList() ?? new List<ElementId>();
                elementIdsBySetId[sf.UniqueId] = eidList;
                var uids = new List<string>(eidList.Count);
                foreach (var eid in eidList)
                {
                    try { var e = doc.GetElement(eid); if (e != null && !string.IsNullOrWhiteSpace(e.UniqueId)) uids.Add(e.UniqueId); } catch { }
                }
                memberUidsBySetId[sf.UniqueId] = uids;
            }

            // PROJECT-SCOPED CSV BRANCH
            if (isCsvEntireProject)
            {
                var cts = new CancellationTokenSource();
                bool cancelRequested = false;
                var progressWin = new UI.ProgressWindow();
                progressWin.CancelRequested += (_, __) => { cancelRequested = true; if (!cts.IsCancellationRequested) cts.Cancel(); };
                try { progressWin.Show(); } catch { }
                var swOnce = Stopwatch.StartNew();
                try { progressWin.Update("EntireProject", 1, 1, 0.0, swOnce.Elapsed); } catch { }
                UI.ProgressWindow.DoEvents();

                try
                {
                    using (PerfLogger.Measure("Batch.Workflows.ProjectCsv", "EntireProject"))
                    {
                        foreach (var a in internalActions)
                        {
                            using (PerfLogger.Measure($"Action.{a.desc.Id}", "EntireProject"))
                            {
                                Trace.WriteLine($"Executing action (project): {a.desc.Id}");
                                a.impl.Execute(uiapp, doc, null, string.Empty, new List<string>(), isDryRun);
                                Trace.WriteLine($"Action {a.desc.Id} completed (project).");
                            }
                            UI.ProgressWindow.DoEvents();
                            if (cts.IsCancellationRequested) break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    dialogs.Info("Batch Export", $"Project run failed: {ex.Message}");
                }
                finally
                {
                    try { progressWin.ForceClose(); } catch { }
                    swOnce.Stop();
                }

                if (cancelRequested)
                {
                    dialogs.Info("Batch Export", "Cancelled after current step.");
                    return RunOutcome.CancelledDuringRun;
                }

                dialogs.Info("Batch Export", isDryRun ? "Dry run completed." : "Done.");
                Trace.WriteLine("END RunOnce corr=" + Logging.RunLog.CorrId);
                return RunOutcome.Completed;
            }

            var progressWin2 = new UI.ProgressWindow();
            var cts2 = new CancellationTokenSource();
            bool cancelRequested2 = false;
            progressWin2.CancelRequested += (_, __) => { cancelRequested2 = true; if (!cts2.IsCancellationRequested) cts2.Cancel(); };
            try { progressWin2.Show(); } catch { }
            var sw = Stopwatch.StartNew();

            bool breakAfterThisSet = false;

            int totalCount = selectedFilterElems.Count;
            int iSet = 0;
            foreach (var sf in selectedFilterElems)
            {
                var setId = sf.UniqueId;
                var setName = sf.Name ?? string.Empty;
                iSet++;
                try { progressWin2.Update(setName, iSet, totalCount, totalCount > 0 ? (double)(iSet - 1) * 100.0 / totalCount : 0.0, sw.Elapsed); } catch { }
                UI.ProgressWindow.DoEvents();

                if (!elementIdsBySetId.ContainsKey(setId))
                {
                    continue; // should not happen
                }

                // --- PDF success baseline capture (directory snapshot before actions) ---
                var pdfOutDir = projectSettingsProvider.GetEffectiveOutputDir(appSettings);
                HashSet<string> pdfBefore = new HashSet<string>(
                    Directory.Exists(pdfOutDir) ? Directory.GetFiles(pdfOutDir, "*.pdf", SearchOption.TopDirectoryOnly) : Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);

                var entry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Key"] = setName
                };

                bool toggled = false;
                XYZ stageDelta = XYZ.Zero;
                var sfilter = doc.GetElement(setId) as SelectionFilterElement; // legacy direct resolution
                IList<ElementId> stageIds = sfilter?.GetElementIds()?.ToList() ?? new List<ElementId>();
                try { toggled = TryToggleCurrentSet(doc, appSettings, memberUidsBySetId.GetValueOrDefault(setId) ?? new List<string>(), true); } catch { }

                using (PerfLogger.Measure("Batch.PreBatch", setName))
                {
                    // Legacy staging move attempt (swallow failures)
                    try
                    {
                        if (stageIds.Count > 0)
                        {
                            if (!TryStageMove(doc, stageIds, appSettings, out stageDelta))
                                stageDelta = XYZ.Zero;
                        }
                    }
                    catch { stageDelta = XYZ.Zero; }
                }

                try
                {
                    // Ensure setName is valid
                    if (string.IsNullOrWhiteSpace(setName))
                    {
                        TaskDialog.Show("Batch Export", "Select or define a valid SetName.");
                        return RunOutcome.CanceledBeforeStart;
                    }

                    var preserveUids = memberUidsBySetId.GetValueOrDefault(setId) ?? new List<string>();

                    using (PerfLogger.Measure("Batch.Workflows", setName))
                    {
                        foreach (var a in internalActions)
                        {
                            using (PerfLogger.Measure($"Action.{a.desc.Id}", setName))
                            {
                                Trace.WriteLine($"Executing action: {a.desc.Id}");
                                a.impl.Execute(uiapp, doc, null, setName, preserveUids!, isDryRun);
                                Trace.WriteLine($"Action {a.desc.Id} completed.");
                            }
                            UI.ProgressWindow.DoEvents();
                            if (cts2.IsCancellationRequested) { breakAfterThisSet = true; break; }
                        }
                        if (breakAfterThisSet) throw new OperationCanceledException();
                    }
                }

                catch (Exception ex)
                {
                    dialogs.Info("Batch Export", $"Set '{setName}' failed:\n{ex.Message}");
                }
                finally
                {
                    using (PerfLogger.Measure("Batch.PostBatch", setName))
                    {
                        try
                        {
                            if (stageIds.Count > 0 && stageDelta != null && !stageDelta.IsAlmostEqualTo(XYZ.Zero))
                                TryRestoreStage(doc, stageIds, stageDelta);
                        }
                        catch { }
                        try { if (toggled) TryToggleCurrentSet(doc, appSettings, memberUidsBySetId.GetValueOrDefault(setId) ?? new List<string>(), false); } catch { }
                        logSan.Update(setName, entry);
                        logSan.Save(logPathSan);
                    }
                }

                try { progressWin2.Update(setName, iSet, totalCount, totalCount > 0 ? (double)iSet * 100.0 / totalCount : 100.0, sw.Elapsed); } catch { }
                UI.ProgressWindow.DoEvents();

                if (breakAfterThisSet)
                {
                    cancelRequested2 = true;
                    break;
                }
            }

            try { progressWin2.ForceClose(); } catch { }
            sw.Stop();

            if (cancelRequested2)
            {
                dialogs.Info("Batch Export", "Cancelled after current step.");
                return RunOutcome.CancelledDuringRun;
            }

            dialogs.Info("Batch Export", isDryRun ? "Dry run completed." : "Done.");

            // New: optionally open output folder after completion
            try
            {
                if (!isDryRun && appSettings.OpenOutputFolder)
                {
                    var outDir = projectSettingsProvider.GetEffectiveOutputDir(appSettings);
                    if (System.IO.Directory.Exists(outDir))
                    {
                        // Check if an explorer window for this path is already open (best-effort via process arguments)
                        bool alreadyOpen = false;
                        try
                        {
                            var explorerProcs = System.Diagnostics.Process.GetProcessesByName("explorer");
                            foreach (var p in explorerProcs)
                            {
                                try
                                {
                                    // CommandLine access requires some permissions; ignore failures
                                    var cl = p.MainWindowTitle ?? string.Empty;
                                    if (!string.IsNullOrWhiteSpace(cl) && cl.IndexOf(System.IO.Path.GetFileName(outDir), StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        alreadyOpen = true;
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                        if (!alreadyOpen)
                        {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = outDir, UseShellExecute = true }); } catch { }
                        }
                    }
                }
            }
            catch { }

            Trace.WriteLine("END RunOnce corr=" + Logging.RunLog.CorrId);
            return RunOutcome.Completed;
        }


        private static bool TryToggleCurrentSet(Document doc, AppSettings settings, IEnumerable<string> uids, bool value)
        {
            if (doc == null || uids == null) return false;
            string? guidText = settings.CurrentSetParameterGuid;
            Guid guid = Guid.Empty;
            bool hasGuid = !string.IsNullOrWhiteSpace(guidText) && Guid.TryParse(guidText, out guid);
            string name = settings.CurrentSetParameterName ?? "CurrentSet";

            var els = new List<Element>();
            foreach (var uid in uids)
            {
                try { var e = doc.GetElement(uid); if (e != null) els.Add(e); } catch { }
            }
            if (els.Count == 0) return false;

            using var tx = new Transaction(doc, value ? "Mark CurrentSet" : "Unmark CurrentSet");
            tx.Start();
            try
            {
                foreach (var e in els)
                {
                    Parameter? p = null;
                    if (hasGuid) { try { p = e.get_Parameter(guid); } catch { p = null; } }
                    if (p == null)
                    {
                        try { p = e.LookupParameter(name); } catch { p = null; }
                    }
                    if (p == null || p.StorageType != StorageType.Integer) continue;
                    try { p.Set(value ? 1 : 0); } catch { }
                }
                tx.Commit();
                return true;
            }
            catch { try { tx.RollBack(); } catch { } return false; }
        }

        private static bool TryStageMove(Document doc, IList<ElementId> ids, AppSettings settings, out XYZ delta)
        {
            delta = XYZ.Zero;
            if (doc == null || ids == null || ids.Count == 0) return false;
            try
            {
                // Compute combined bounds in model coords
                double minx = double.PositiveInfinity, miny = double.PositiveInfinity;
                double maxx = double.NegativeInfinity, maxy = double.NegativeInfinity;
                bool any = false;
                foreach (var id in ids)
                {
                    Element? e = null; try { e = doc.GetElement(id); } catch { }
                    if (e == null) continue;
                    BoundingBoxXYZ? bb = null; try { bb = e.get_BoundingBox(null); } catch { bb = null; }
                    if (bb == null) continue;
                    minx = Math.Min(minx, bb.Min.X); miny = Math.Min(miny, bb.Min.Y);
                    maxx = Math.Max(maxx, bb.Max.X); maxy = Math.Max(maxy, bb.Max.Y);
                    any = true;
                }
                if (!any) return false;

                // Choose pivot
                XYZ pivot;
                if (settings.StageMoveMode?.Equals("MinToOrigin", StringComparison.OrdinalIgnoreCase) == true)
                    pivot = new XYZ(minx, miny, 0);
                else
                    pivot = new XYZ((minx + maxx) * 0.5, (miny + maxy) * 0.5, 0);

                delta = new XYZ(-pivot.X, -pivot.Y, 0);

                using var tx = new Transaction(doc, "Stage move");
                tx.Start();
                try
                {
                    ElementTransformUtils.MoveElements(doc, ids, delta);
                    tx.Commit();
                    Trace.WriteLine("TryStageMove successful: " + string.Join(", ", ids));
                    return true;
                }
                catch
                {
                    try { tx.RollBack(); } catch { }
                    delta = XYZ.Zero;
                    return false;
                }
            }
            catch
            {
                delta = XYZ.Zero;
                return false;
            }
        }

        private static bool TryRestoreStage(Document doc, IList<ElementId> ids, XYZ delta)
        {
            if (doc == null || ids == null || ids.Count == 0) return false;
            if (delta == null || delta.IsAlmostEqualTo(XYZ.Zero)) return true;
            try
            {
                using var tx = new Transaction(doc, "Restore stage move");
                tx.Start();
                try
                {
                    ElementTransformUtils.MoveElements(doc, ids, new XYZ(-delta.X, -delta.Y, -delta.Z));
                    tx.Commit();
                    return true;
                }
                catch
                {
                    try { tx.RollBack(); } catch { }
                    return false;
                }
            }
            catch { return false; }
        }

        private static string San(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static bool EnsureStagingAreaClear(Document doc, AppSettings settings, IDialogService dialogs, IProjectSettingsProvider projectSettingsProvider)
        {
            using (PerfLogger.Measure("Staging.Validate", $"W={settings.StagingWidth};H={settings.StagingHeight};B={settings.StagingBuffer}"))
            {
                try
                {
                    double w = Math.Max(1.0, settings.StagingWidth);
                    double h = Math.Max(1.0, settings.StagingHeight);
                    double buffer = Math.Max(0.0, settings.StagingBuffer);
                    double halfW = w * 0.5 + buffer;
                    double halfH = h * 0.5 + buffer;

                    var min = new XYZ(-halfW, -halfH, double.NegativeInfinity);
                    var max = new XYZ(halfW, halfH, double.PositiveInfinity);
                    var outline = new Outline(min, max);
                    var bbFilter = new BoundingBoxIntersectsFilter(outline);

                    while (true)
                    {
                        List<Element> offenders;
                        using (PerfLogger.Measure("Staging.OffendersQuery", string.Empty))
                        {
                            var authorizedUids = new HashSet<string>(settings.StagingAuthorizedUids ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                            var authorizedCatNames = new HashSet<string>((settings.StagingAuthorizedCategoryNames ?? new List<string>()).Select(n => n?.Trim() ?? string.Empty), StringComparer.OrdinalIgnoreCase);

                            offenders = new FilteredElementCollector(doc)
                                .WhereElementIsNotElementType()
                                .WherePasses(bbFilter)
                                .ToElements()
                                .Where(e =>
                                {
                                    try
                                    {
                                        var uid = e.UniqueId ?? string.Empty;
                                        if (!string.IsNullOrEmpty(uid) && authorizedUids.Contains(uid)) return false;
                                        var catName = e.Category?.Name ?? string.Empty;
                                        if (!string.IsNullOrWhiteSpace(catName) && authorizedCatNames.Contains(catName)) return false;
                                        return true;
                                    }
                                    catch { return true; }
                                })
                                .ToList();
                        }

                        if (offenders.Count == 0)
                            return true;

                        var catCount = offenders.Select(o => { try { return o.Category?.Name ?? string.Empty; } catch { return string.Empty; } })
                                                 .Where(n => !string.IsNullOrWhiteSpace(n))
                                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                                 .Count();

                        var sb = new StringBuilder();
                        sb.AppendLine($"Staging area is occupied by {offenders.Count} unauthorized element(s) across {catCount} categor(ies).\nArea: W={w}, H={h}, Buffer={buffer}, Mode={settings.StageMoveMode}");
                        sb.AppendLine("Examples:");
                        foreach (var ex in offenders.Take(10))
                        {
                            string cat = string.Empty; try { cat = ex.Category?.Name ?? string.Empty; } catch { }
                            string name = string.Empty; try { name = ex.Name ?? string.Empty; } catch { }
                            string idText = string.Empty; try { idText = ex?.Id?.ToString() ?? string.Empty; } catch { idText = ""; }
                            sb.AppendLine($" - {(string.IsNullOrWhiteSpace(cat) ? "Element" : cat)}: {name} (Id {idText})");
                        }
                        if (offenders.Count > 10) sb.AppendLine($" (+{offenders.Count - 10} more)");

                        var decision = dialogs.StagingPrompt("Batch Export", "Staging area contains unauthorized elements.", sb.ToString());
                        using (PerfLogger.Measure("Staging.Decision", decision.ToString())) { }

                        if (decision == StagingDecision.Cancel)
                            return false;
                        if (decision == StagingDecision.Continue)
                            return true;
                        if (decision == StagingDecision.ResolveElements)
                        {
                            using (PerfLogger.Measure("Staging.Resolve.Elements", offenders.Count.ToString()))
                            {
                                var win = new ElementsPickerWindow(doc, settings, settings.StagingAuthorizedUids) { Owner = null };
                                if (win.ShowDialog() == true)
                                {
                                    var chosen = (win.ResultUids ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s));
                                    var merged = new HashSet<string>(settings.StagingAuthorizedUids ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                                    foreach (var u in chosen) merged.Add(u);
                                    settings.StagingAuthorizedUids = merged.ToList();
                                    projectSettingsProvider.Save(settings);
                                    continue; // re-check
                                }
                                else { continue; }
                            }
                        }
                        if (decision == StagingDecision.ResolveCategories)
                        {
                            using (PerfLogger.Measure("Staging.Resolve.Categories", catCount.ToString()))
                            {
                                // Seed from saved category names -> resolve BuiltInCategory ids for display
                                var seedIds = new List<int>();
                                foreach (var name in settings.StagingAuthorizedCategoryNames ?? new List<string>())
                                {
                                    try
                                    {
                                        foreach (var bic in Enum.GetValues(typeof(BuiltInCategory)).Cast<BuiltInCategory>())
                                        {
                                            try { var cat = Category.GetCategory(doc, bic); if (cat != null && string.Equals(cat.Name, name, StringComparison.OrdinalIgnoreCase)) { seedIds.Add((int)bic); break; } } catch { }
                                        }
                                    }
                                    catch { }
                                }
                                var dlg = new CategoriesPickerWindow(seedIds, doc, initialScope: 3, settings: settings) { Owner = null };
                                if (dlg.ShowDialog() == true)
                                {
                                    // Map picked ids back to names and merge
                                    var newNames = new HashSet<string>(settings.StagingAuthorizedCategoryNames ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                                    foreach (var id in dlg.ResultIds)
                                    {
                                        try
                                        {
                                            var bic = (BuiltInCategory)id;
                                            var cat = Category.GetCategory(doc, bic);
                                            var n = cat?.Name;
                                            if (!string.IsNullOrWhiteSpace(n)) newNames.Add(n);
                                        }
                                        catch { }
                                    }
                                    settings.StagingAuthorizedCategoryNames = newNames.ToList();
                                    projectSettingsProvider.Save(settings);
                                    continue; // re-check
                                }
                                else { continue; }
                            }
                        }
                    }
                }
                catch
                {
                    var proceed = (ServiceBootstrap.Provider.GetService(typeof(IDialogService)) as IDialogService ?? new DialogService())
                        .ConfirmYesNo("Batch Export", "Could not validate staging area.", "Proceed without validation?", defaultYes: false);
                    return proceed;
                }
            }
        }
    }
}
