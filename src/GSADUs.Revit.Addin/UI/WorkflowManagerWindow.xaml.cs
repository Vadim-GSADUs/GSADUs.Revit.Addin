using Autodesk.Revit.DB;
using GSADUs.Revit.Addin.Workflows.Image;
using GSADUs.Revit.Addin.Workflows.Pdf;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace GSADUs.Revit.Addin.UI
{
    public partial class WorkflowManagerWindow : Window
    {
        // --- Singleton management ---
        private static WorkflowManagerWindow? _activeInstance;
        public static bool TryActivateExisting()
        {
            if (_activeInstance == null) return false;
            if (!_activeInstance.IsLoaded || !_activeInstance.IsVisible) return false;
            try { _activeInstance.Topmost = true; _activeInstance.Activate(); _activeInstance.Topmost = false; } catch { }
            return true;
        }
        private void RegisterInstance()
        {
            _activeInstance = this;
            try { this.Closed += (_, __) => { if (ReferenceEquals(_activeInstance, this)) _activeInstance = null; }; } catch { }
        }
        // --- end singleton management ---

        private readonly WorkflowCatalogService _catalog;
        private readonly WorkflowManagerPresenter _presenter;
        private AppSettings _settings;
        private readonly IDialogService _dialogs;
        private readonly Document? _doc;
        private static readonly string[] ScopeOptions = new[] { "Catalog Database", "CurrentSet", "Report Log" };
        private GridViewColumnHeader? _lastHeader;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;
        private bool _isDirtyPdf, _isDirtyImage;
        private List<int> _imageBlacklistIds = new();

        public WorkflowManagerWindow(AppSettings? settings = null) : this(null, settings) { }
        public WorkflowManagerWindow(Document? doc, AppSettings? settings = null)
        {
            System.Windows.Application.LoadComponent(this, new Uri("/GSADUs.Revit.Addin;component/UI/WorkflowManagerWindow.xaml", UriKind.Relative));

            RegisterInstance();

            _dialogs = ServiceBootstrap.Provider.GetService(typeof(IDialogService)) as IDialogService ?? new DialogService();
            _catalog = ServiceBootstrap.Provider.GetService(typeof(WorkflowCatalogService)) as WorkflowCatalogService
                       ?? new WorkflowCatalogService(new SettingsPersistence());
            _presenter = ServiceBootstrap.Provider.GetService(typeof(WorkflowManagerPresenter)) as WorkflowManagerPresenter
                         ?? new WorkflowManagerPresenter(_catalog, _dialogs);
            _settings = _catalog.Settings;
            _doc = doc;

            _presenter.OnWindowConstructed(this);

            // Set tab DataContexts to their respective VMs
            try { (FindName("PdfTabRoot") as FrameworkElement)!.DataContext = _presenter.PdfWorkflow; } catch { }
            try { (FindName("ImageTabRoot") as FrameworkElement)!.DataContext = _presenter.ImageWorkflow; } catch { }

            _imageBlacklistIds = new List<int>(_settings.ImageBlacklistCategoryIds ?? new List<int>());
            RefreshMainList();
            RefreshSavedCombos();
            InitScopeCombos();
            UpdateImageBlacklistSummary();

            this.Loaded += WorkflowManagerWindow_Loaded;
        }

        private void UpdateImageBlacklistSummary()
        {
            try
            {
                var tb = FindName("ImageBlacklistSummary") as TextBlock;
                if (tb == null) return;
                tb.Text = _imageBlacklistIds.Count == 0 ? "(all categories)" : string.Join(", ", _imageBlacklistIds.Select(id => CategoryNameOrEnum((BuiltInCategory)id)));
            }
            catch { }
        }

        private string CategoryNameOrEnum(BuiltInCategory bic)
        {
            try { if (_doc != null) { var c = Category.GetCategory(_doc, bic); if (c != null && !string.IsNullOrWhiteSpace(c.Name)) return c.Name; } } catch { }
            return bic.ToString().StartsWith("OST_") ? bic.ToString().Substring(4) : bic.ToString();
        }

        private void ImageBlacklistPickBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var initialScope = 2; // Selection Set scope attempt
                var doc = _doc ?? RevitUiContext.Current?.ActiveUIDocument?.Document;
                IEnumerable<int> preselected = _imageBlacklistIds;
                var dlg = new CategoriesPickerWindow(preselected, doc, initialScope: initialScope) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    _imageBlacklistIds = dlg.ResultIds.ToList();
                    _settings.ImageBlacklistCategoryIds = _imageBlacklistIds.Distinct().ToList();
                    try { _catalog.Save(); } catch { }
                    UpdateImageBlacklistSummary();
                }
            }
            catch { }
        }

        private void WorkflowManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var uiDoc = RevitUiContext.Current?.ActiveUIDocument;
                var doc = _doc ?? uiDoc?.Document;

                _presenter.OnLoaded(uiDoc, this);

                if (doc == null || doc.IsFamilyDocument)
                {
                    TryDisablePdfControls();
                    return;
                }

                var wf = GetSelectedFromTab("Pdf");

                // Populate sources via presenter (VM collections)
                try { _presenter.PopulateImageSources(doc); } catch { }
                try { _presenter.PopulatePdfSources(doc); } catch { }

                // Apply saved values if present (PDF)
                var p = wf?.Parameters ?? new Dictionary<string, JsonElement>();
                string Gs(string k) => p.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty) : string.Empty;
                var savedSet = Gs(PdfWorkflowKeys.PrintSetName);
                var savedSetup = Gs(PdfWorkflowKeys.ExportSetupName);
                var savedPattern = Gs(PdfWorkflowKeys.FileNamePattern);

                try
                {
                    var pdfVm = _presenter.PdfWorkflow;
                    pdfVm.SelectedSetName = savedSet;
                    pdfVm.SelectedPrintSet = savedSetup;
                    pdfVm.Pattern = savedPattern;
                }
                catch { }

                try
                {
                    var outLbl = FindName("PdfOutFolderLabel") as Label; if (outLbl != null) outLbl.Content = _settings.DefaultOutputDir ?? string.Empty;
                    var overLbl = FindName("PdfOverwriteLabel") as Label; if (overLbl != null) overLbl.Content = _settings.DefaultOverwrite ? "True" : "False";
                }
                catch { }
            }
            catch { }
            UpdateImageBlacklistSummary();
        }

        private void TryDisablePdfControls()
        {
            try
            {
                foreach (var n in new[] { "ViewSetCombo", "ExportSetupCombo", "FileNamePatternBox", "PdfSaveBtn" })
                {
                    if (FindName(n) is System.Windows.Controls.Control c) { c.IsEnabled = false; }
                }
                var outLbl = FindName("PdfOutFolderLabel") as Label; if (outLbl != null) outLbl.Content = "(no project open)";
                var overLbl = FindName("PdfOverwriteLabel") as Label; if (overLbl != null) overLbl.Content = string.Empty;
            }
            catch { }
        }

        private void SetSaveVisual(string btnName, bool hasUnsaved)
        {
            var btn = FindName(btnName) as Button;
            if (btn != null)
            {
                btn.Opacity = hasUnsaved && btn.IsEnabled ? 1.0 : 0.5;
                btn.Content = hasUnsaved ? "Save Workflow" : "Saved";
            }
        }

        private void MarkDirty(string tag, bool dirty = true)
        {
            switch (tag)
            {
                case "Pdf": _isDirtyPdf = dirty; _presenter.PdfWorkflow?.SetDirty(dirty); break;
                case "Image": _isDirtyImage = dirty; _presenter.ImageWorkflow?.SetDirty(dirty); SetSaveVisual("ImageSaveBtn", _isDirtyImage); break;
            }
        }

        private void UpdateCanSaveFor(string tag)
        {
            try
            {
                if (string.Equals(tag, "Pdf", StringComparison.OrdinalIgnoreCase)) return;
                var name = (FindName(tag + "NameBox") as TextBox)?.Text?.Trim();
                var scope = (FindName(tag + "ScopeCombo") as ComboBox)?.SelectedItem as string;
                bool can = !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(scope);
                var btn = FindName(tag + "SaveBtn") as Button; if (btn != null) btn.IsEnabled = can;
                SetSaveVisual(tag + "SaveBtn", tag switch { "Image" => _isDirtyImage, _ => false });
            }
            catch { }
        }

        private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string tag = ((FrameworkElement)sender).Name.Contains("Image") ? "Image" : "Pdf";
            _presenter.OnMarkDirty(tag);
            MarkDirty(tag);
            UpdateCanSaveFor(tag);
        }

        private void PdfNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _presenter.OnMarkDirty("Pdf");
            MarkDirty("Pdf");
        }

        private void PdfConfigChanged(object sender, RoutedEventArgs e)
        {
            _presenter.OnMarkDirty("Pdf");
            MarkDirty("Pdf");
        }

        private void RefreshSavedCombosAndMain()
        {
            try { _catalog.SaveAndRefresh(); } catch { }
            RefreshMainList();
            RefreshSavedCombos();
        }

        private void RefreshMainList()
        {
            try
            {
                var rows = (_catalog.Workflows ?? new System.Collections.ObjectModel.ObservableCollection<WorkflowDefinition>())
                    .Select(w => new { w.Id, w.Name, w.Output, w.Scope, w.Description })
                    .ToList();
                (FindName("WorkflowsList") as ListView)!.ItemsSource = rows;
            }
            catch { }
        }

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not GridViewColumnHeader header || header.Column == null) return;
            var lv = FindName("WorkflowsList") as ListView;
            if (lv == null) return;
            var dir = (_lastHeader == header && _lastDirection == ListSortDirection.Ascending) ? ListSortDirection.Descending : ListSortDirection.Ascending;
            _lastHeader = header; _lastDirection = dir;
            var sortBy = header.Column.Header as string ?? "Name";
            var view = (ListCollectionView)CollectionViewSource.GetDefaultView(lv.ItemsSource);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(sortBy, dir));
            view.Refresh();
        }

        private void InitScopeCombos()
        {
            try
            {
                foreach (var name in new[] { "PdfScopeCombo", "ImageScopeCombo" })
                {
                    var cb = FindName(name) as ComboBox;
                    if (cb != null) cb.ItemsSource = ScopeOptions;
                }
            }
            catch { }
        }

        private WorkflowDefinition? GetSelectedFromTab(string kind)
        {
            string comboName = kind switch { "Pdf" => "PdfSavedCombo", "Image" => "ImageSavedCombo", _ => string.Empty };
            var cb = FindName(comboName) as ComboBox;
            var sel = cb?.SelectedItem;
            if (sel == null) return null;
            var idProp = sel.GetType().GetProperty("Id");
            var id = idProp?.GetValue(sel) as string;
            return (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>()).FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private void RefreshSavedCombos()
        {
            try
            {
                var all = _catalog.Workflows?.ToList() ?? new List<WorkflowDefinition>();
                var mk = static (WorkflowDefinition w) => new { Id = w.Id, Display = $"{w.Name} - {w.Scope} - {w.Description}" };
                (FindName("PdfSavedCombo") as ComboBox)!.ItemsSource = all.Where(w => w.Output == OutputType.Pdf).Select(mk).ToList();
                (FindName("ImageSavedCombo") as ComboBox)!.ItemsSource = all.Where(w => w.Output == OutputType.Image).Select(mk).ToList();
            }
            catch { }
        }

        // ----- Main tab actions -----
        private void DupBtn_Click(object sender, RoutedEventArgs e)
        {
            var lv = FindName("WorkflowsList") as ListView;
            var sel = lv?.SelectedItem; if (sel == null) { _dialogs.Info("Duplicate", "Select a workflow."); return; }
            var id = sel.GetType().GetProperty("Id")?.GetValue(sel) as string; if (id == null) return;
            _ = _catalog.Duplicate(id);
            RefreshSavedCombosAndMain();
        }

        private void RenBtn_Click(object sender, RoutedEventArgs e)
        {
            var lv = FindName("WorkflowsList") as ListView;
            var sel = lv?.SelectedItem; if (sel == null) return;
            var id = sel.GetType().GetProperty("Id")?.GetValue(sel) as string;
            if (id == null) return;
            var wf = (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>()).FirstOrDefault(w => w.Id == id);
            if (wf == null) return;
            var input = Microsoft.VisualBasic.Interaction.InputBox("New name:", "Rename Workflow", wf.Name);
            if (!string.IsNullOrWhiteSpace(input)) { _catalog.Rename(id, input.Trim()); RefreshSavedCombosAndMain(); }
        }

        private void DelBtn_Click(object sender, RoutedEventArgs e)
        {
            var lv = FindName("WorkflowsList") as ListView;
            var sel = lv?.SelectedItem; if (sel == null) return;
            var id = sel.GetType().GetProperty("Id")?.GetValue(sel) as string; if (id == null) return;
            var wf = (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>()).FirstOrDefault(w => w.Id == id);
            if (wf == null) return;
            if (_dialogs.ConfirmYesNo("Delete Workflow", $"Delete '{wf.Name}'?", "This cannot be undone.", defaultYes: false))
            {
                _catalog.Delete(id);
                RefreshSavedCombosAndMain();
            }
        }

        private void AuditBtn_Click(object sender, RoutedEventArgs e)
        {
            var lv = FindName("WorkflowsList") as ListView;
            var sel = lv?.SelectedItem; if (sel == null) { _dialogs.Info("Audit", "Select a workflow."); return; }
            _dialogs.Info("Audit", "Audit will validate workflows in the new flow.");
        }

        private void SaveCloseBtn_Click(object sender, RoutedEventArgs e)
        {
            try { _presenter.SaveSettings(); } catch { }
            DialogResult = true;
        }

        // ----- Common field change handlers -----
        private void ScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var tag = (sender as FrameworkElement)?.Name?.StartsWith("Pdf") == true ? "Pdf" : "Image";
                var wf = GetSelectedFromTab(tag); if (wf == null) return;
                var cb = sender as ComboBox; if (cb == null) return;
                var val = cb.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(val)) wf.Scope = val;
                _presenter.OnMarkDirty(tag);
                MarkDirty(tag);
            }
            catch { }
        }

        private void DescBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var fe = sender as FrameworkElement;
                var tag = fe?.Name?.StartsWith("Pdf") == true ? "Pdf" : "Image";

                WorkflowTabBaseViewModel? baseVm = tag == "Pdf" ? _presenter.PdfWorkflow : _presenter.ImageWorkflow;
                if (baseVm != null)
                {
                    baseVm.Description = (sender as TextBox)?.Text ?? string.Empty;
                }

                _presenter.OnMarkDirty(tag);
                MarkDirty(tag);
            }
            catch { }
        }

        private void SavedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox; if (combo == null) return;
            string tag = combo.Name.StartsWith("Pdf") ? "Pdf" : "Image";
            var wf = GetSelectedFromTab(tag);
            _presenter.OnSavedComboChanged(tag, wf, this);
            try
            {
                WorkflowTabBaseViewModel? baseVm = tag == "Pdf" ? _presenter.PdfWorkflow : _presenter.ImageWorkflow;
                if (baseVm != null)
                {
                    baseVm.Name = wf?.Name ?? string.Empty;
                    baseVm.WorkflowScope = wf?.Scope ?? string.Empty;
                    baseVm.Description = wf?.Description ?? string.Empty;
                }

                if (tag == "Pdf")
                {
                    var pdfVm = _presenter.PdfWorkflow;
                    var p = wf?.Parameters;
                    string Gs(string k) =>
                        (p != null && p.TryGetValue(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                        ? (v.GetString() ?? string.Empty) : string.Empty;

                    pdfVm.SelectedSetName  = Gs(PdfWorkflowKeys.PrintSetName);
                    pdfVm.SelectedPrintSet = Gs(PdfWorkflowKeys.ExportSetupName);
                    pdfVm.Pattern          = Gs(PdfWorkflowKeys.FileNamePattern);

                    _isDirtyPdf = false;
                    _presenter.PdfWorkflow?.SetDirty(false);
                    SetSaveVisual("PdfSaveBtn", false);
                }

                if (tag == "Image")
                {
                    var vm = _presenter.ImageWorkflow;
                    var p = wf?.Parameters;

                    string Gs(string k) =>
                        (p != null && p.TryGetValue(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                            ? (v.GetString() ?? string.Empty)
                            : string.Empty;

                    vm.ExportScope          = Gs(ImageWorkflowKeys.exportScope);
                    vm.SelectedPrintSet     = Gs(ImageWorkflowKeys.imagePrintSetName);
                    vm.SelectedSingleViewId = Gs(ImageWorkflowKeys.singleViewId);
                    vm.Pattern              = Gs(ImageWorkflowKeys.fileNamePattern);
                    vm.Prefix               = Gs(ImageWorkflowKeys.prefix);
                    vm.Suffix               = Gs(ImageWorkflowKeys.suffix);
                    vm.Format               = Gs(ImageWorkflowKeys.imageFormat);
                    vm.Resolution           = Gs(ImageWorkflowKeys.resolutionPreset);
                    vm.CropMode             = Gs(ImageWorkflowKeys.cropMode);
                    vm.CropOffset           = Gs(ImageWorkflowKeys.cropOffset);
                }
            }
            catch { }
            UpdateCanSaveFor(tag);
        }

        // ===== Event handlers wired in XAML =====
        private void WorkflowsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var lv = sender as ListView; if (lv?.SelectedItem == null) return;
                var id = lv.SelectedItem.GetType().GetProperty("Id")?.GetValue(lv.SelectedItem) as string;
                var wf = (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>()).FirstOrDefault(w => w.Id == id);
                if (wf == null) return;
                var tabIndex = wf.Output switch { OutputType.Pdf => 1, OutputType.Image => 2, _ => 0 };
                (Tabs as TabControl)!.SelectedIndex = tabIndex;
                string comboName = wf.Output switch { OutputType.Pdf => "PdfSavedCombo", OutputType.Image => "ImageSavedCombo", _ => string.Empty };
                var items = (FindName(comboName) as ComboBox)?.ItemsSource as System.Collections.IEnumerable;
                if (items != null)
                {
                    foreach (var it in items)
                    {
                        var itId = it.GetType().GetProperty("Id")?.GetValue(it) as string;
                        if (string.Equals(itId, wf.Id, StringComparison.OrdinalIgnoreCase)) { (FindName(comboName) as ComboBox)!.SelectedItem = it; break; }
                    }
                }
            }
            catch { }
        }

        private void PdfNewBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var wf = new WorkflowDefinition
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "PDF Workflow",
                    Output = OutputType.Pdf,
                    Kind = WorkflowKind.Internal,
                    Scope = "CurrentSet",
                    Description = string.Empty,
                    ActionIds = new List<string> { "export-pdf" },
                    Parameters = new Dictionary<string, JsonElement>()
                };
                _catalog.Settings.Workflows ??= new List<WorkflowDefinition>();
                _catalog.Settings.Workflows.Add(wf);
                RefreshSavedCombosAndMain();
                Tabs.SelectedIndex = 1;
                var pdfCombo = FindName("PdfSavedCombo") as ComboBox;
                if (pdfCombo?.ItemsSource is System.Collections.IEnumerable items)
                {
                    foreach (var it in items)
                    {
                        var itId = it.GetType().GetProperty("Id")?.GetValue(it) as string;
                        if (string.Equals(itId, wf.Id, StringComparison.OrdinalIgnoreCase)) { pdfCombo.SelectedItem = it; break; }
                    }
                }
            }
            catch { }
        }

        private void ImageNewBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var wf = new WorkflowDefinition
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "Image Workflow",
                    Output = OutputType.Image,
                    Kind = WorkflowKind.Internal,
                    Scope = "CurrentSet",
                    Description = string.Empty,
                    ActionIds = new List<string> { "export-image" },
                    Parameters = new Dictionary<string, JsonElement>()
                };
                _catalog.Settings.Workflows ??= new List<WorkflowDefinition>();
                _catalog.Settings.Workflows.Add(wf);
                RefreshSavedCombosAndMain();
                Tabs.SelectedIndex = 2;
                SelectInCombo("ImageSavedCombo", wf.Id);
            }
            catch { }
        }

        private void SelectInCombo(string comboName, string id)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id)) return;
                var combo = FindName(comboName) as ComboBox;
                if (combo?.ItemsSource is System.Collections.IEnumerable items)
                {
                    foreach (var it in items)
                    {
                        var itId = it.GetType().GetProperty("Id")?.GetValue(it) as string;
                        if (string.Equals(itId, id, StringComparison.OrdinalIgnoreCase)) { combo.SelectedItem = it; break; }
                    }
                }
            }
            catch { }
        }

        private static void EnsureActionId(WorkflowDefinition wf, string id)
        {
            wf.ActionIds ??= new List<string>();
            if (!wf.ActionIds.Any(a => string.Equals(a, id, StringComparison.OrdinalIgnoreCase))) wf.ActionIds.Add(id);
        }

        private void ImageResolutionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Legacy secondary resolution control removed; keep empty handler to satisfy XAML reference.
        }

        private void SaveWorkflow_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement; if (btn == null) return;
            var tag = (btn.Tag as string) ?? string.Empty;
            if (tag == "Png") tag = "Image";

            WorkflowTabBaseViewModel? baseVm = tag == "Pdf" ? _presenter.PdfWorkflow : _presenter.ImageWorkflow;

            string nameVal = baseVm?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(nameVal)) nameVal = (FindName(tag + "NameBox") as TextBox)?.Text?.Trim() ?? string.Empty;

            string scopeVal = baseVm?.WorkflowScope ?? string.Empty;
            if (string.IsNullOrWhiteSpace(scopeVal)) scopeVal = (FindName(tag + "ScopeCombo") as ComboBox)?.SelectedItem as string ?? string.Empty;

            string descVal = baseVm?.Description ?? string.Empty;
            if (string.IsNullOrWhiteSpace(descVal)) descVal = (FindName(tag + "DescBox") as TextBox)?.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(nameVal) || string.IsNullOrWhiteSpace(scopeVal)) { _dialogs.Info("Save", "Name and Scope required."); return; }

            var output = tag switch { "Pdf" => OutputType.Pdf, _ => OutputType.Image };

            var existing = GetSelectedFromTab(tag);
            if (existing == null)
            {
                existing = new WorkflowDefinition { Id = Guid.NewGuid().ToString("N"), Kind = WorkflowKind.Internal, Output = output, ActionIds = new List<string>(), Parameters = new Dictionary<string, JsonElement>() };
                _catalog.Settings.Workflows ??= new List<WorkflowDefinition>();
                _catalog.Settings.Workflows.Add(existing);
            }

            existing.Name = nameVal; existing.Scope = scopeVal; existing.Description = descVal;

            switch (output)
            {
                case OutputType.Pdf:
                {
                    if (!_presenter.SavePdfWorkflow(existing)) return;
                    break;
                }
                case OutputType.Image:
                {
                    _presenter.SaveImageWorkflow(existing);
                    break;
                }
            }

            _catalog.SaveAndRefresh();
            RefreshMainList();
            RefreshSavedCombos();

            try
            {
                SelectInCombo(tag + "SavedCombo", existing.Id);
            }
            catch { }

            MarkDirty(tag, false);
            UpdateCanSaveFor(tag);
        }

        private void PdfManageSetupBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var uiapp = RevitUiContext.Current; if (uiapp == null) { _dialogs.Info("PDF", "Revit UI not available."); return; }
                var cmd = Autodesk.Revit.UI.RevitCommandId.LookupPostableCommandId(Autodesk.Revit.UI.PostableCommand.ExportPDF);
                if (cmd != null && uiapp.CanPostCommand(cmd)) uiapp.PostCommand(cmd);
            }
            catch { }
        }
    }
}
