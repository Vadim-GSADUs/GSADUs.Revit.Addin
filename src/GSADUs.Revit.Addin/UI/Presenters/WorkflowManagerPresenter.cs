using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using GSADUs.Revit.Addin.Workflows.Pdf;
using GSADUs.Revit.Addin.Workflows.Image;
using System.ComponentModel;
using System.Diagnostics;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class WorkflowManagerPresenter
    {
        private readonly WorkflowCatalogService _catalog;
        private readonly IDialogService _dialogs;

        public PdfWorkflowTabViewModel PdfWorkflow { get; } = new PdfWorkflowTabViewModel();
        public ImageWorkflowTabViewModel ImageWorkflow { get; } = new ImageWorkflowTabViewModel();

        public WorkflowManagerPresenter(
            WorkflowCatalogService catalog,
            IDialogService dialogs)
        {
            // File-based Trace listener setup
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "GSADUs.AddinLogs");
                System.IO.Directory.CreateDirectory(dir);
                var logPath = System.IO.Path.Combine(dir, "workflow-manager.log");
                if (!System.Diagnostics.Trace.Listeners.OfType<System.Diagnostics.TextWriterTraceListener>()
                      .Any(l => string.Equals(l.Writer?.ToString(), logPath, StringComparison.OrdinalIgnoreCase)))
                {
                    var tw = System.IO.File.CreateText(logPath); tw.AutoFlush = true;
                    System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(tw));
                    System.Diagnostics.Trace.AutoFlush = true;
                    System.Diagnostics.Trace.WriteLine($"[Init] Trace listener started at {DateTime.Now:u} -> {logPath}");
                }
            }
            catch { /* ignore */ }

            _catalog = catalog;
            _dialogs = dialogs;

            // Initialize scope options
            var scopes = new[] { "CurrentSet", "SelectionSet", "EntireProject" };
            PdfWorkflow.Scopes.Clear(); foreach (var s in scopes) PdfWorkflow.Scopes.Add(s);
            ImageWorkflow.Scopes.Clear(); foreach (var s in scopes) ImageWorkflow.Scopes.Add(s);

            // Wire image whitelist command
            ImageWorkflow.PickWhitelistCommand = new DelegateCommand(_ => ExecutePickWhitelist());
            UpdateImageWhitelistSummary();

            // Observe SelectedWorkflowId changes
            PdfWorkflow.PropertyChanged += VmOnPropertyChanged;
            ImageWorkflow.PropertyChanged += VmOnPropertyChanged;
            System.Diagnostics.Debug.WriteLine($"[Presenter] Image VM instance at attach: {ImageWorkflow.GetHashCode()}");
            ImageWorkflow.PropertyChanged += VmOnPropertyChanged;
            System.Diagnostics.Trace.WriteLine("[Presenter] Subscribed to Image VM PropertyChanged");

            // Seed saved lists
            PopulateSavedLists();

            // Wire ManagePdfSetup via presenter to UI API
            PdfWorkflow.ManagePdfSetupCommand = new DelegateCommand(
                _ => ExecuteManagePdfSetup(),
                _ => !string.IsNullOrWhiteSpace(PdfWorkflow.SelectedPrintSet));

            // Save commands
            PdfWorkflow.SaveCommand = new DelegateCommand(_ => SaveCurrentPdf(), _ => PdfWorkflow.IsSaveEnabled);
            ImageWorkflow.SaveCommand = new DelegateCommand(_ => SaveCurrentImage(), _ => ImageWorkflow.IsSaveEnabled);
        }

        // Centralized deletion to cascade updates across tabs and main
        public void DeleteWorkflow(string id)
        {
            // Service delete returns success/failure
            bool deleted = _catalog.Delete(id);
            if (!deleted) return;

            // Refresh unified list from service
            _catalog.RefreshCaches();

            // Repopulate SavedWorkflows for each tab VM
            PopulateSavedLists();

            // Cascade clear and reset if deleted workflow was selected
            if (string.Equals(PdfWorkflow.SelectedWorkflowId, id, StringComparison.OrdinalIgnoreCase))
            {
                PdfWorkflow.SelectedWorkflowId = null;
                PdfWorkflow.Reset();
            }
            if (string.Equals(ImageWorkflow.SelectedWorkflowId, id, StringComparison.OrdinalIgnoreCase))
            {
                ImageWorkflow.SelectedWorkflowId = null;
                ImageWorkflow.Reset();
            }
        }

        private void SaveCurrentPdf()
        {
            var vm = PdfWorkflow;
            var nameVal = vm?.Name?.Trim() ?? string.Empty;
            var scopeVal = vm?.WorkflowScope ?? string.Empty;
            var descVal = vm?.Description ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nameVal) || string.IsNullOrWhiteSpace(scopeVal)) { _dialogs.Info("Save", "Name and Scope required."); return; }

            var existing = (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>())
                .FirstOrDefault(w => string.Equals(w.Id, vm.SelectedWorkflowId, StringComparison.OrdinalIgnoreCase) && w.Output == OutputType.Pdf);
            if (existing == null)
            {
                existing = new WorkflowDefinition { Id = Guid.NewGuid().ToString("N"), Kind = WorkflowKind.Internal, Output = OutputType.Pdf, ActionIds = new List<string>(), Parameters = new Dictionary<string, JsonElement>() };
                _catalog.Settings.Workflows ??= new List<WorkflowDefinition>();
                _catalog.Settings.Workflows.Add(existing);
                vm.SelectedWorkflowId = existing.Id;
            }

            existing.Name = nameVal; existing.Scope = scopeVal; existing.Description = descVal;
            if (!SavePdfWorkflow(existing)) return;
            vm.SetDirty(false);
            RefreshListsAfterSave();
        }

        private void SaveCurrentImage()
        {
            var vm = ImageWorkflow;
            var nameVal = vm?.Name?.Trim() ?? string.Empty;
            var scopeVal = vm?.WorkflowScope ?? string.Empty;
            var descVal = vm?.Description ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nameVal) || string.IsNullOrWhiteSpace(scopeVal)) { _dialogs.Info("Save", "Name and Scope required."); return; }

            var existing = (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>())
                .FirstOrDefault(w => string.Equals(w.Id, vm.SelectedWorkflowId, StringComparison.OrdinalIgnoreCase) && w.Output == OutputType.Image);
            if (existing == null)
            {
                existing = new WorkflowDefinition { Id = Guid.NewGuid().ToString("N"), Kind = WorkflowKind.Internal, Output = OutputType.Image, ActionIds = new List<string>(), Parameters = new Dictionary<string, JsonElement>() };
                _catalog.Settings.Workflows ??= new List<WorkflowDefinition>();
                _catalog.Settings.Workflows.Add(existing);
                vm.SelectedWorkflowId = existing.Id;
            }

            existing.Name = nameVal; existing.Scope = scopeVal; existing.Description = descVal;
            SaveImageWorkflow(existing);
            vm.SetDirty(false);
            RefreshListsAfterSave();
        }

        private void ExecuteManagePdfSetup()
        {
            try
            {
                var uiapp = RevitUiContext.Current; if (uiapp == null) { _dialogs.Info("PDF", "Revit UI not available."); return; }
                var cmd = Autodesk.Revit.UI.RevitCommandId.LookupPostableCommandId(Autodesk.Revit.UI.PostableCommand.ExportPDF);
                if (cmd != null && uiapp.CanPostCommand(cmd)) uiapp.PostCommand(cmd);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                throw;
            }
        }

        private void PopulateSavedLists()
        {
            var all = _catalog.Workflows?.ToList() ?? new List<WorkflowDefinition>();
            var pdf = all.Where(w => w.Output == OutputType.Pdf)
                         .Select(w => new SavedWorkflowListItem { Id = w.Id, Display = $"{w.Name} - {w.Scope} - {w.Description}" })
                         .ToList();
            var img = all.Where(w => w.Output == OutputType.Image)
                         .Select(w => new SavedWorkflowListItem { Id = w.Id, Display = $"{w.Name} - {w.Scope} - {w.Description}" })
                         .ToList();
            PdfWorkflow.SavedWorkflows.Clear(); foreach (var i in pdf) PdfWorkflow.SavedWorkflows.Add(i);
            ImageWorkflow.SavedWorkflows.Clear(); foreach (var i in img) ImageWorkflow.SavedWorkflows.Add(i);
        }

        private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is PdfWorkflowTabViewModel p && e.PropertyName == nameof(PdfWorkflowTabViewModel.SelectedWorkflowId))
            {
                LoadWorkflowIntoPdfVm(p.SelectedWorkflowId);
            }
            else if (sender is ImageWorkflowTabViewModel i && e.PropertyName == nameof(ImageWorkflowTabViewModel.SelectedWorkflowId))
            {
                System.Diagnostics.Trace.WriteLine($"[Presenter] PropertyChanged(Image.SelectedWorkflowId) from sender {sender?.GetHashCode()} value={(i.SelectedWorkflowId ?? "<null>")}");
                LoadWorkflowIntoImageVm(i.SelectedWorkflowId);
            }
        }

        private void ApplyBaseFields(WorkflowDefinition? wf, WorkflowTabBaseViewModel vm)
        {
            vm.Name = wf?.Name ?? string.Empty;
            vm.WorkflowScope = wf?.Scope ?? string.Empty;
            vm.Description = wf?.Description ?? string.Empty;
        }

        private void LoadWorkflowIntoPdfVm(string? id)
        {
            var wf = (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>()).FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));
            ApplyBaseFields(wf, PdfWorkflow);
            ApplySavedPdfParameters(wf);
            PdfWorkflow.SetDirty(false);
        }

        private void LoadWorkflowIntoImageVm(string? id)
        {
            var wf = (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>()).FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));
            ApplyBaseFields(wf, ImageWorkflow);
            ApplySavedImageParameters(wf);
            ImageWorkflow.SetDirty(false);
        }

        public AppSettings Settings => _catalog.Settings;

        public void OnWindowConstructed(WorkflowManagerWindow win)
        {
        }

        public void OnLoaded(UIDocument? uidoc, WorkflowManagerWindow win)
        {
            PdfWorkflow.ApplySettings(Settings);

            try
            {
                var doc = uidoc?.Document;
                // Hydrate collections on load, driven by VM-only bindings
                PopulateSavedLists();
                if (doc != null)
                {
                    HydratePdfVmSources(doc);
                    PopulateImageSources(doc);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                throw;
            }
        }

        public void HydratePdfVmSources(Document doc)
        {
            if (doc == null) return;
            try
            {
                // Populate available view sets
                var setNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheetSet))
                    .Cast<ViewSheetSet>()
                    .Select(s => s.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();
                var prevSet = PdfWorkflow.SelectedSetName;
                PdfWorkflow.AvailableViewSets.Clear();
                foreach (var n in setNames) PdfWorkflow.AvailableViewSets.Add(n);
                if (!string.IsNullOrWhiteSpace(prevSet) && setNames.Contains(prevSet))
                    PdfWorkflow.SelectedSetName = prevSet;
                else
                    PdfWorkflow.SelectedSetName = null;

                // Populate available export setups
                var setupNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(ExportPDFSettings))
                    .Cast<ExportPDFSettings>()
                    .Select(s => s.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();
                var prevSetup = PdfWorkflow.SelectedPrintSet;
                PdfWorkflow.AvailableExportSetups.Clear();
                foreach (var n in setupNames) PdfWorkflow.AvailableExportSetups.Add(n);
                if (!string.IsNullOrWhiteSpace(prevSetup) && setupNames.Contains(prevSetup))
                    PdfWorkflow.SelectedPrintSet = prevSetup;
                else
                    PdfWorkflow.SelectedPrintSet = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                throw;
            }
        }

        private void ExecutePickWhitelist()
        {
            try
            {
                var doc = RevitUiContext.Current?.ActiveUIDocument?.Document;
                var preselected = Settings.ImageBlacklistCategoryIds ?? new List<int>();
                var dlg = new CategoriesPickerWindow(preselected, doc, initialScope: 2);
                if (dlg.ShowDialog() == true)
                {
                    Settings.ImageBlacklistCategoryIds = dlg.ResultIds?.Distinct().ToList() ?? new List<int>();
                    try { _catalog.Save(); } catch (Exception ex) { Debug.WriteLine(ex); throw; }
                    // Update summary immediately after selection
                    var summary = ComputeImageWhitelistSummary();
                    ImageWorkflow.WhitelistSummary = summary;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                throw;
            }
        }

        // Helper for summary computation
        private string ComputeImageWhitelistSummary()
        {
            var ids = Settings.ImageBlacklistCategoryIds ?? new List<int>();
            if (ids.Count == 0) return "(all categories)";
            var doc = RevitUiContext.Current?.ActiveUIDocument?.Document;
            string NameOrEnum(int id)
            {
                try
                {
                    if (doc != null)
                    {
                        var cat = Category.GetCategory(doc, (BuiltInCategory)id);
                        if (cat != null && !string.IsNullOrWhiteSpace(cat.Name)) return cat.Name;
                    }
                }
                catch { }
                var s = ((BuiltInCategory)id).ToString();
                return s.StartsWith("OST_") ? s.Substring(4) : s;
            }
            return string.Join(", ", ids.Select(NameOrEnum));
        }

        private void UpdateImageWhitelistSummary()
        {
            var ids = Settings.ImageBlacklistCategoryIds ?? new List<int>();
            if (ids.Count == 0) { ImageWorkflow.WhitelistSummary = "(all categories)"; return; }
            var doc = RevitUiContext.Current?.ActiveUIDocument?.Document;
            string NameOrEnum(int id)
            {
                try
                {
                    if (doc != null)
                    {
                        var cat = Category.GetCategory(doc, (BuiltInCategory)id);
                        if (cat != null && !string.IsNullOrWhiteSpace(cat.Name)) return cat.Name;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    throw;
                }
                var s = ((BuiltInCategory)id).ToString();
                return s.StartsWith("OST_") ? s.Substring(4) : s;
            }
            var computedSummary = string.Join(", ", ids.Select(NameOrEnum));
            ImageWorkflow.WhitelistSummary = computedSummary;
        }

        private void PopulateImageSources(Document doc)
        {
            if (doc == null) return;
            try
            {
                var setNamesForImage = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheetSet))
                    .Cast<ViewSheetSet>()
                    .Select(s => s.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();

                ImageWorkflow.AvailablePrintSets.Clear();
                foreach (var n in setNamesForImage)
                    ImageWorkflow.AvailablePrintSets.Add(n);

                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate
                                && v.ViewType != ViewType.ThreeD
                                && v.ViewType != ViewType.DrawingSheet
                                && (v is ViewPlan || v is ViewDrafting || v is ViewSection || v.ViewType == ViewType.AreaPlan))
                    .OrderBy(v => v.ViewType.ToString())
                    .ThenBy(v => v.Name)
                    .Select(v => new SingleViewOption { Id = v.Id.ToString(), Label = $"{v.ViewType} - {v.Name}" })
                    .ToList();
                ImageWorkflow.AvailableSingleViews.Clear();
                foreach (var o in views)
                    ImageWorkflow.AvailableSingleViews.Add(o);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                throw;
            }
        }

        public void ApplySavedPdfParameters(WorkflowDefinition? wf)
        {
            var p = wf?.Parameters;
            if (p == null)
            {
                // Ensure default prefill when no parameters saved
                if (string.IsNullOrWhiteSpace(PdfWorkflow.Pattern)) PdfWorkflow.Pattern = "{SetName}.pdf";
                return;
            }
            string Gs(string k) =>
                (p != null && p.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String)
                    ? (v.GetString() ?? string.Empty)
                    : string.Empty;
            PdfWorkflow.SelectedSetName  = Gs(PdfWorkflowKeys.PrintSetName);
            PdfWorkflow.SelectedPrintSet = Gs(PdfWorkflowKeys.ExportSetupName);
            var pattern = Gs(PdfWorkflowKeys.FileNamePattern);
            PdfWorkflow.Pattern = string.IsNullOrWhiteSpace(pattern) ? "{SetName}.pdf" : pattern;
        }

        public void ApplySavedImageParameters(WorkflowDefinition? wf)
        {
            var p = wf?.Parameters;
            if (p == null)
            {
                if (string.IsNullOrWhiteSpace(ImageWorkflow.Pattern)) ImageWorkflow.Pattern = "{SetName}";
                return;
            }
            string Gs(string k) =>
                (p != null && p.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String)
                    ? (v.GetString() ?? string.Empty)
                    : string.Empty;
            ImageWorkflow.ExportScope          = Gs(ImageWorkflowKeys.exportScope);
            ImageWorkflow.SelectedPrintSet     = Gs(ImageWorkflowKeys.imagePrintSetName);
            ImageWorkflow.SelectedSingleViewId = Gs(ImageWorkflowKeys.singleViewId);
            var pattern = Gs(ImageWorkflowKeys.fileNamePattern);
            ImageWorkflow.Pattern              = string.IsNullOrWhiteSpace(pattern) ? "{SetName}" : pattern;
            ImageWorkflow.Format               = Gs(ImageWorkflowKeys.imageFormat);
            // ---- META (no params dict needed) ----
            ImageWorkflow.Name          = wf?.Name  ?? string.Empty;
            ImageWorkflow.WorkflowScope = wf?.Scope ?? string.Empty;
            ImageWorkflow.Description   = wf?.Description ?? string.Empty;
            // ---- PARAMS (guarded; do NOT touch Resolution) ----
            var cm = Gs(ImageWorkflowKeys.cropMode);
            if (!string.IsNullOrWhiteSpace(cm)) ImageWorkflow.CropMode = cm;
            var co = Gs(ImageWorkflowKeys.cropOffset);
            if (!string.IsNullOrWhiteSpace(co)) ImageWorkflow.CropOffset = co;
            // Leave Resolution as-is. Do not set it here.
        }

        private static void EnsureActionId(WorkflowDefinition wf, string id)
        {
            wf.ActionIds ??= new List<string>();
            if (!wf.ActionIds.Any(a => string.Equals(a, id, StringComparison.OrdinalIgnoreCase))) wf.ActionIds.Add(id);
        }

        private static JsonElement J(string value)
        {
            using var d = JsonDocument.Parse("\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"");
            return d.RootElement.Clone();
        }

        public bool SavePdfWorkflow(WorkflowDefinition existing)
        {
            // Hard guard: require all three fields and valid pattern without showing dialogs
            var pdf = PdfWorkflow;
            var setName = pdf?.SelectedSetName;
            var setupName = pdf?.SelectedPrintSet;
            var pattern = pdf?.PdfPattern;

            // Trace diagnostics for first-save
            try { System.Diagnostics.Trace.WriteLine($"[SavePdf] set='{setName ?? "<null>"}' setup='{setupName ?? "<null>"}' pattern='{pattern ?? "<null>"}'"); } catch { }

            if (string.IsNullOrWhiteSpace(setName)
                || string.IsNullOrWhiteSpace(setupName)
                || string.IsNullOrWhiteSpace(pattern)
                || !pattern.Contains("{SetName}"))
            {
                return false; // fail fast, do not modify settings
            }

            // Build parameters as-is (no normalization/sanitization)
            var p = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [PdfWorkflowKeys.PrintSetName] = J(setName!),
                [PdfWorkflowKeys.ExportSetupName] = J(setupName!),
                [PdfWorkflowKeys.FileNamePattern] = J(pattern!)
            };
            existing.Parameters = p;
            EnsureActionId(existing, "export-pdf");
            return true;
        }

        public void SaveImageWorkflow(WorkflowDefinition existing)
        {
            var vm = ImageWorkflow;
            var imageParams = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            void SP(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) imageParams[k] = J(v); }

            // Normalize pattern to always include {SetName}
            var pat = vm?.Pattern;
            if (string.IsNullOrWhiteSpace(pat)) pat = "{SetName}";
            if (!pat.Contains("{SetName}")) pat = "{SetName}" + pat;

            SP(ImageWorkflowKeys.imagePrintSetName, vm?.SelectedPrintSet);
            SP(ImageWorkflowKeys.fileNamePattern, pat);

            var cropMode = vm?.CropMode;
            if (!string.IsNullOrWhiteSpace(cropMode) && !string.Equals(cropMode, "Static", StringComparison.OrdinalIgnoreCase))
                SP(ImageWorkflowKeys.cropMode, cropMode);

            var cropOffset = vm?.CropOffset;
            if (!string.IsNullOrWhiteSpace(cropOffset)) SP(ImageWorkflowKeys.cropOffset, cropOffset);

            SP(ImageWorkflowKeys.resolutionPreset, vm?.Resolution);

            var fmt = vm?.Format;
            if (!string.IsNullOrWhiteSpace(fmt))
            {
                var up = fmt.Trim().ToUpperInvariant();
                if (up == "PNG" || up == "BMP" || up == "TIFF") SP(ImageWorkflowKeys.imageFormat, up);
            }

            var scope = vm?.ExportScope ?? "PrintSet";
            imageParams[ImageWorkflowKeys.exportScope] = J(scope);
            if (string.Equals(scope, "SingleView", StringComparison.OrdinalIgnoreCase))
            {
                var svId = vm?.SelectedSingleViewId;
                if (!string.IsNullOrWhiteSpace(svId)) imageParams[ImageWorkflowKeys.singleViewId] = J(svId);
            }

            existing.Parameters = imageParams;
            EnsureActionId(existing, "export-image");
        }

        public void RefreshListsAfterSave()
        {
            _catalog.SaveAndRefresh();
            PopulateSavedLists();
            // Restore selection for Image tab after save
            var savedId = ImageWorkflow.SelectedWorkflowId;
            if (!string.IsNullOrWhiteSpace(savedId) &&
                ImageWorkflow.SavedWorkflows.Any(w => string.Equals(w.Id, savedId, StringComparison.OrdinalIgnoreCase)))
            {
                ImageWorkflow.SelectedWorkflowId = savedId;
            }
            else if (string.IsNullOrWhiteSpace(ImageWorkflow.SelectedWorkflowId) &&
                     ImageWorkflow.SavedWorkflows.Count > 0)
            {
                ImageWorkflow.SelectedWorkflowId = ImageWorkflow.SavedWorkflows[0].Id;
            }
        }

        public void SaveSettings()
        {
            _catalog.Save();
        }

        private void ImageWorkflow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is ImageWorkflowTabViewModel i && e.PropertyName == nameof(ImageWorkflowTabViewModel.SelectedWorkflowId))
            {
                System.Diagnostics.Debug.WriteLine($"[Presenter] PropertyChanged(Image.SelectedWorkflowId) from sender {sender?.GetHashCode()} value={(i.SelectedWorkflowId ?? "<null>")}");
                LoadWorkflowIntoImageVm(i.SelectedWorkflowId);
            }
        }
    }
}
