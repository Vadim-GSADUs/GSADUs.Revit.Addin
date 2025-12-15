using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using GSADUs.Revit.Addin.Workflows.Pdf;
using GSADUs.Revit.Addin.Workflows.Image;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed partial class WorkflowManagerPresenter
    {
        private readonly WorkflowCatalogService _catalog;
        private readonly IDialogService _dialogs;
        private readonly ProjectSettingsSaveExternalEvent _settingsSaver;
        private readonly WorkflowCatalogChangeNotifier _catalogNotifier;
        private bool _isDirty; // tracks unsaved catalog/settings changes

        public PdfWorkflowTabViewModel PdfWorkflow { get; } = new PdfWorkflowTabViewModel();
        public ImageWorkflowTabViewModel ImageWorkflow { get; } = new ImageWorkflowTabViewModel();

        public WorkflowManagerPresenter(
            WorkflowCatalogService catalog,
            IDialogService dialogs,
            WorkflowCatalogChangeNotifier catalogNotifier,
            ProjectSettingsSaveExternalEvent settingsSaver)
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
            _catalogNotifier = catalogNotifier ?? throw new ArgumentNullException(nameof(catalogNotifier));

            _settingsSaver = settingsSaver ?? throw new ArgumentNullException(nameof(settingsSaver));

            // Initialize scope options: PDF/Image are SelectionSet only
            var scopesPdfImg = new[] { "SelectionSet" };
            PdfWorkflow.Scopes.Clear(); foreach (var s in scopesPdfImg) PdfWorkflow.Scopes.Add(s);
            ImageWorkflow.Scopes.Clear(); foreach (var s in scopesPdfImg) ImageWorkflow.Scopes.Add(s);

            // Wire CSV tab
            try { WireCsv(); } catch { }

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
            if (CsvWorkflow != null && string.Equals(CsvWorkflow.SelectedWorkflowId, id, StringComparison.OrdinalIgnoreCase))
            {
                CsvWorkflow.SelectedWorkflowId = null;
                CsvWorkflow.Reset();
            }
        }

        public WorkflowDefinition? RenameWorkflow(WorkflowDefinition? workflow)
        {
            if (workflow == null) return null;

            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            var dialog = new RenameWorkflowDialog(workflow.Name ?? string.Empty)
            {
                Owner = owner
            };

            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            var newName = dialog.ResultName?.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                _dialogs.Info("Rename Workflow", "Name cannot be empty.");
                return null;
            }

            bool duplicate = _catalog.Workflows.Any(w => !string.Equals(w.Id, workflow.Id, StringComparison.OrdinalIgnoreCase)
                                                         && string.Equals(w.Name, newName, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                _dialogs.Info("Rename Workflow", "A workflow with that name already exists.");
                return null;
            }

            _catalog.Rename(workflow.Id, newName);
            PopulateSavedLists();
            return _catalog.Workflows.FirstOrDefault(w => string.Equals(w.Id, workflow.Id, StringComparison.OrdinalIgnoreCase));
        }

        private void SaveCurrentPdf()
        {
            var vm = PdfWorkflow;
            var nameVal = vm?.Name?.Trim() ?? string.Empty;
            var descVal = vm?.Description ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nameVal)) { _dialogs.Info("Save", "Name is required."); return; }

            var existing = (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>())
                .FirstOrDefault(w => string.Equals(w.Id, vm.SelectedWorkflowId, StringComparison.OrdinalIgnoreCase) && w.Output == OutputType.Pdf);
            if (existing == null)
            {
                existing = new WorkflowDefinition { Id = Guid.NewGuid().ToString("N"), Kind = WorkflowKind.Internal, Output = OutputType.Pdf, ActionIds = new List<string>(), Parameters = new Dictionary<string, JsonElement>() };
                _catalog.Settings.Workflows ??= new List<WorkflowDefinition>();
                _catalog.Settings.Workflows.Add(existing);
                vm.SelectedWorkflowId = existing.Id;
            }

            existing.Name = nameVal; existing.Scope = "SelectionSet"; existing.Description = descVal;
            if (!SavePdfWorkflow(existing)) return;
            vm.SetDirty(false);
            _isDirty = true;
            RefreshLists();
            PersistChanges(success =>
            {
                if (!success)
                {
                    _dialogs.Info("Save Workflow", "Failed to persist workflow changes. Please try again.");
                }
            });
        }

        private void SaveCurrentImage()
        {
            var vm = ImageWorkflow;
            var nameVal = vm?.Name?.Trim() ?? string.Empty;
            var descVal = vm?.Description ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nameVal)) { _dialogs.Info("Save", "Name is required."); return; }

            var existing = (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>())
                .FirstOrDefault(w => string.Equals(w.Id, vm.SelectedWorkflowId, StringComparison.OrdinalIgnoreCase) && w.Output == OutputType.Image);
            if (existing == null)
            {
                existing = new WorkflowDefinition { Id = Guid.NewGuid().ToString("N"), Kind = WorkflowKind.Internal, Output = OutputType.Image, ActionIds = new List<string>(), Parameters = new Dictionary<string, JsonElement>() };
                _catalog.Settings.Workflows ??= new List<WorkflowDefinition>();
                _catalog.Settings.Workflows.Add(existing);
                vm.SelectedWorkflowId = existing.Id;
            }

            existing.Name = nameVal; existing.Scope = "SelectionSet"; existing.Description = descVal;
            if (!SaveImageWorkflowInternal(existing)) return; // abort if validation fails
            vm.SetDirty(false);
            _isDirty = true;
            RefreshLists();
            PersistChanges(success =>
            {
                if (!success)
                {
                    _dialogs.Info("Save Workflow", "Failed to persist workflow changes. Please try again.");
                }
            });
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
            var csv = all.Where(w => w.Output == OutputType.Csv)
                         .Select(w => new SavedWorkflowListItem { Id = w.Id, Display = $"{w.Name} - {w.Scope} - {w.Description}" })
                         .ToList();

            // Temporarily detach handlers to avoid transient reloads during list mutations
            try
            {
                PdfWorkflow.PropertyChanged -= VmOnPropertyChanged;
                ImageWorkflow.PropertyChanged -= VmOnPropertyChanged;
                CsvWorkflow.PropertyChanged -= VmOnPropertyChanged;

                PdfWorkflow.SavedWorkflows.Clear(); foreach (var i in pdf) PdfWorkflow.SavedWorkflows.Add(i);
                ImageWorkflow.SavedWorkflows.Clear(); foreach (var i in img) ImageWorkflow.SavedWorkflows.Add(i);
                CsvWorkflow.SavedWorkflows.Clear(); foreach (var i in csv) CsvWorkflow.SavedWorkflows.Add(i);
            }
            finally
            {
                // Reattach deterministically (ensure single subscription)
                PdfWorkflow.PropertyChanged -= VmOnPropertyChanged;
                PdfWorkflow.PropertyChanged += VmOnPropertyChanged;
                ImageWorkflow.PropertyChanged -= VmOnPropertyChanged;
                ImageWorkflow.PropertyChanged += VmOnPropertyChanged;
                CsvWorkflow.PropertyChanged -= VmOnPropertyChanged;
                CsvWorkflow.PropertyChanged += VmOnPropertyChanged;
            }
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
            else if (sender is CsvWorkflowTabViewModel c && e.PropertyName == nameof(CsvWorkflowTabViewModel.SelectedWorkflowId))
            {
                LoadWorkflowIntoCsvVm(c.SelectedWorkflowId);
            }
        }

        private void ApplyBaseFields(WorkflowDefinition? wf, WorkflowTabBaseViewModel vm)
        {
            vm.Name = wf?.Name ?? string.Empty;
            var scopeRaw = wf?.Scope ?? string.Empty;
            if (string.IsNullOrWhiteSpace(scopeRaw)) scopeRaw = "SelectionSet"; // default predominant scope
            vm.WorkflowScope = scopeRaw;
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
                    // CSV sources
                    try { PopulateCsvSources(doc); } catch { }
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
                var preselected = Settings.ImageWhitelistCategoryIds ?? new List<int>();
                var dlg = new CategoriesPickerWindow(preselected, doc, initialScope: 2);
                if (dlg.ShowDialog() == true)
                {
                    Settings.ImageWhitelistCategoryIds = dlg.ResultIds?.Distinct().ToList() ?? new List<int>();
                    _isDirty = true;

                    // Update summary immediately after selection (in-memory only; persistence waits for explicit Save).
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
            var ids = Settings.ImageWhitelistCategoryIds ?? new List<int>();
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
            var ids = Settings.ImageWhitelistCategoryIds ?? new List<int>();
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

                // Include plans/sections/drafting/area plans AND 3D views (perspective + orthographic). Exclude templates and sheets.
                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate
                                && v.ViewType != ViewType.DrawingSheet
                                && (v is ViewPlan || v is ViewDrafting || v is ViewSection || v.ViewType == ViewType.AreaPlan || v is View3D))
                    .OrderBy(v => v.ViewType.ToString())
                    .ThenBy(v => v.Name)
                    .Select(v =>
                    {
                        string label;
                        if (v is View3D v3)
                        {
                            bool isPerspective = false; try { isPerspective = v3.IsPerspective; } catch { }
                            label = isPerspective ? $"3D (Perspective) - {v.Name}" : $"3D - {v.Name}";
                        }
                        else
                        {
                            label = $"{v.ViewType} - {v.Name}";
                        }
                        return new SingleViewOption { Id = v.Id.ToString(), Label = label };
                    })
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
                if (string.IsNullOrWhiteSpace(ImageWorkflow.Format)) ImageWorkflow.Format = "PNG";
                if (string.IsNullOrWhiteSpace(ImageWorkflow.Resolution)) ImageWorkflow.Resolution = "Ultra"; // updated default
                if (string.IsNullOrWhiteSpace(ImageWorkflow.CropMode)) ImageWorkflow.CropMode = "Static";
                return;
            }
            string Gs(string k) => (p != null && p.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String) ? (v.GetString() ?? string.Empty) : string.Empty;
            ImageWorkflow.ExportScope = Gs(ImageWorkflowKeys.exportScope);
            ImageWorkflow.SelectedPrintSet = Gs(ImageWorkflowKeys.imagePrintSetName);
            ImageWorkflow.SelectedSingleViewId = Gs(ImageWorkflowKeys.singleViewId);
            var pattern = Gs(ImageWorkflowKeys.fileNamePattern);
            ImageWorkflow.Pattern = string.IsNullOrWhiteSpace(pattern) ? "{SetName}" : pattern;
            var fmt = Gs(ImageWorkflowKeys.imageFormat);
            ImageWorkflow.Format = string.IsNullOrWhiteSpace(fmt) ? "PNG" : fmt;
            ImageWorkflow.Name = wf?.Name ?? string.Empty;
            ImageWorkflow.WorkflowScope = string.IsNullOrWhiteSpace(wf?.Scope) ? "SelectionSet" : (wf?.Scope ?? "SelectionSet");
            ImageWorkflow.Description = wf?.Description ?? string.Empty;
            var cm = Gs(ImageWorkflowKeys.cropMode);
            ImageWorkflow.CropMode = string.IsNullOrWhiteSpace(cm) ? "Static" : cm;
            var co = Gs(ImageWorkflowKeys.cropOffset);
            ImageWorkflow.CropOffset = string.IsNullOrWhiteSpace(co) ? string.Empty : co;
            var hf = Gs(ImageWorkflowKeys.heuristicFovDeg);
            ImageWorkflow.HeuristicFov = string.IsNullOrWhiteSpace(hf) ? (string.IsNullOrWhiteSpace(ImageWorkflow.HeuristicFov) ? "50" : ImageWorkflow.HeuristicFov) : hf;
            var hb = Gs(ImageWorkflowKeys.heuristicFovBufferPct);
            ImageWorkflow.HeuristicBufferPct = string.IsNullOrWhiteSpace(hb) ? (string.IsNullOrWhiteSpace(ImageWorkflow.HeuristicBufferPct) ? "5" : ImageWorkflow.HeuristicBufferPct) : hb;
            var res = Gs(ImageWorkflowKeys.resolutionPreset);
            ImageWorkflow.Resolution = string.IsNullOrWhiteSpace(res) ? "Ultra" : res; // updated default when missing
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
            // Legacy signature retained for compatibility; new validation wraps Save logic.
            _ = SaveImageWorkflowInternal(existing);
        }

        private bool SaveImageWorkflowInternal(WorkflowDefinition existing)
        {
            var vm = ImageWorkflow;
            if (vm == null) return false;

            // Basic validation aligned with VM logic
            var rawScope = vm.ExportScope;
            if (string.IsNullOrWhiteSpace(rawScope) ||
                (!rawScope.Equals("PrintSet", StringComparison.OrdinalIgnoreCase) &&
                 !rawScope.Equals("SingleView", StringComparison.OrdinalIgnoreCase)))
            {
                rawScope = "PrintSet"; // fallback
            }

            // Range validation depending on scope
            if (rawScope.Equals("PrintSet", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(vm.SelectedPrintSet))
                return false;
            if (rawScope.Equals("SingleView", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(vm.SelectedSingleViewId))
                return false;
            if (string.IsNullOrWhiteSpace(vm.Name)) return false; // name required
            if (string.IsNullOrWhiteSpace(vm.Pattern) || !vm.Pattern.Contains("{SetName}")) return false; // pattern requirement
            if (string.IsNullOrWhiteSpace(vm.Format)) vm.Format = "PNG"; // default format
            if (string.IsNullOrWhiteSpace(vm.Resolution)) vm.Resolution = "Medium"; // resolution default

            var imageParams = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            JsonElement J(string? v)
            {
                using var d = JsonDocument.Parse("\"" + (v ?? string.Empty).Replace("\"", "\\\"") + "\"");
                return d.RootElement.Clone();
            }
            void SP(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) imageParams[k] = J(v); }

            // Normalize pattern to always include {SetName}
            var pat = vm.Pattern;
            if (string.IsNullOrWhiteSpace(pat)) pat = "{SetName}";
            if (!pat.Contains("{SetName}")) pat = "{SetName}" + pat;
            SP(ImageWorkflowKeys.fileNamePattern, pat);

            // Scope persistence
            SP(ImageWorkflowKeys.exportScope, rawScope);
            if (rawScope.Equals("SingleView", StringComparison.OrdinalIgnoreCase))
            {
                SP(ImageWorkflowKeys.singleViewId, vm.SelectedSingleViewId);
            }
            else
            {
                SP(ImageWorkflowKeys.imagePrintSetName, vm.SelectedPrintSet);
            }

            // Crop settings
            if (!string.IsNullOrWhiteSpace(vm.CropMode) && !vm.CropMode.Equals("Static", StringComparison.OrdinalIgnoreCase))
                SP(ImageWorkflowKeys.cropMode, vm.CropMode);
            SP(ImageWorkflowKeys.cropOffset, vm.CropOffset);

            // Heuristic camera params
            SP(ImageWorkflowKeys.heuristicFovDeg, vm.HeuristicFov);
            SP(ImageWorkflowKeys.heuristicFovBufferPct, vm.HeuristicBufferPct);

            // Resolution + format
            SP(ImageWorkflowKeys.resolutionPreset, vm.Resolution);
            var upFmt = (vm.Format ?? string.Empty).Trim().ToUpperInvariant();
            if (upFmt is "PNG" or "BMP" or "TIFF") SP(ImageWorkflowKeys.imageFormat, upFmt); else SP(ImageWorkflowKeys.imageFormat, "PNG");

            existing.Parameters = imageParams;
            EnsureActionId(existing, "export-image");
            return true;
        }

        private void RefreshLists()
        {
            // Cache current selections to restore after refresh
            var pdfId = PdfWorkflow.SelectedWorkflowId;
            var imgId = ImageWorkflow.SelectedWorkflowId;
            var csvId = CsvWorkflow.SelectedWorkflowId;

            // Temporarily detach handlers to avoid transient reloads during refresh + selection restore
            try
            {
                PdfWorkflow.PropertyChanged -= VmOnPropertyChanged;
                ImageWorkflow.PropertyChanged -= VmOnPropertyChanged;
                CsvWorkflow.PropertyChanged -= VmOnPropertyChanged;

                _catalog.RefreshCaches();
                PopulateSavedLists();

                // Restore selection for PDF tab after save
                if (!string.IsNullOrWhiteSpace(pdfId) &&
                    PdfWorkflow.SavedWorkflows.Any(w => string.Equals(w.Id, pdfId, StringComparison.OrdinalIgnoreCase)))
                {
                    PdfWorkflow.SelectedWorkflowId = pdfId;
                }
                else if (string.IsNullOrWhiteSpace(PdfWorkflow.SelectedWorkflowId) &&
                         PdfWorkflow.SavedWorkflows.Count > 0)
                {
                    PdfWorkflow.SelectedWorkflowId = PdfWorkflow.SavedWorkflows[0].Id;
                }

                // Restore selection for Image tab after save (use cached id)
                if (!string.IsNullOrWhiteSpace(imgId) &&
                    ImageWorkflow.SavedWorkflows.Any(w => string.Equals(w.Id, imgId, StringComparison.OrdinalIgnoreCase)))
                {
                    ImageWorkflow.SelectedWorkflowId = imgId;
                }
                else if (string.IsNullOrWhiteSpace(ImageWorkflow.SelectedWorkflowId) &&
                         ImageWorkflow.SavedWorkflows.Count > 0)
                {
                    ImageWorkflow.SelectedWorkflowId = ImageWorkflow.SavedWorkflows[0].Id;
                }

                // Restore selection for CSV tab
                if (!string.IsNullOrWhiteSpace(csvId) &&
                    CsvWorkflow.SavedWorkflows.Any(w => string.Equals(w.Id, csvId, StringComparison.OrdinalIgnoreCase)))
                {
                    CsvWorkflow.SelectedWorkflowId = csvId;
                }
                else if (string.IsNullOrWhiteSpace(CsvWorkflow.SelectedWorkflowId) &&
                         CsvWorkflow.SavedWorkflows.Count > 0)
                {
                    CsvWorkflow.SelectedWorkflowId = CsvWorkflow.SavedWorkflows[0].Id;
                }
            }
            finally
            {
                // Reattach deterministically (ensure single subscription)
                PdfWorkflow.PropertyChanged -= VmOnPropertyChanged;
                PdfWorkflow.PropertyChanged += VmOnPropertyChanged;
                ImageWorkflow.PropertyChanged -= VmOnPropertyChanged;
                ImageWorkflow.PropertyChanged += VmOnPropertyChanged;
                CsvWorkflow.PropertyChanged -= VmOnPropertyChanged;
                CsvWorkflow.PropertyChanged += VmOnPropertyChanged;
            }
        }

        private void PersistChanges(Action<bool>? onCompleted)
        {
            RequestCatalogSave(success =>
            {
                if (success)
                {
                    _isDirty = false;
                }
                onCompleted?.Invoke(success);
            });
        }

        public void SaveSettings(Action<bool>? onCompleted = null)
        {
            PersistChanges(onCompleted ?? (_ => { }));
        }

        private void RequestCatalogSave(Action<bool>? continuation)
        {
            try
            {
                if (!_isDirty)
                {
                    continuation?.Invoke(true);
                    return;
                }

                _settingsSaver.RequestSave(success =>
                {
                    if (success)
                    {
                        _isDirty = false;
                        try { _catalogNotifier.NotifyChanged(); } catch (Exception ex) { Trace.WriteLine(ex); }
                    }
                    continuation?.Invoke(success);
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
                continuation?.Invoke(false);
            }
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
