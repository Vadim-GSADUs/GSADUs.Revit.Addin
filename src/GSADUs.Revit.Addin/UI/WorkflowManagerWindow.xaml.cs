using Autodesk.Revit.DB;
using GSADUs.Revit.Addin.Workflows.Image;
using GSADUs.Revit.Addin.Workflows.Pdf;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
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
        private bool _isDirtyRvt, _isDirtyPdf, _isDirtyImage, _isDirtyCsv;
        private List<int> _imageBlacklistIds = new();
        private bool _hydratingImage; // Guard to suppress dirty state & event side-effects during Image tab hydration

        // Helpers to build JsonElement safely from primitive values
        private static JsonElement ToJson(string value)
        {
            var json = "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        private static JsonElement ToJson(bool value)
        {
            using var doc = JsonDocument.Parse(value ? "true" : "false");
            return doc.RootElement.Clone();
        }

        // NEW: basic filename component sanitation (keeps token braces and dots). Does NOT add extensions.
        private static string SanitizeFileComponent(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var invalid = Path.GetInvalidFileNameChars();
            var s = raw.Trim();
            foreach (var c in invalid) s = s.Replace(c, '_');
            // collapse accidental path pieces (just in case user pasted something with separators)
            s = s.Replace('/', '_').Replace('\\', '_');
            return s;
        }

        // NEW (Image tab scope): map selected format text to extension
        private static string MapFormatToExt(string? fmt)
        {
            return (fmt ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "BMP" => ".bmp",
                "TIFF" => ".tiff",
                _ => ".png"
            };
        }

        // --- Image Export Range helpers (added, not yet wired) ---
        private string GetImageExportScope()
        {
            try { return (ImageScopeSingleViewRadio?.IsChecked == true) ? "SingleView" : "PrintSet"; } catch { return "PrintSet"; }
        }

        private void ApplyImageScopeUiState()
        {
            try
            {
                var scope = GetImageExportScope();
                var singleViewAvailable = (ImageSingleViewCombo?.Items?.Count ?? 0) > 0;

                if (ImagePrintSetCombo != null)
                    ImagePrintSetCombo.IsEnabled = scope == "PrintSet";

                if (ImageSingleViewCombo != null)
                {
                    ImageSingleViewCombo.IsEnabled = scope == "SingleView" && singleViewAvailable;
                    if (!singleViewAvailable)
                        ImageSingleViewCombo.ToolTip = "No eligible 2D views found.";
                }
            }
            catch { }
        }

        private void PopulateImageSingleViewList()
        {
            if (_doc == null || ImageSingleViewCombo == null || ImageScopeSingleViewRadio == null) return;

            try
            {
                ImageSingleViewCombo.Items.Clear();

                var views = new FilteredElementCollector(_doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate
                        && v.ViewType != ViewType.ThreeD
                        && v.ViewType != ViewType.DrawingSheet
                        && (v is ViewPlan || v is ViewDrafting || v is ViewSection || v.ViewType == ViewType.AreaPlan))
                    .OrderBy(v => v.ViewType.ToString())
                    .ThenBy(v => v.Name)
                    .ToList();

                foreach (var v in views)
                {
                    var item = new ComboBoxItem
                    {
                        Content = $"{v.ViewType} - {v.Name}",
                        Tag = v.Id.ToString()
                    };
                    ImageSingleViewCombo.Items.Add(item);
                }

                ImageScopeSingleViewRadio.IsEnabled = views.Count > 0;
                if (!ImageScopeSingleViewRadio.IsEnabled && ImageScopeSingleViewRadio.IsChecked == true && ImageScopePrintSetRadio != null)
                    ImageScopePrintSetRadio.IsChecked = true;
            }
            catch { }
        }

        private void ImageScopeRadio_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_hydratingImage) return;
                ApplyImageScopeUiState();
                // Clear the text/selection of the inactive picker to avoid ambiguity
                if (ImageScopeSingleViewRadio?.IsChecked == true)
                {
                    if (ImagePrintSetCombo != null)
                    {
                        ImagePrintSetCombo.SelectedIndex = -1;
                        if (ImagePrintSetCombo.IsEditable) ImagePrintSetCombo.Text = string.Empty;
                    }
                }
                else // PrintSet
                {
                    if (ImageSingleViewCombo != null)
                    {
                        ImageSingleViewCombo.SelectedIndex = -1;
                        ImageSingleViewCombo.Text = string.Empty;
                    }
                }

                // Safe mark-dirty signaling
                try { _presenter?.OnMarkDirty("Image"); } catch { }
                MarkDirty("Image");
                UpdateImageSaveState();
            }
            catch { }
        }

        private void ImageSingleViewCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_hydratingImage) return;
                try { _presenter?.OnMarkDirty("Image"); } catch { }
                MarkDirty("Image");
                UpdateImageSaveState();
            }
            catch { }
        }

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
            _settings = _catalog.Settings; // central settings source
            _doc = doc;

            _presenter.OnWindowConstructed(this);

            // NEW: set tab DataContexts to their respective VMs
            try { (FindName("PdfTabRoot") as FrameworkElement)!.DataContext = _presenter.PdfWorkflow; } catch { }
            try { (FindName("ImageTabRoot") as FrameworkElement)!.DataContext = _presenter.ImageWorkflow; } catch { }
            try { (FindName("RvtTabRoot") as FrameworkElement)!.DataContext = _presenter.RvtBase; } catch { }
            try { (FindName("CsvTabRoot") as FrameworkElement)!.DataContext = _presenter.CsvBase; } catch { }

            _imageBlacklistIds = new List<int>(_settings.ImageBlacklistCategoryIds ?? new List<int>());
            RefreshMainList();
            RefreshSavedCombos();
            InitScopeCombos();
            UpdateImageBlacklistSummary();

            // Phase 2: minimal load hook
            this.Loaded += WorkflowManagerWindow_Loaded;
            UpdatePdfEnableState();
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
                // Determine active document (prefer ctor doc, else UI context)
                var uiDoc = RevitUiContext.Current?.ActiveUIDocument;
                var doc = _doc ?? uiDoc?.Document;

                _presenter.OnLoaded(uiDoc, this);

                // Guard: disable PDF tab if no valid project document
                if (doc == null || doc.IsFamilyDocument)
                {
                    TryDisablePdfControls();
                    return;
                }

                // Populate PDF controls deterministically
                var wf = GetSelectedFromTab("Pdf");

                // Populate Image print sets (same source as PDF view/sheet sets)
                try
                {
                    var setNamesForImage = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheetSet))
                        .Cast<ViewSheetSet>()
                        .Select(s => s.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n)
                        .ToList();
                    var imgPrintSetCombo = FindName("ImagePrintSetCombo") as ComboBox;
                    if (imgPrintSetCombo != null)
                    {
                        imgPrintSetCombo.ItemsSource = setNamesForImage;
                        // hook selection changed to persist & enable saving
                        imgPrintSetCombo.SelectionChanged -= ImagePrintSetCombo_SelectionChanged;
                        imgPrintSetCombo.SelectionChanged += ImagePrintSetCombo_SelectionChanged;
                    }
                }
                catch { }

                // Wire scope events (idempotent)
                if (ImageScopePrintSetRadio != null) ImageScopePrintSetRadio.Checked -= ImageScopeRadio_Checked;
                if (ImageScopeSingleViewRadio != null) ImageScopeSingleViewRadio.Checked -= ImageScopeRadio_Checked;
                if (ImageSingleViewCombo != null) ImageSingleViewCombo.SelectionChanged -= ImageSingleViewCombo_SelectionChanged;
                if (ImageScopePrintSetRadio != null) ImageScopePrintSetRadio.Checked += ImageScopeRadio_Checked;
                if (ImageScopeSingleViewRadio != null) ImageScopeSingleViewRadio.Checked += ImageScopeRadio_Checked;
                if (ImageSingleViewCombo != null) ImageSingleViewCombo.SelectionChanged += ImageSingleViewCombo_SelectionChanged;

                // Initial state
                PopulateImageSingleViewList();
                ApplyImageScopeUiState();

                // Also hydrate Image simple fields if an Image workflow is selected (viewKind + pattern)
                try
                {
                    var imageWf = GetSelectedFromTab("Image");
                    if (imageWf != null && imageWf.Output == OutputType.Image)
                    {
                        HydrateImageWorkflow(imageWf);
                    }
                }
                catch { }

                // View/Sheet Sets (PDF)
                var setNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheetSet))
                    .Cast<ViewSheetSet>()
                    .Select(s => s.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();
                var viewSetCombo = FindName("ViewSetCombo") as ComboBox;
                if (viewSetCombo != null)
                {
                    viewSetCombo.ItemsSource = setNames;
                    viewSetCombo.SelectionChanged -= PdfSelectionChanged;
                    viewSetCombo.SelectionChanged += PdfSelectionChanged;
                }

                // Export PDF Setups
                var setupNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(ExportPDFSettings))
                    .Cast<ExportPDFSettings>()
                    .Select(s => s.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();
                var setupCombo = FindName("ExportSetupCombo") as ComboBox;
                if (setupCombo != null)
                {
                    setupCombo.ItemsSource = setupNames;
                    setupCombo.SelectionChanged -= PdfSelectionChanged;
                    setupCombo.SelectionChanged += PdfSelectionChanged;
                }

                // Apply saved values if present
                var p = wf?.Parameters ?? new Dictionary<string, JsonElement>();
                string Gs(string k) => p.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty) : string.Empty;
                var savedSet = Gs(PdfWorkflowKeys.PrintSetName);
                var savedSetup = Gs(PdfWorkflowKeys.ExportSetupName);
                var savedPattern = Gs(PdfWorkflowKeys.FileNamePattern);

                var viewSetComboLocal = viewSetCombo; // avoid closure confusion
                if (viewSetComboLocal != null && !string.IsNullOrWhiteSpace(savedSet) && setNames.Contains(savedSet, StringComparer.OrdinalIgnoreCase))
                    viewSetComboLocal.SelectedItem = setNames.First(n => string.Equals(n, savedSet, StringComparison.OrdinalIgnoreCase));

                if (setupCombo != null && !string.IsNullOrWhiteSpace(savedSetup) && setupNames.Contains(savedSetup, StringComparer.OrdinalIgnoreCase))
                    setupCombo.SelectedItem = setupNames.First(n => string.Equals(n, savedSetup, StringComparison.OrdinalIgnoreCase));

                var patBox = FindName("FileNamePatternBox") as TextBox;
                if (patBox != null)
                {
                    patBox.Text = string.IsNullOrWhiteSpace(savedPattern) ? "{SetName}.pdf" : savedPattern;
                    patBox.TextChanged -= PdfPatternChanged;
                    patBox.TextChanged += PdfPatternChanged;
                }

                // Read-only labels from raw settings (no fallback logic per spec)
                try
                {
                    var outLbl = FindName("PdfOutFolderLabel") as Label; if (outLbl != null) outLbl.Content = _settings.DefaultOutputDir ?? string.Empty;
                    var overLbl = FindName("PdfOverwriteLabel") as Label; if (overLbl != null) overLbl.Content = _settings.DefaultOverwrite ? "True" : "False"; // show boolean directly
                }
                catch { }

                UpdatePdfEnableState();
            }
            catch { }
            UpdateImageSaveState();
            UpdateCropOffsetEnable();
            UpdateImageBlacklistSummary();
        }

        private void UpdatePdfEnableState()
        {
            try
            {
                var btn = FindName("PdfSaveBtn") as Button;
                var setSel = (FindName("ViewSetCombo") as ComboBox)?.SelectedItem as string;
                var setupSel = (FindName("ExportSetupCombo") as ComboBox)?.SelectedItem as string;
                var pattern = (FindName("FileNamePatternBox") as TextBox)?.Text ?? string.Empty;
                bool ok = !string.IsNullOrWhiteSpace(setSel) && !string.IsNullOrWhiteSpace(setupSel) && pattern.Contains("{SetName}");
                if (btn != null)
                {
                    btn.IsEnabled = ok;
                    btn.Opacity = _isDirtyPdf && ok ? 1.0 : 0.5;
                }
            }
            catch { }
        }

        private void PdfSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _presenter.OnMarkDirty("Pdf");
            MarkDirty("Pdf");
            UpdatePdfEnableState();
        }

        private void PdfPatternChanged(object sender, TextChangedEventArgs e)
        {
            _presenter.OnMarkDirty("Pdf");
            MarkDirty("Pdf");
            UpdatePdfEnableState();
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
                case "Rvt": _isDirtyRvt = dirty; SetSaveVisual("RvtSaveBtn", _isDirtyRvt); break;
                case "Pdf": _isDirtyPdf = dirty; SetSaveVisual("PdfSaveBtn", _isDirtyPdf); break;
                case "Image": _isDirtyImage = dirty; SetSaveVisual("ImageSaveBtn", _isDirtyImage); break;
                case "Csv": _isDirtyCsv = dirty; SetSaveVisual("CsvSaveBtn", _isDirtyCsv); break;
            }
        }

        private void UpdateCanSaveFor(string tag)
        {
            try
            {
                if (string.Equals(tag, "Pdf", StringComparison.OrdinalIgnoreCase))
                {
                    UpdatePdfEnableState();
                    SetSaveVisual("PdfSaveBtn", _isDirtyPdf);
                    return;
                }
                var name = (FindName(tag + "NameBox") as TextBox)?.Text?.Trim();
                var scope = (FindName(tag + "ScopeCombo") as ComboBox)?.SelectedItem as string;
                bool can = !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(scope);
                var btn = FindName(tag + "SaveBtn") as Button; if (btn != null) btn.IsEnabled = can;
                SetSaveVisual(tag + "SaveBtn", tag switch { "Rvt" => _isDirtyRvt, "Image" => _isDirtyImage, _ => _isDirtyCsv });
            }
            catch { }
        }

        private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string tag = ((FrameworkElement)sender).Name.Contains("Rvt") ? "Rvt" : ((FrameworkElement)sender).Name.Contains("Image") ? "Image" : "Csv";
            _presenter.OnMarkDirty(tag);
            MarkDirty(tag);
            UpdateCanSaveFor(tag);
            UpdateImageSaveState();
        }

        private void PdfNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _presenter.OnMarkDirty("Pdf");
            MarkDirty("Pdf");
            UpdatePdfEnableState();
        }

        private void PdfConfigChanged(object sender, RoutedEventArgs e)
        {
            _presenter.OnMarkDirty("Pdf");
            MarkDirty("Pdf");
            UpdatePdfEnableState();
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
                foreach (var name in new[] { "RvtScopeCombo", "PdfScopeCombo", "ImageScopeCombo", "CsvScopeCombo" })
                {
                    var cb = FindName(name) as ComboBox;
                    if (cb != null) cb.ItemsSource = ScopeOptions;
                }
            }
            catch { }
        }

        private WorkflowDefinition? GetSelectedFromTab(string kind)
        {
            string comboName = kind switch { "Rvt" => "RvtSavedCombo", "Pdf" => "PdfSavedCombo", "Image" => "ImageSavedCombo", _ => "CsvSavedCombo" };
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
                (FindName("RvtSavedCombo") as ComboBox)!.ItemsSource = all.Where(w => w.Output == OutputType.Rvt).Select(mk).ToList();
                (FindName("PdfSavedCombo") as ComboBox)!.ItemsSource = all.Where(w => w.Output == OutputType.Pdf).Select(mk).ToList();
                (FindName("ImageSavedCombo") as ComboBox)!.ItemsSource = all.Where(w => w.Output == OutputType.Image).Select(mk).ToList();
                (FindName("CsvSavedCombo") as ComboBox)!.ItemsSource = all.Where(w => w.Output == OutputType.Csv).Select(mk).ToList();
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
            // Legacy PDF API surface audit removed in clean slate. Validation will be handled in runner/audit later.
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
                var tag = (sender as FrameworkElement)?.Name?.StartsWith("Rvt") == true ? "Rvt"
                          : (sender as FrameworkElement)?.Name?.StartsWith("Pdf") == true ? "Pdf"
                          : (sender as FrameworkElement)?.Name?.StartsWith("Image") == true ? "Image" : "Csv";
                var wf = GetSelectedFromTab(tag); if (wf == null) return;
                var cb = sender as ComboBox; if (cb == null) return;
                var val = cb.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(val)) wf.Scope = val;
                _presenter.OnMarkDirty(tag);
                MarkDirty(tag);
                if (tag == "Pdf") UpdatePdfEnableState();
            }
            catch { }
            UpdateImageSaveState();
        }

        private void DescBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var fe = sender as FrameworkElement;
                var tag = fe?.Name?.StartsWith("Rvt") == true ? "Rvt"
                          : fe?.Name?.StartsWith("Pdf") == true ? "Pdf"
                          : fe?.Name?.StartsWith("Image") == true ? "Image" : "Csv";

                // Update VM only; defer model persistence to Save
                WorkflowTabBaseViewModel? baseVm = tag switch
                {
                    "Pdf" => _presenter.PdfWorkflow,
                    "Image" => _presenter.ImageWorkflow,
                    "Rvt" => _presenter.RvtBase,
                    _ => _presenter.CsvBase
                };
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
            string tag = combo.Name.StartsWith("Rvt") ? "Rvt" : combo.Name.StartsWith("Pdf") ? "Pdf" : combo.Name.StartsWith("Image") ? "Image" : "Csv";
            var wf = GetSelectedFromTab(tag);
            _presenter.OnSavedComboChanged(tag, wf, this);
            try
            {
                // Hydrate the tab's base VM first (Name, Scope, Description)
                WorkflowTabBaseViewModel? baseVm = tag switch
                {
                    "Pdf" => _presenter.PdfWorkflow,
                    "Image" => _presenter.ImageWorkflow,
                    "Rvt" => _presenter.RvtBase,
                    _ => _presenter.CsvBase
                };
                if (baseVm != null)
                {
                    baseVm.Name = wf?.Name ?? string.Empty;
                    baseVm.WorkflowScope = wf?.Scope ?? string.Empty;
                    baseVm.Description = wf?.Description ?? string.Empty;
                }

                // Keep legacy UI sync to avoid regressions during migration
                (FindName(tag + "ScopeCombo") as ComboBox)!.SelectedItem = wf?.Scope;
                (FindName(tag + "DescBox") as TextBox)!.Text = wf?.Description ?? string.Empty;
                (FindName(tag + "NameBox") as TextBox)!.Text = wf?.Name ?? string.Empty;

                if (tag == "Pdf") WorkflowManagerWindow_Loaded(this, new RoutedEventArgs());
                if (tag == "Image") HydrateImageWorkflow(wf);
                if (tag == "Rvt") { _isDirtyRvt = false; SetSaveVisual("RvtSaveBtn", false); }
                if (tag == "Image") { _isDirtyImage = false; SetSaveVisual("ImageSaveBtn", false); }
                if (tag == "Csv") { _isDirtyCsv = false; SetSaveVisual("CsvSaveBtn", false); }
            }
            catch { }
            UpdateCanSaveFor(tag);
            UpdateImageSaveState();
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
                var tabIndex = wf.Output switch { OutputType.Rvt => 1, OutputType.Pdf => 2, OutputType.Image => 3, OutputType.Csv => 4, _ => 0 };
                (Tabs as TabControl)!.SelectedIndex = tabIndex;
                string comboName = wf.Output switch { OutputType.Rvt => "RvtSavedCombo", OutputType.Pdf => "PdfSavedCombo", OutputType.Image => "ImageSavedCombo", _ => "CsvSavedCombo" };
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
                Tabs.SelectedIndex = 2;
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

        private void RvtNewBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var wf = new WorkflowDefinition
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "RVT Workflow",
                    Output = OutputType.Rvt,
                    Kind = WorkflowKind.External,
                    Scope = "Model",
                    Description = string.Empty,
                    ActionIds = new List<string> { "export-rvt", "cleanup" },
                    Parameters = new Dictionary<string, System.Text.Json.JsonElement>()
                };
                _catalog.Settings.Workflows ??= new List<WorkflowDefinition>();
                _catalog.Settings.Workflows.Add(wf);
                RefreshSavedCombosAndMain();
                Tabs.SelectedIndex = 1;
                SelectInCombo("RvtSavedCombo", wf.Id);
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
                Tabs.SelectedIndex = 3;
                SelectInCombo("ImageSavedCombo", wf.Id);
            }
            catch { }
        }

        private void CsvNewBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var wf = new WorkflowDefinition
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "CSV Workflow",
                    Output = OutputType.Csv,
                    Kind = WorkflowKind.Internal,
                    Scope = "CurrentSet",
                    Description = string.Empty,
                    ActionIds = new List<string> { "export-csv" },
                    Parameters = new Dictionary<string, JsonElement>()
                };
                _catalog.Settings.Workflows ??= new List<WorkflowDefinition>();
                _catalog.Settings.Workflows.Add(wf);
                RefreshSavedCombosAndMain();
                Tabs.SelectedIndex = 4;
                SelectInCombo("CsvSavedCombo", wf.Id);
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

        private void HydrateImageWorkflow(WorkflowDefinition? wf)
        {
            _hydratingImage = true; // begin guard
            try
            {
                // print set
                string storedPrintSet = string.Empty;
                if (wf?.Parameters != null && wf.Parameters.TryGetValue(ImageWorkflowKeys.imagePrintSetName, out var jps) && jps.ValueKind == JsonValueKind.String)
                    storedPrintSet = jps.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(storedPrintSet) && FindName("ImagePrintSetCombo") is ComboBox ps && ps.ItemsSource is System.Collections.IEnumerable items)
                {
                    foreach (var it in items)
                    {
                        if (string.Equals(it?.ToString(), storedPrintSet, StringComparison.OrdinalIgnoreCase)) { ps.SelectedItem = it; break; }
                    }
                }

                // file name pattern (persisted WITHOUT extension now)
                if (FindName("ImageFileNamePatternBox") is TextBox patBox)
                {
                    string pattern = string.Empty;
                    if (wf?.Parameters != null && wf.Parameters.TryGetValue(ImageWorkflowKeys.fileNamePattern, out var je) && je.ValueKind == JsonValueKind.String)
                        pattern = je.GetString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(pattern)) pattern = "{SetName}"; // default now has no extension
                    patBox.Text = pattern;
                }

                if (FindName("ImagePrefixBox") is TextBox preBox)
                {
                    string pre = string.Empty;
                    if (wf?.Parameters != null && wf.Parameters.TryGetValue(ImageWorkflowKeys.prefix, out var jp) && jp.ValueKind == JsonValueKind.String)
                        pre = jp.GetString() ?? string.Empty;
                    preBox.Text = pre;
                }
                if (FindName("ImageSuffixBox") is TextBox sufBox)
                {
                    string suf = string.Empty;
                    if (wf?.Parameters != null && wf.Parameters.TryGetValue(ImageWorkflowKeys.suffix, out var js) && js.ValueKind == JsonValueKind.String)
                        suf = js.GetString() ?? string.Empty;
                    sufBox.Text = suf;
                }
                // format
                if (FindName("ImageFormatCombo") is ComboBox fmtCombo)
                {
                    string storedFormat = string.Empty;
                    if (wf?.Parameters != null && wf.Parameters.TryGetValue(ImageWorkflowKeys.imageFormat, out var jf) && jf.ValueKind == JsonValueKind.String)
                        storedFormat = (jf.GetString() ?? string.Empty).Trim().ToUpperInvariant();
                    foreach (var o in fmtCombo.Items)
                    {
                        if (o is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), storedFormat, StringComparison.OrdinalIgnoreCase)) { fmtCombo.SelectedItem = cbi; break; }
                    }
                }
                // crop mode
                if (FindName("ImageCropModeCombo") is ComboBox cmCombo)
                {
                    string cm = string.Empty;
                    if (wf?.Parameters != null && wf.Parameters.TryGetValue(ImageWorkflowKeys.cropMode, out var jcm) && jcm.ValueKind == JsonValueKind.String)
                        cm = jcm.GetString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(cm)) cm = "Static";
                    foreach (var o in cmCombo.Items)
                    {
                        if (o is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), cm, StringComparison.OrdinalIgnoreCase)) { cmCombo.SelectedItem = cbi; break; }
                    }
                }
                // crop offset
                if (FindName("ImageCropOffsetBox") is TextBox cropBox)
                {
                    string co = string.Empty;
                    if (wf?.Parameters != null && wf.Parameters.TryGetValue(ImageWorkflowKeys.cropOffset, out var jc) && jc.ValueKind == JsonValueKind.String)
                        co = jc.GetString() ?? string.Empty;
                    cropBox.Text = co;
                }
                // resolution preset
                if (FindName("ImageResolutionPresetCombo") is ComboBox resCombo)
                {
                    string res = string.Empty;
                    if (wf?.Parameters != null && wf.Parameters.TryGetValue(ImageWorkflowKeys.resolutionPreset, out var jr) && jr.ValueKind == JsonValueKind.String)
                        res = jr.GetString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(res)) res = "Medium";
                    foreach (var o in resCombo.Items)
                    {
                        if (o is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), res, StringComparison.OrdinalIgnoreCase)) { resCombo.SelectedItem = cbi; break; }
                    }
                }

                // --- New: hydrate scope & single view without marking dirty ---
                string scope = "PrintSet"; // default for legacy
                try
                {
                    if (wf?.Parameters != null &&
                        wf.Parameters.TryGetValue(ImageWorkflowKeys.exportScope, out var scopeEl) &&
                        scopeEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        scope = scopeEl.GetString() ?? "PrintSet";
                    }
                }
                catch { scope = "PrintSet"; }

                if (string.Equals(scope, "SingleView", StringComparison.OrdinalIgnoreCase))
                    ImageScopeSingleViewRadio.IsChecked = true;
                else
                    ImageScopePrintSetRadio.IsChecked = true;

                PopulateImageSingleViewList();

                try
                {
                    if (wf?.Parameters != null &&
                        wf.Parameters.TryGetValue(ImageWorkflowKeys.singleViewId, out var idEl) &&
                        idEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var savedId = idEl.GetString();
                        if (!string.IsNullOrWhiteSpace(savedId) && ImageSingleViewCombo?.Items?.Count > 0)
                        {
                            foreach (ComboBoxItem it in ImageSingleViewCombo.Items)
                            {
                                if (string.Equals(it.Tag as string, savedId, StringComparison.Ordinal))
                                { ImageSingleViewCombo.SelectedItem = it; break; }
                            }
                        }
                    }
                }
                catch { }

                ApplyImageScopeUiState();
                // --- End new scope hydration ---

                UpdateCropOffsetEnable();
                UpdateImagePreview();
                UpdateImageSaveState();
            }
            catch { }
            _hydratingImage = false; // end guard
        }

        private void PersistImageParameters(WorkflowDefinition existing)
        {
            var imageParams = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            void SP(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) imageParams[k] = ToJson(v); }

            var printSet = (FindName("ImagePrintSetCombo") as ComboBox)?.SelectedItem as string;
            SP(ImageWorkflowKeys.imagePrintSetName, printSet);

            // pattern: store WITHOUT extension now
            var patternRaw = (FindName("ImageFileNamePatternBox") as TextBox)?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(patternRaw)) patternRaw = "{SetName}";
            patternRaw = patternRaw.Replace("[SetName]", "{SetName}"); // normalize legacy brackets
            if (patternRaw.Contains("{SetName}")) // only sanitize if it looks like a pattern
            {
                // remove any extension if user typed one
                patternRaw = Path.GetFileNameWithoutExtension(patternRaw);
                patternRaw = SanitizeFileComponent(patternRaw);
            }
            SP(ImageWorkflowKeys.fileNamePattern, patternRaw);

            var prefixRaw = SanitizeFileComponent((FindName("ImagePrefixBox") as TextBox)?.Text?.Trim());
            if (!string.IsNullOrWhiteSpace(prefixRaw)) SP(ImageWorkflowKeys.prefix, prefixRaw);
            var suffixRaw = SanitizeFileComponent((FindName("ImageSuffixBox") as TextBox)?.Text?.Trim());
            if (!string.IsNullOrWhiteSpace(suffixRaw)) SP(ImageWorkflowKeys.suffix, suffixRaw);

            var cropModeCombo = FindName("ImageCropModeCombo") as ComboBox;
            var cropModeSel = (cropModeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(cropModeSel) && !string.Equals(cropModeSel, "Static", StringComparison.OrdinalIgnoreCase))
                SP(ImageWorkflowKeys.cropMode, cropModeSel);
            var cropBox = FindName("ImageCropOffsetBox") as TextBox;
            if (cropBox != null && double.TryParse(cropBox.Text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var off))
                SP(ImageWorkflowKeys.cropOffset, off.ToString(CultureInfo.InvariantCulture));

            var resCombo = FindName("ImageResolutionPresetCombo") as ComboBox;
            var resSel = (resCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(resSel)) SP(ImageWorkflowKeys.resolutionPreset, resSel);

            var fmtCombo = FindName("ImageFormatCombo") as ComboBox;
            var selFmt = (fmtCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(selFmt))
            {
                var up = selFmt.Trim().ToUpperInvariant();
                if (up == "PNG" || up == "BMP" || up == "TIFF") SP(ImageWorkflowKeys.imageFormat, up);
            }

            // New: persist export scope + single view id (conditionally)
            try
            {
                var scope = GetImageExportScope();
                imageParams[ImageWorkflowKeys.exportScope] = System.Text.Json.JsonDocument.Parse($"\"{scope}\"").RootElement;
                if (string.Equals(scope, "SingleView", StringComparison.OrdinalIgnoreCase))
                {
                    string? svId = null;
                    if (ImageSingleViewCombo?.SelectedItem is ComboBoxItem it) svId = it.Tag as string;
                    if (!string.IsNullOrWhiteSpace(svId))
                        imageParams[ImageWorkflowKeys.singleViewId] = System.Text.Json.JsonDocument.Parse($"\"{svId}\"").RootElement;
                    // Do not clear singleViewId when switching back to PrintSet; retain for round-trip.
                }
            }
            catch { }

            existing.Parameters = imageParams;
            EnsureActionId(existing, "export-image");
        }

        private (bool CanSave, string Reason) ComputeImageSaveEligibility()
        {
            try
            {
                // name
                var name = (FindName("ImageNameBox") as TextBox)?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(name)) return (false, "Name required");
                // pattern
                var pattern = (FindName("ImageFileNamePatternBox") as TextBox)?.Text ?? string.Empty;
                pattern = pattern.Replace("[SetName]", "{SetName}"); // normalize legacy brackets
                if (string.IsNullOrWhiteSpace(pattern)) return (false, "Pattern required");
                if (!pattern.Contains("{SetName}")) return (false, "Pattern must include {SetName}");
                // format
                var fmt = (FindName("ImageFormatCombo") as ComboBox)?.SelectedItem as ComboBoxItem;
                if (fmt == null || string.IsNullOrWhiteSpace(fmt.Content?.ToString())) return (false, "Select image format");
                // resolution preset
                var res = (FindName("ImageResolutionPresetCombo") as ComboBox)?.SelectedItem as ComboBoxItem;
                if (res == null) return (false, "Select resolution preset");
                // crop offset validation
                var cropBox = FindName("ImageCropOffsetBox") as TextBox;
                if (cropBox != null && !string.IsNullOrWhiteSpace(cropBox.Text))
                {
                    if (!double.TryParse(cropBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var off)) return (false, "Invalid crop offset");
                }

                // Scope-specific gating
                var scope = GetImageExportScope();
                if (string.Equals(scope, "SingleView", StringComparison.OrdinalIgnoreCase))
                {
                    if (!(ImageSingleViewCombo?.SelectedItem is ComboBoxItem)) return (false, "Select single view");
                }
                else
                {
                    var psSel = (FindName("ImagePrintSetCombo") as ComboBox)?.SelectedItem as string;
                    if (string.IsNullOrWhiteSpace(psSel)) return (false, "Select print set");
                }
                return (true, string.Empty);
            }
            catch { return (false, "Eligibility error"); }
        }

        private void UpdateImageSaveState()
        {
            var saveBtn = FindName("ImageSaveBtn") as Button;
            if (saveBtn != null)
            {
                var (canSave, reason) = ComputeImageSaveEligibility();
                saveBtn.IsEnabled = canSave;
                SetSaveVisual("ImageSaveBtn", _isDirtyImage && canSave);
                saveBtn.ToolTip = canSave ? null : reason;
            }
        }

        private void ImageResolutionPresetCombo_SelectionChanged(object s, SelectionChangedEventArgs e) { _presenter.OnMarkDirty("Image"); MarkDirty("Image"); UpdateImageSaveState(); }
        private void ImageCropOffsetBox_TextChanged(object s, TextChangedEventArgs e) { _presenter.OnMarkDirty("Image"); MarkDirty("Image"); UpdateImageSaveState(); }
        private void ImageFileNamePatternBox_TextChanged(object s, TextChangedEventArgs e)
        {
            try
            {
                if (s is TextBox tb && tb.Text.Contains("[SetName]"))
                {
                    var caret = tb.SelectionStart;
                    tb.Text = tb.Text.Replace("[SetName]", "{SetName}");
                    tb.SelectionStart = System.Math.Min(caret, tb.Text.Length);
                }
            }
            catch { }
            _presenter.OnMarkDirty("Image");
            MarkDirty("Image"); UpdateImagePreview(); UpdateImageSaveState();
        }
        private void ImagePrefixBox_TextChanged(object s, TextChangedEventArgs e) { _presenter.OnMarkDirty("Image"); MarkDirty("Image"); UpdateImagePreview(); UpdateImageSaveState(); }
        private void ImageSuffixBox_TextChanged(object s, TextChangedEventArgs e) { _presenter.OnMarkDirty("Image"); MarkDirty("Image"); UpdateImagePreview(); UpdateImageSaveState(); }
        private void ImageCropModeCombo_SelectionChanged(object s, SelectionChangedEventArgs e) { UpdateCropOffsetEnable(); _presenter.OnMarkDirty("Image"); MarkDirty("Image"); UpdateImageSaveState(); }
        private void ImageFormatCombo_SelectionChanged(object s, SelectionChangedEventArgs e) { _presenter.OnMarkDirty("Image"); MarkDirty("Image"); UpdateImagePreview(); UpdateImageSaveState(); }
        private void ImagePrintSetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { _presenter.OnMarkDirty("Image"); MarkDirty("Image"); UpdateImageSaveState(); }

        private void UpdateCropOffsetEnable()
        {
            try
            {
                var modeCombo = FindName("ImageCropModeCombo") as ComboBox;
                var box = FindName("ImageCropOffsetBox") as TextBox;
                if (modeCombo == null || box == null) return;
                var sel = (modeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Static";
                bool auto = string.Equals(sel, "Auto", StringComparison.OrdinalIgnoreCase);
                box.IsEnabled = auto;
                box.Opacity = auto ? 1.0 : 0.5;
                if (!auto) box.Text = string.Empty;
            }
            catch { }
        }

        private void UpdateImagePreview()
        {
            try
            {
                var prefix = (FindName("ImagePrefixBox") as TextBox)?.Text ?? string.Empty;
                var suffix = (FindName("ImageSuffixBox") as TextBox)?.Text ?? string.Empty;
                var patternRaw = (FindName("ImageFileNamePatternBox") as TextBox)?.Text ?? "{SetName}";
                string core = Path.GetFileNameWithoutExtension(patternRaw.Trim());
                if (string.IsNullOrWhiteSpace(core)) core = "{SetName}";
                var fmtCombo = FindName("ImageFormatCombo") as ComboBox;
                var selFmt = (fmtCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
                var ext = MapFormatToExt(selFmt);
                var finalName = prefix + core + suffix + ext;
                var lbl = FindName("ImagePreviewText") as TextBlock;
                if (lbl != null) lbl.Text = $"Preview: {finalName}";
            }
            catch { }
        }

        private void SaveWorkflow_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement; if (btn == null) return;
            var tag = (btn.Tag as string) ?? string.Empty;
            if (tag == "Png") tag = "Image"; // backward tag mapping

            // Prefer VM values for base fields, with control fallback to preserve legacy behavior
            WorkflowTabBaseViewModel? baseVm = tag switch
            {
                "Pdf" => _presenter.PdfWorkflow,
                "Image" => _presenter.ImageWorkflow,
                "Rvt" => _presenter.RvtBase,
                _ => _presenter.CsvBase
            };

            string nameVal = baseVm?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(nameVal)) nameVal = (FindName(tag + "NameBox") as TextBox)?.Text?.Trim() ?? string.Empty;

            string scopeVal = baseVm?.WorkflowScope ?? string.Empty;
            if (string.IsNullOrWhiteSpace(scopeVal)) scopeVal = (FindName(tag + "ScopeCombo") as ComboBox)?.SelectedItem as string ?? string.Empty;

            string descVal = baseVm?.Description ?? string.Empty;
            if (string.IsNullOrWhiteSpace(descVal)) descVal = (FindName(tag + "DescBox") as TextBox)?.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(nameVal) || string.IsNullOrWhiteSpace(scopeVal)) { _dialogs.Info("Save", "Name and Scope required."); return; }

            var output = tag switch { "Rvt" => OutputType.Rvt, "Pdf" => OutputType.Pdf, "Image" => OutputType.Image, "Csv" => OutputType.Csv, _ => OutputType.Pdf };

            // Keep/identify current workflow (create if needed)
            var existing = GetSelectedFromTab(tag);
            if (existing == null)
            {
                existing = new WorkflowDefinition { Id = Guid.NewGuid().ToString("N"), Kind = WorkflowKind.Internal, Output = output, ActionIds = new List<string>(), Parameters = new Dictionary<string, JsonElement>() };
                _catalog.Settings.Workflows ??= new List<WorkflowDefinition>();
                _catalog.Settings.Workflows.Add(existing);
            }

            // Persist base fields, including Description
            existing.Name = nameVal; existing.Scope = scopeVal; existing.Description = descVal;

            switch (output)
            {
                case OutputType.Pdf:
                {
                    var p = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                    void S(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) p[k] = ToJson(v); }

                    var pdfVm = _presenter.PdfWorkflow;
                    var pdfSetName = pdfVm?.SelectedSetName ?? (FindName("ViewSetCombo") as ComboBox)?.SelectedItem as string;
                    var pdfSetupName = pdfVm?.SelectedPrintSet ?? (FindName("ExportSetupCombo") as ComboBox)?.SelectedItem as string;
                    var pdfPattern = pdfVm?.Pattern ?? (FindName("FileNamePatternBox") as TextBox)?.Text?.Trim();

                    if (string.IsNullOrWhiteSpace(pdfPattern) || !pdfPattern.Contains("{SetName}")) pdfPattern = "{SetName}.pdf";
                    if (!pdfPattern.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) pdfPattern += ".pdf";
                    pdfPattern = SanitizeFileComponent(pdfPattern);

                    S(PdfWorkflowKeys.PrintSetName, pdfSetName);
                    S(PdfWorkflowKeys.ExportSetupName, pdfSetupName);
                    S(PdfWorkflowKeys.FileNamePattern, pdfPattern);
                    existing.Parameters = p; EnsureActionId(existing, "export-pdf");
                    break;
                }
                case OutputType.Image:
                {
                    PersistImageParameters(existing);
                    break;
                }
                case OutputType.Rvt:
                {
                    EnsureActionId(existing, "export-rvt");
                    break;
                }
                case OutputType.Csv:
                {
                    EnsureActionId(existing, "export-csv");
                    break;
                }
            }

            // Persist to disk and refresh UI lists
            _catalog.SaveAndRefresh();
            RefreshMainList();
            RefreshSavedCombos();

            // Restore selection so controls don't blank out
            try
            {
                SelectInCombo(tag + "SavedCombo", existing.Id);
            }
            catch { }

            // Mark as clean and recompute buttons/preview
            MarkDirty(tag, false);
            UpdateCanSaveFor(tag);
            UpdateImageSaveState();
        }

        private void RvtOption_Checked(object sender, RoutedEventArgs e)
        {
            _presenter.OnMarkDirty("Rvt");
            MarkDirty("Rvt");
            UpdateCanSaveFor("Rvt");
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
