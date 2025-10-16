using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using GSADUs.Revit.Addin.Workflows.Pdf;
using GSADUs.Revit.Addin.Workflows.Image;
using System.ComponentModel;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class WorkflowManagerPresenter
    {
        private readonly WorkflowCatalogService _catalog;
        private readonly IDialogService _dialogs;

        public PdfWorkflowTabViewModel PdfWorkflow { get; } = new PdfWorkflowTabViewModel();
        public ImageWorkflowTabViewModel ImageWorkflow { get; } = new ImageWorkflowTabViewModel();

        public WorkflowManagerPresenter(WorkflowCatalogService catalog, IDialogService dialogs)
        {
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

            // Seed saved lists and observe catalog changes via simple method
            PopulateSavedLists();

            // Wire ManagePdfSetup via presenter to UI API
            PdfWorkflow.ManagePdfSetupCommand = new DelegateCommand(_ => ExecuteManagePdfSetup(), _ => PdfWorkflow.IsPdfEnabled);

            // Save commands
            PdfWorkflow.SaveCommand = new DelegateCommand(_ => SaveCurrentPdf(), _ => PdfWorkflow.IsSaveEnabled);
            ImageWorkflow.SaveCommand = new DelegateCommand(_ => SaveCurrentImage(), _ => ImageWorkflow.IsSaveEnabled);
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
            catch { }
        }

        private void PopulateSavedLists()
        {
            try
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
            catch { }
        }

        private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is PdfWorkflowTabViewModel p && e.PropertyName == nameof(PdfWorkflowTabViewModel.SelectedWorkflowId))
            {
                LoadWorkflowIntoPdfVm(p.SelectedWorkflowId);
            }
            else if (sender is ImageWorkflowTabViewModel i && e.PropertyName == nameof(ImageWorkflowTabViewModel.SelectedWorkflowId))
            {
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
            try { PdfWorkflow.ApplySettings(Settings); } catch { }

            try
            {
                var doc = uidoc?.Document;
                PdfWorkflow.IsPdfEnabled = !(doc == null || doc.IsFamilyDocument);
            }
            catch { }
        }

        private void UpdateImageWhitelistSummary()
        {
            try
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
                    catch { }
                    var s = ((BuiltInCategory)id).ToString();
                    return s.StartsWith("OST_") ? s.Substring(4) : s;
                }
                ImageWorkflow.WhitelistSummary = string.Join(", ", ids.Select(NameOrEnum));
            }
            catch { }
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
                    try { _catalog.Save(); } catch { }
                    UpdateImageWhitelistSummary();
                }
            }
            catch { }
        }

        public void OnSavedComboChanged(string tag, WorkflowDefinition? wf, WorkflowManagerWindow win)
        {
        }

        public void OnMarkDirty(string tag)
        {
            try { PerfLogger.Write("WorkflowManager.MarkDirty", tag, TimeSpan.Zero); } catch { }
        }

        public void SaveSettings() => _catalog.Save();

        public void PopulatePdfSources(Document doc)
        {
            if (doc == null) return;
            try
            {
                var setNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheetSet))
                    .Cast<ViewSheetSet>()
                    .Select(s => s.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();
                PdfWorkflow.AvailableViewSets.Clear();
                foreach (var n in setNames) PdfWorkflow.AvailableViewSets.Add(n);

                var setupNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(ExportPDFSettings))
                    .Cast<ExportPDFSettings>()
                    .Select(s => s.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();
                PdfWorkflow.AvailableExportSetups.Clear();
                foreach (var n in setupNames) PdfWorkflow.AvailableExportSetups.Add(n);
            }
            catch { }
        }

        public void PopulateImageSources(Document doc)
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
            catch { }
        }

        public void ApplySavedPdfParameters(WorkflowDefinition? wf)
        {
            try
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
            catch { }
        }

        public void ApplySavedImageParameters(WorkflowDefinition? wf)
        {
            try
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
                ImageWorkflow.Prefix               = Gs(ImageWorkflowKeys.prefix);
                ImageWorkflow.Suffix               = Gs(ImageWorkflowKeys.suffix);
                ImageWorkflow.Format               = Gs(ImageWorkflowKeys.imageFormat);
                ImageWorkflow.Resolution           = Gs(ImageWorkflowKeys.resolutionPreset);
                ImageWorkflow.CropMode             = Gs(ImageWorkflowKeys.cropMode);
                ImageWorkflow.CropOffset           = Gs(ImageWorkflowKeys.cropOffset);
            }
            catch { }
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
            var vm = PdfWorkflow;
            var pdfSetName = vm?.SelectedSetName;
            var pdfSetupName = vm?.SelectedPrintSet;
            var pdfPattern = vm?.Pattern;

            // Require set and setup; pattern will be normalized automatically
            if (string.IsNullOrWhiteSpace(pdfSetName) || string.IsNullOrWhiteSpace(pdfSetupName))
            {
                _dialogs.Info("Save", "Select a view/sheet set and an export setup.");
                return false;
            }

            // Normalize pattern: ensure {SetName} present and .pdf extension
            if (string.IsNullOrWhiteSpace(pdfPattern)) pdfPattern = "{SetName}.pdf";
            if (!pdfPattern.Contains("{SetName}")) pdfPattern = "{SetName}" + pdfPattern;
            if (!pdfPattern.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) pdfPattern += ".pdf";

            // basic sanitize (mirror window helper without dependency)
            try
            {
                var invalid = System.IO.Path.GetInvalidFileNameChars();
                var s = pdfPattern.Trim();
                foreach (var c in invalid) s = s.Replace(c, '_');
                s = s.Replace('/', '_').Replace('\\', '_');
                pdfPattern = s;
            }
            catch { }

            var p = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [PdfWorkflowKeys.PrintSetName] = J(pdfSetName!),
                [PdfWorkflowKeys.ExportSetupName] = J(pdfSetupName!),
                [PdfWorkflowKeys.FileNamePattern] = J(pdfPattern!)
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
            SP(ImageWorkflowKeys.prefix, vm?.Prefix);
            SP(ImageWorkflowKeys.suffix, vm?.Suffix);

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

            try
            {
                var scope = vm?.ExportScope ?? "PrintSet";
                imageParams[ImageWorkflowKeys.exportScope] = J(scope);
                if (string.Equals(scope, "SingleView", StringComparison.OrdinalIgnoreCase))
                {
                    var svId = vm?.SelectedSingleViewId;
                    if (!string.IsNullOrWhiteSpace(svId)) imageParams[ImageWorkflowKeys.singleViewId] = J(svId);
                }
            }
            catch { }

            existing.Parameters = imageParams;
            EnsureActionId(existing, "export-image");
        }

        public void RefreshListsAfterSave()
        {
            try
            {
                _catalog.SaveAndRefresh();
                PopulateSavedLists();
            }
            catch { }
        }
    }
}
