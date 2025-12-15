using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Abstractions;
using GSADUs.Revit.Addin.Infrastructure;
using System;
using System.Collections.Generic;
using System.ComponentModel; // for ICollectionView & INotifyPropertyChanged
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ListView = System.Windows.Controls.ListView;
// Aliases
using TextBox = System.Windows.Controls.TextBox;

namespace GSADUs.Revit.Addin.UI
{
    public partial class BatchExportWindow : Window
    {
        // --- Singleton management ---
        private static BatchExportWindow? _activeInstance;
        public static bool HasOpenInstance => _activeInstance != null && _activeInstance.IsLoaded && _activeInstance.Visibility == Visibility.Visible;
        public static bool TryActivateExisting()
        {
            if (!HasOpenInstance) return false;
            try
            {
                _activeInstance!.Topmost = true; // bring to front
                _activeInstance.Activate();
                _activeInstance.Topmost = false; // reset
            }
            catch { }
            return true;
        }
        private void RegisterInstance()
        {
            _activeInstance = this;
            try { this.Closed += (_, __) => { if (ReferenceEquals(_activeInstance, this)) _activeInstance = null; }; } catch { }
        }
        // --- end singleton management ---

        private readonly UIDocument _uidoc;
        private readonly IProjectSettingsProvider _settingsProvider;
        private AppSettings _settings;
        public BatchRunOptions? Result { get; private set; }
        private readonly WorkflowCatalogChangeNotifier? _catalogNotifier;
        private IDisposable? _catalogSubscription;

        private BatchExportPrefs _prefs = BatchExportPrefs.Load();
        private bool _curationAppliedFlag; // tracks if SSM Save&Log occurred this session and deferred apply executed
        private bool _workflowSelectionDirty; // tracks if SelectedWorkflowIds changed and need persistence

        // Persist currently selected SetIds across grid refresh / external dialog re-entry
        private readonly HashSet<string> _selectedSetIds = new(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] DefaultV2SetColumns = new[] {
            "SetName","AuditDate","AuditStatus","IgnoreFlag","IgnoreReason",
            "MemberCount","MembersHash","export-pdf_Status","export-pdf_ExportDate"
        };

        public sealed class ColumnChoice : INotifyPropertyChanged
        {
            public string Name { get; init; } = string.Empty;
            private bool _isSelected;
            public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } } }
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly List<ColumnChoice> _setColumnChoices = new();
        public IEnumerable<ColumnChoice> ColumnChoices => _setColumnChoices;

        // Row models now notify selection changes so the HashSet stays in sync even if bindings update off-focus
        private sealed class SetRow : INotifyPropertyChanged
        {
            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value) return;
                    _isSelected = value;
                    SelectionChanged?.Invoke(this);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
            public Dictionary<string, string> Data { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public string Key => Data.GetValueOrDefault("SetId") ?? string.Empty;
            public string Name => Data.GetValueOrDefault("SetName") ?? string.Empty;
            public Action<SetRow>? SelectionChanged { get; set; }
            public event PropertyChangedEventHandler? PropertyChanged;
        }
        private readonly List<SetRow> _setRows = new();
        private ICollectionView? _setsView; private GridViewColumnHeader? _setsLastHeader; private ListSortDirection _setsLastDir = ListSortDirection.Ascending;

        public IEnumerable<ColumnChoice> WorkColumnChoices => _workColumnChoices;
        private readonly List<ColumnChoice> _workColumnChoices = new();
        private sealed class WorkRow : INotifyPropertyChanged
        {
            private bool _isSelected;
            public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
            public string Id { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public string Output { get; set; } = string.Empty; public string Scope { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public event PropertyChangedEventHandler? PropertyChanged;
        }
        private readonly List<WorkRow> _workRows = new();
        private ICollectionView? _workView; private GridViewColumnHeader? _wfLastHeader; private ListSortDirection _wfLastDir = ListSortDirection.Ascending;

        private const double MinSectionHeight = 140;

        public BatchExportWindow(IEnumerable<string> _ignored, UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;

            _settingsProvider = ServiceBootstrap.Provider.GetService(typeof(IProjectSettingsProvider)) as IProjectSettingsProvider;
            if (_settingsProvider == null)
            {
                MessageBox.Show(this,
                    "Settings persistence is not available. Please restart Revit or reinstall the add-in.",
                    "Batch Export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                IsEnabled = false;
                return;
            }

            _settings = _settingsProvider.Load();

            _catalogNotifier = ServiceBootstrap.Provider.GetService(typeof(WorkflowCatalogChangeNotifier)) as WorkflowCatalogChangeNotifier;
            if (_catalogNotifier != null)
            {
                _catalogSubscription = _catalogNotifier.Subscribe((_, __) =>
                {
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(OnCatalogChanged), DispatcherPriority.Background);
                    }
                    catch { }
                });
            }
            RegisterInstance();
            this.Width = _prefs.WindowWidth > 0 ? _prefs.WindowWidth : this.Width;
            this.Height = _prefs.WindowHeight > 0 ? _prefs.WindowHeight : this.Height;

            LoadCsvIntoSetsList();
            LoadWorkflowsIntoList();
        }

        public bool IsDryRun() => _settings.DryrunDiagnostics;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var total = RootGrid.RowDefinitions[0].ActualHeight + RootGrid.RowDefinitions[2].ActualHeight;
                if (total > 0)
                {
                    var top = Math.Max(100, total * Math.Clamp(_prefs.SplitterRatio, 0.1, 0.9));
                    RootGrid.RowDefinitions[0].Height = new GridLength(top, GridUnitType.Pixel);
                    RootGrid.RowDefinitions[2].Height = new GridLength(Math.Max(100, total - top), GridUnitType.Pixel);
                }
            }
            catch { }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                _prefs.WindowWidth = this.Width; _prefs.WindowHeight = this.Height;
                var total = RootGrid.RowDefinitions[0].ActualHeight + RootGrid.RowDefinitions[2].ActualHeight;
                _prefs.SplitterRatio = total > 0 ? RootGrid.RowDefinitions[0].ActualHeight / total : 0.5;
                SaveColumnOrder((FindName("SetsList") as ListView)!, _prefs.Sets.ColumnOrder, skipFirst: true);
                SaveColumnOrder((FindName("WorkflowsListView") as ListView)!, _prefs.Workflows.ColumnOrder, skipFirst: true);
                SaveSelectedWorkflowIds();
                BatchExportPrefs.Save(_prefs);
            }
            catch { }

            _catalogSubscription?.Dispose();
            _catalogSubscription = null;
        }

        private static void SaveColumnOrder(ListView lv, List<string> target, bool skipFirst)
        {
            try
            {
                if (lv.View is not GridView gv) return;
                target.Clear();
                for (int i = 0; i < gv.Columns.Count; i++)
                {
                    if (skipFirst && i == 0) continue;
                    var header = gv.Columns[i].Header as string;
                    if (!string.IsNullOrWhiteSpace(header)) target.Add(header);
                }
            }
            catch { }
        }

        private static string Get(Dictionary<string, string> row, string key)
        {
            if (row == null) return null;
            row.TryGetValue(key, out var v);
            return v;
        }

        private static string ShortDate(string? iso)
        {
            if (string.IsNullOrWhiteSpace(iso)) return string.Empty;
            return DateTime.TryParse(iso, out var dt) ? dt.ToString("yy-MM-dd HH:mm") : iso!;
        }

        private string ResolveLogPath()
        {
            var doc = _uidoc?.Document;
            if (doc == null) throw new InvalidOperationException("No active document.");
            var logDir = _settingsProvider.GetEffectiveLogDir(_settings);
            if (string.IsNullOrWhiteSpace(logDir)) throw new InvalidOperationException("Log directory is not configured.");
            System.IO.Directory.CreateDirectory(logDir);
            var modelName = System.IO.Path.GetFileNameWithoutExtension(doc.PathName) ?? "Model";
            return System.IO.Path.Combine(logDir, $"{modelName} Batch Export Log.csv");
        }

        private void OnSetRowSelectionChanged(SetRow row)
        {
            if (string.IsNullOrWhiteSpace(row.Key)) return;
            if (row.IsSelected) _selectedSetIds.Add(row.Key); else _selectedSetIds.Remove(row.Key);
        }

        private void EnsureSelectionConsistency()
        {
            // Remove keys that no longer exist in the refreshed rows
            var existing = new HashSet<string>(_setRows.Select(r => r.Key), StringComparer.OrdinalIgnoreCase);
            var toRemove = _selectedSetIds.Where(k => !existing.Contains(k)).ToList();
            foreach (var k in toRemove) _selectedSetIds.Remove(k);
        }

        private void OnCatalogChanged()
        {
            try
            {
                _settings = _settingsProvider.Load();
                LoadWorkflowsIntoList();
            }
            catch { }
        }

        private void LoadCsvIntoSetsList()
        {
            try
            {
                var path = ResolveLogPath();
                var factory = ServiceBootstrap.Provider.GetService(typeof(IBatchLogFactory)) as IBatchLogFactory ?? new CsvBatchLogger();
                var log = GSADUs.Revit.Addin.Logging.GuardedBatchLog.Wrap(factory.Load(path));
                var headers = factory.ReadHeadersOrDefaults(path);

                _setColumnChoices.Clear();
                bool havePrefs = _prefs.Sets.VisibleColumns.Count > 0;
                var prefSet = new HashSet<string>(_prefs.Sets.VisibleColumns, StringComparer.OrdinalIgnoreCase);
                var defaultSet = new HashSet<string>(DefaultV2SetColumns, StringComparer.OrdinalIgnoreCase);

                foreach (var h in headers)
                {
                    if (string.Equals(h, "MemberIds", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(h, "Before", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(h, "PlusAdded", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(h, "MinusRemoved", StringComparison.OrdinalIgnoreCase)) continue;
                    bool visible = havePrefs ? prefSet.Contains(h) : defaultSet.Contains(h);
                    _setColumnChoices.Add(new ColumnChoice { Name = h, IsSelected = visible });
                }

                if (!_setColumnChoices.Any(c => c.Name.Equals("export-pdf_Status", StringComparison.OrdinalIgnoreCase)))
                {
                    bool visible = havePrefs ? prefSet.Contains("export-pdf_Status") : defaultSet.Contains("export-pdf_Status");
                    _setColumnChoices.Add(new ColumnChoice { Name = "export-pdf_Status", IsSelected = visible });
                }
                if (!_setColumnChoices.Any(c => c.Name.Equals("export-pdf_ExportDate", StringComparison.OrdinalIgnoreCase)))
                {
                    bool visible = havePrefs ? prefSet.Contains("export-pdf_ExportDate") : defaultSet.Contains("export-pdf_ExportDate");
                    _setColumnChoices.Add(new ColumnChoice { Name = "export-pdf_ExportDate", IsSelected = visible });
                }

                var doc = _uidoc?.Document;
                var rows = log.GetRows().ToList();
                if (rows.Count == 0 && doc != null)
                {
                    try
                    {
                        var factory2 = ServiceBootstrap.Provider.GetService(typeof(IBatchLogFactory)) as IBatchLogFactory
                                      ?? throw new InvalidOperationException("IBatchLogFactory not available.");
                        var log2 = factory2.Load(path);
                        new LogSyncService().EnsureSync(_uidoc.Document, log2, factory2);
                        log2.Save(path);
                        rows = log2.GetRows().ToList();
                    }
                    catch (Exception ex)
                    {
                        try { PerfLogger.Write("BatchExport.AuditError", $"path={path} :: " + ex, TimeSpan.Zero); } catch { }
                        ShowShortError("Audit failed: " + ex.Message + ". See Performance Log.");
                        return;
                    }
                }

                _setRows.Clear();
                foreach (var r in rows)
                {
                    var display = new Dictionary<string, string>(r, StringComparer.OrdinalIgnoreCase);
                    display["AuditDate"] = ShortDate(Get(display, "AuditDate"));
                    var wf = "export-pdf";
                    if (display.TryGetValue($"{wf}_ExportDate", out var wfDate))
                        display[$"{wf}_ExportDate"] = ShortDate(wfDate);

                    var auditStatus = Get(display, "AuditStatus") ?? string.Empty;
                    var membersHash = Get(display, "MembersHash") ?? string.Empty;
                    var expDate = Get(display, $"{wf}_ExportDate");
                    var expSig = Get(display, $"{wf}_ExportSig");
                    var blocked = string.Equals(auditStatus, "Ignored", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(auditStatus, "Ambiguous", StringComparison.OrdinalIgnoreCase);
                    string derived;
                    if (blocked) derived = "Blocked";
                    else if (string.IsNullOrWhiteSpace(expDate)) derived = "Missing";
                    else
                    {
                        var currSig = HashUtil.Fnv1a64Hex(membersHash + "|" + wf);
                        derived = string.Equals(currSig, expSig, StringComparison.OrdinalIgnoreCase) ? "UpToDate" : "OutOfDate";
                    }
                    display[$"{wf}_Status"] = derived;

                    var row = new SetRow { Data = display, SelectionChanged = OnSetRowSelectionChanged };
                    // Restore prior selection state if present in HashSet
                    if (_selectedSetIds.Contains(row.Key)) row.IsSelected = true; // setter updates hash again but idempotent
                    _setRows.Add(row);
                }
                EnsureSelectionConsistency();

                _setsView = CollectionViewSource.GetDefaultView(_setRows);
                (FindName("SetsList") as ListView)!.ItemsSource = _setsView;
                RebuildSetsColumns();
                if (FindName("SetsFilterBox") is TextBox filterBox) filterBox.Text = _prefs.Sets.FilterText ?? string.Empty;
            }
            catch (Exception ex)
            {
                ShowShortError($"Audit failed: {ex.Message}. See log for details.");
            }
        }

        private void RebuildSetsColumns()
        {
            var list = (FindName("SetsList") as ListView)!; var gv = list.View as GridView; if (gv == null) return; gv.Columns.Clear();
            gv.Columns.Add(BuildCheckAllColumn(isForSets: true));
            foreach (var name in OrderByPrefs(_setColumnChoices, _prefs.Sets.ColumnOrder).Where(c => c.IsSelected).Select(c => c.Name))
            {
                var col = new GridViewColumn { Header = name, DisplayMemberBinding = new Binding($"Data[{name}]") };
                if (_prefs.Sets.ColumnWidths.TryGetValue(name, out var w) && w > 0) col.Width = w; else col.Width = Double.NaN;
                AttachWidthTracking(col, name, isWorkflow: false);
                gv.Columns.Add(col);
            }
        }

        private IEnumerable<ColumnChoice> OrderByPrefs(List<ColumnChoice> choices, List<string> order)
            => (order == null || order.Count == 0) ? choices : choices.OrderBy(c => { int i = order.IndexOf(c.Name); return i < 0 ? int.MaxValue : i; });

        private void AttachWidthTracking(GridViewColumn col, string name, bool isWorkflow)
        {
            try
            {
                var dpd = DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn));
                dpd.AddValueChanged(col, (s, e) =>
                {
                    double w = col.Width; if (double.IsNaN(w) || w <= 0) return;
                    var dict = isWorkflow ? _prefs.Workflows.ColumnWidths : _prefs.Sets.ColumnWidths; dict[name] = w; BatchExportPrefs.Save(_prefs);
                });
            }
            catch { }
        }

        private GridViewColumn BuildCheckAllColumn(bool isForSets)
        {
            var template = new DataTemplate();
            var fef = new FrameworkElementFactory(typeof(CheckBox));
            var binding = new Binding("IsSelected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
            fef.SetBinding(CheckBox.IsCheckedProperty, binding);
            fef.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            template.VisualTree = fef;
            var header = new Button { Content = "Check All" };
            header.Click += (s, e) => ToggleCheckAll(isForSets, header);
            return new GridViewColumn { Header = header, CellTemplate = template, Width = 90 };
        }

        private void ToggleCheckAll(bool isForSets, Button headerBtn)
        {
            if (isForSets)
            {
                var items = _setsView != null ? _setsView.Cast<SetRow>() : _setRows;
                bool allChecked = items.All(r => r.IsSelected);
                bool newVal = !allChecked;
                if (!newVal)
                {
                    foreach (var r in items) r.IsSelected = false;
                }
                else
                {
                    foreach (var r in items)
                    {
                        var status = Get(r.Data, "AuditStatus");
                        var ignored = string.Equals(Get(r.Data, "IgnoreFlag"), "true", StringComparison.OrdinalIgnoreCase);
                        r.IsSelected = string.Equals(status, "Valid", StringComparison.OrdinalIgnoreCase) && !ignored;
                    }
                }
                headerBtn.Content = newVal ? "Uncheck All" : "Check All";
                (FindName("SetsList") as ListView)?.Items.Refresh();
            }
            else
            {
                var items = _workView != null ? _workView.Cast<WorkRow>() : _workRows;
                bool allChecked = items.All(r => r.IsSelected);
                bool newVal = !allChecked;
                foreach (var r in items) r.IsSelected = newVal;
                headerBtn.Content = newVal ? "Uncheck All" : "Check All";
                (FindName("WorkflowsListView") as ListView)?.Items.Refresh();
                SaveSelectedWorkflowIds();
            }
        }

        private void SaveSelectedWorkflowIds()
        {
            try
            {
                var visibleSelected = (_workView != null ? _workView.Cast<WorkRow>() : _workRows)
                    .Where(w => w.IsSelected)
                    .Select(w => w.Id)
                    .ToList();
                _settings.SelectedWorkflowIds = visibleSelected;
                _workflowSelectionDirty = true;
            }
            catch { }
        }

        private void MainSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            try
            {
                var top = RootGrid.RowDefinitions[0].ActualHeight;
                var bottom = RootGrid.RowDefinitions[2].ActualHeight;
                var delta = e.VerticalChange;
                var topNew = top + delta; var bottomNew = bottom - delta;
                if (topNew < MinSectionHeight) { bottomNew -= (MinSectionHeight - topNew); topNew = MinSectionHeight; }
                if (bottomNew < MinSectionHeight) { topNew -= (MinSectionHeight - bottomNew); bottomNew = MinSectionHeight; }
                topNew = Math.Max(MinSectionHeight, topNew); bottomNew = Math.Max(MinSectionHeight, bottomNew);
                RootGrid.RowDefinitions[0].Height = new GridLength(topNew, GridUnitType.Pixel);
                RootGrid.RowDefinitions[2].Height = new GridLength(bottomNew, GridUnitType.Pixel);
                var total = topNew + bottomNew; if (total > 0) _prefs.SplitterRatio = topNew / total;
            }
            catch { }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        { if (e.Key == Key.Enter) { var element = Keyboard.FocusedElement as UIElement; element?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; } }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                var totalAvailable = RootGrid.ActualHeight - RootGrid.RowDefinitions[1].ActualHeight - RootGrid.RowDefinitions[3].ActualHeight - 16;
                if (totalAvailable <= 0) return;
                var currentTop = RootGrid.RowDefinitions[0].ActualHeight; var currentBottom = RootGrid.RowDefinitions[2].ActualHeight; var currentSum = currentTop + currentBottom; if (currentSum <= 0) return;
                var ratio = Math.Clamp(currentTop / currentSum, 0.1, 0.9);
                var minTop = Math.Max(RootGrid.RowDefinitions[0].MinHeight, MinSectionHeight);
                var minBottom = Math.Max(RootGrid.RowDefinitions[2].MinHeight, MinSectionHeight);
                var topNew = Math.Max(minTop, totalAvailable * ratio); var bottomNew = Math.Max(minBottom, totalAvailable - topNew);
                if (topNew + bottomNew > totalAvailable)
                {
                    var extra = (topNew + bottomNew) - totalAvailable;
                    if (topNew - minTop >= extra) topNew -= extra; else bottomNew -= extra;
                }
                RootGrid.RowDefinitions[0].Height = new GridLength(topNew, GridUnitType.Pixel);
                RootGrid.RowDefinitions[2].Height = new GridLength(bottomNew, GridUnitType.Pixel);
            }
            catch { }
        }

        private void SetsColumnsPopup_Closed(object sender, EventArgs e)
        { _prefs.Sets.VisibleColumns = _setColumnChoices.Where(c => c.IsSelected).Select(c => c.Name).ToList(); BatchExportPrefs.Save(_prefs); RebuildSetsColumns(); }

        private void SetsHeader_Click(object sender, RoutedEventArgs e)
        {
            if (_setsView == null) return; if (e.OriginalSource is not GridViewColumnHeader header) return;
            string sortBy = header.Tag as string ?? header.Column?.Header as string ?? ""; if (string.IsNullOrWhiteSpace(sortBy)) return;
            var dir = (_setsLastHeader == header && _setsLastDir == ListSortDirection.Ascending) ? ListSortDirection.Descending : ListSortDirection.Ascending;
            _setsLastHeader = header; _setsLastDir = dir; _setsView.SortDescriptions.Clear();
            if (sortBy == "IsSelected") _setsView.SortDescriptions.Add(new SortDescription("IsSelected", dir)); else _setsView.SortDescriptions.Add(new SortDescription($"Data[{sortBy}]", dir));
            _setsView.Refresh(); _prefs.Sets.SortBy = sortBy; _prefs.Sets.SortDesc = (dir == ListSortDirection.Descending); BatchExportPrefs.Save(_prefs);
        }

        private void SetsFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_setsView == null) return; var tb = sender as TextBox; if (tb == null) return; var term = (tb.Text ?? string.Empty).Trim();
            _prefs.Sets.FilterText = term; BatchExportPrefs.Save(_prefs);
            if (string.IsNullOrEmpty(term)) { _setsView.Filter = null; _setsView.Refresh(); return; }
            var cols = _setColumnChoices.Where(c => c.IsSelected).Select(c => c.Name).ToList();
            _setsView.Filter = (o) => { if (o is not SetRow row) return true; foreach (var c in cols) { if (row.Data.TryGetValue(c, out var v) && v?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true; } return false; };
            _setsView.Refresh();
        }

        private void LoadWorkflowsIntoList()
        {
            try
            {
                _workRows.Clear();
                var selected = new HashSet<string>(_settings.SelectedWorkflowIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var wf in (_settings.Workflows ?? new List<WorkflowDefinition>()))
                {
                    _workRows.Add(new WorkRow { Id = wf.Id, Name = wf.Name ?? string.Empty, Output = wf.Output.ToString(), Scope = wf.Scope ?? string.Empty, Description = wf.Description ?? string.Empty, IsSelected = selected.Contains(wf.Id) });
                }

                if (_workColumnChoices.Count == 0)
                {
                    foreach (var n in new[] { "Name", "Output", "Scope", "Description" })
                    { bool visible = _prefs.Workflows.VisibleColumns.Count > 0 ? _prefs.Workflows.VisibleColumns.Contains(n) : true; _workColumnChoices.Add(new ColumnChoice { Name = n, IsSelected = visible }); }
                }

                _workView = CollectionViewSource.GetDefaultView(_workRows); (FindName("WorkflowsListView") as ListView)!.ItemsSource = _workView; RebuildWorkflowColumns();
                if (FindName("WfFilterBox") is TextBox filterBox) filterBox.Text = _prefs.Workflows.FilterText ?? string.Empty;
            }
            catch { }
        }

        private void RebuildWorkflowColumns()
        {
            var list = (FindName("WorkflowsListView") as ListView)!; var gv = list.View as GridView; if (gv == null) return; gv.Columns.Clear();
            gv.Columns.Add(BuildCheckAllColumn(isForSets: false));
            foreach (var name in _workColumnChoices.Where(c => c.IsSelected).Select(c => c.Name))
            {
                var col = new GridViewColumn { Header = name };
                col.DisplayMemberBinding = new Binding(name);
                if (_prefs.Workflows.ColumnWidths.TryGetValue(name, out var w) && w > 0) col.Width = w; else col.Width = Double.NaN;
                AttachWidthTracking(col, name, isWorkflow: true);
                gv.Columns.Add(col);
            }
        }

        private void WfColumnsPopup_Closed(object sender, EventArgs e)
        { _prefs.Workflows.VisibleColumns = _workColumnChoices.Where(c => c.IsSelected).Select(c => c.Name).ToList(); BatchExportPrefs.Save(_prefs); RebuildWorkflowColumns(); }

        private void WfHeader_Click(object sender, RoutedEventArgs e)
        {
            if (_workView == null) return; if (e.OriginalSource is not GridViewColumnHeader header) return; string sortBy = header.Tag as string ?? header.Column?.Header as string ?? ""; if (string.IsNullOrWhiteSpace(sortBy)) return;
            var dir = (_wfLastHeader == header && _wfLastDir == ListSortDirection.Ascending) ? ListSortDirection.Descending : ListSortDirection.Ascending; _wfLastHeader = header; _wfLastDir = dir;
            _workView.SortDescriptions.Clear(); _workView.SortDescriptions.Add(new SortDescription(sortBy, dir)); _workView.Refresh();
            _prefs.Workflows.SortBy = sortBy; _prefs.Workflows.SortDesc = (dir == ListSortDirection.Descending); BatchExportPrefs.Save(_prefs);
        }

        private void WfFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_workView == null) return; var tb = sender as TextBox; if (tb == null) return; var term = (tb.Text ?? string.Empty).Trim(); _prefs.Workflows.FilterText = term; BatchExportPrefs.Save(_prefs);
            if (string.IsNullOrEmpty(term)) { _workView.Filter = null; _workView.Refresh(); return; }
            var cols = _workColumnChoices.Where(c => c.IsSelected).Select(c => c.Name).ToList();
            _workView.Filter = (o) => { if (o is not WorkRow r) return true; foreach (var c in cols) { var v = c switch { "Name" => r.Name, "Output" => r.Output, "Scope" => r.Scope, "Description" => r.Description, _ => null }; if (!string.IsNullOrEmpty(v) && v.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true; } return false; };
            _workView.Refresh();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(_settings, _uidoc?.Document) { Owner = this };
            try { win.ShowDialog(); } catch { }
            _settings = _settingsProvider.Load();
            LoadCsvIntoSetsList();
            LoadWorkflowsIntoList();
        }

        private void Audit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new SelectionSetManagerWindow(_uidoc?.Document, _settings) { Owner = this };
                dlg.ShowDialog();
                if (dlg.SaveRequested)
                {
                    var doc = _uidoc?.Document;
                    if (doc != null)
                    {
                        CuratePlan? plan = CuratePlanCache.Get(doc);
                        if (plan == null)
                        {
                            try
                            {
                                var view = _uidoc?.ActiveView as Autodesk.Revit.DB.View;
                                if (view != null)
                                {
                                    using (PerfLogger.Measure("Curate.Compute.Deferred", string.Empty))
                                    {
                                        plan = AuditAndCurate.Compute(doc, view, null);
                                        CuratePlanCache.Store(doc, plan);
                                    }
                                }
                            }
                            catch { }
                        }

                        if (plan != null)
                        {
                            bool applied = false;
                            if (plan.AnyChanges)
                            {
                                using (PerfLogger.Measure("Curate.DeferredApply", string.Empty))
                                {
                                    var summary = AuditAndCurate.Apply(doc, plan);
                                    if (!summary.AnyChanges) { try { PerfLogger.Write("Curate.Apply.NoChange", "DeferredUnexpected", TimeSpan.Zero); } catch { } }
                                    else { applied = true; try { PerfLogger.Write("Curate.Apply.Commit", "Deferred", TimeSpan.Zero); } catch { } }
                                }
                                try { AuditAndCurate.ReconcileWithModel(doc, plan, true); } catch { }
                                try { CuratePlanCache.Store(doc, plan); } catch { }
                            }
                            else
                            {
                                // Skip apply, but still need to sync log for possible renames / flag updates
                                try { PerfLogger.Write("Curate.Deferred.SkipApply", "NoMembershipChanges", TimeSpan.Zero); } catch { }
                            }

                            // Always run a lightweight log sync so SetName (rename) & flags persist even with no membership deltas
                            try
                            {
                                using (PerfLogger.Measure("Curate.Deferred.LogSync", applied ? "Applied" : "NoApply"))
                                {
                                    var path = ResolveLogPath();
                                    var factory = ServiceBootstrap.Provider.GetService(typeof(IBatchLogFactory)) as IBatchLogFactory
                                                  ?? throw new InvalidOperationException("IBatchLogFactory not available.");
                                    var log = factory.Load(path);
                                    new LogSyncService().EnsureSync(_uidoc.Document, log, factory);
                                    if (dlg.IgnoredSetIds != null)
                                    {
                                        foreach (var id in dlg.IgnoredSetIds)
                                            log.Upsert(id, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["IgnoreFlag"] = "true", ["IgnoreReason"] = "by manager" });
                                    }
                                    if (dlg.AmbiguousSetIds != null)
                                    {
                                        foreach (var id in dlg.AmbiguousSetIds)
                                            log.Upsert(id, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["AuditStatus"] = "Ambiguous", ["AmbiguityNote"] = "by manager" });
                                    }
                                    // Second sync to normalize any newly flagged rows (status recalculation)
                                    new LogSyncService().EnsureSync(_uidoc.Document, log, factory);
                                    log.Save(path);
                                }
                                LoadCsvIntoSetsList();
                            }
                            catch (Exception ex)
                            {
                                try { PerfLogger.Write("BatchExport.AuditError", ex.ToString(), TimeSpan.Zero); } catch { }
                            }

                            _curationAppliedFlag = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { PerfLogger.Write("BatchExport.AuditError", ex.ToString(), TimeSpan.Zero); } catch { }
                ShowShortError($"{ex.GetType().Name}: {ex.Message}. See log for details.");
            }
        }

        private bool IsCsvEntireProjectSelected()
        {
            try
            {
                var selectedIds = new HashSet<string>(_settings.SelectedWorkflowIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                var selectedWorkflows = (_settings.Workflows ?? new List<WorkflowDefinition>())
                    .Where(w => selectedIds.Contains(w.Id))
                    .ToList();
                if (selectedWorkflows.Count == 0) return false; // nothing selected
                return selectedWorkflows.All(w => w.Output == OutputType.Csv && string.Equals(w.Scope, "EntireProject", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            SaveSelectedWorkflowIds();
            var visibleSetRows = _setsView != null ? _setsView.Cast<SetRow>().ToList() : _setRows;
            try { (FindName("SetsList") as ListView)?.UpdateLayout(); } catch { }

            // Build selectedRows via HashSet to avoid relying on potentially stale row.IsSelected flags
            var selectedRows = visibleSetRows.Where(r => _selectedSetIds.Contains(r.Key)).ToList();
            try { PerfLogger.Write("BatchExport.Debug.SelectionSnapshot", $"TotalRows={visibleSetRows.Count};CheckedRows={selectedRows.Count}", TimeSpan.Zero); } catch { }

            bool allowZeroSets = IsCsvEntireProjectSelected();

            if (selectedRows.Count == 0 && !allowZeroSets)
            {
                var doc = _uidoc?.Document;
                if (doc != null)
                {
                    CuratePlan? plan = CuratePlanCache.Get(doc);
                    if (plan == null)
                    {
                        try
                        {
                            var view = _uidoc?.ActiveView as Autodesk.Revit.DB.View;
                            if (view != null)
                            {
                                using (PerfLogger.Measure("Curate.Compute.ZeroSelection", string.Empty))
                                {
                                    plan = AuditAndCurate.Compute(doc, view, null);
                                    CuratePlanCache.Store(doc, plan);
                                }
                            }
                        }
                        catch { }
                    }
                    if (plan != null && plan.AnyChanges)
                    {
                        using (PerfLogger.Measure("Curate.Apply.ZeroSelection", string.Empty))
                        {
                            var summary = AuditAndCurate.Apply(doc, plan);
                            if (summary.AnyChanges) { try { PerfLogger.Write("Curate.Apply.Commit", "ZeroSelection", TimeSpan.Zero); } catch { } }
                            else { try { PerfLogger.Write("Curate.Apply.NoChange", "ZeroSelectionUnexpected", TimeSpan.Zero); } catch { } }
                        }
                        try { AuditAndCurate.ReconcileWithModel(doc, plan, true); } catch { }
                        try { CuratePlanCache.Store(doc, plan); } catch { }
                    }
                    else if (plan != null)
                    {
                        try { PerfLogger.Write("Curate.ZeroSelection.SkipApply", string.Empty, TimeSpan.Zero); } catch { }
                    }

                    try
                    {
                        using (PerfLogger.Measure("Curate.ZeroSelection.LogSync", plan?.AnyChanges == true ? "Applied" : "NoApply"))
                        {
                            var path = ResolveLogPath();
                            var factory = ServiceBootstrap.Provider.GetService(typeof(IBatchLogFactory)) as IBatchLogFactory
                                              ?? new CsvBatchLogger();
                            var log = factory.Load(path);
                            new LogSyncService().EnsureSync(doc, log, factory);
                            log.Save(path);
                        }
                        LoadCsvIntoSetsList();
                    }
                    catch (Exception ex) { try { PerfLogger.Write("BatchExport.ZeroSelection.LogSyncError", ex.ToString(), TimeSpan.Zero); } catch { } }
                }
                ShowShortError("No sets selected – exports skipped. (Curation/log sync performed.)");
                return;
            }

            var selectedIds = selectedRows.Select(r => r.Key).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var selectedNames = selectedRows.Select(r => r.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            try
            {
                var sb = new System.Text.StringBuilder();
                if (allowZeroSets)
                {
                    sb.AppendLine("Project-scoped CSV run (no sets).");
                    sb.AppendLine();
                }
                sb.AppendLine($"Selected Sets ({selectedNames.Count}):");
                foreach (var nm in selectedNames.Take(20)) sb.AppendLine(" - " + nm);
                if (selectedNames.Count > 20) sb.AppendLine($" (+{selectedNames.Count - 20} more)");
                sb.AppendLine();

                var workflows = (_settings.Workflows ?? new List<WorkflowDefinition>())
                    .Where(w => (_settings.SelectedWorkflowIds ?? new List<string>()).Contains(w.Id))
                    .ToList();
                var wfNames = workflows.Select(w => w.Name ?? w.Id).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                sb.AppendLine($"Selected Workflows ({wfNames.Count}):");
                foreach (var w in wfNames.Take(20)) sb.AppendLine(" - " + w);
                if (wfNames.Count > 20) sb.AppendLine($" (+{wfNames.Count - 20} more)");
                sb.AppendLine();

                sb.AppendLine("Output folder:");
                sb.AppendLine(" " + (_settingsProvider.GetEffectiveOutputDir(_settings) ?? string.Empty));
                sb.AppendLine();
                sb.AppendLine("Proceed with batch run?");

                var res = MessageBox.Show(this, sb.ToString(), "Confirm Batch Run", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
                if (res == MessageBoxResult.No)
                {
                    return; // user adjusts selections
                }
            }
            catch { }

            // Pre-audit / curate just the selected sets if configured and not already applied by SSM save
            try
            {
                // Skip pre-audit when running project-scoped CSV (no sets by design)
                if (!allowZeroSets)
                {
                    var doc = _uidoc?.Document;
                    if (doc != null)
                    {
                        var globalSettings = _settings;
                        if (globalSettings.DefaultRunAuditBeforeExport && !_curationAppliedFlag)
                        {
                            var view = _uidoc?.ActiveView as Autodesk.Revit.DB.View;
                            if (view != null)
                            {
                                CuratePlan? plan = null;
                                // Build restrict list (prefer UniqueIds) combining ids & names for fallback
                                var restrict = selectedIds.Concat(selectedNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                                using (PerfLogger.Measure("Curate.PreRun.Compute", $"SelCount={restrict.Count};Scope=Restricted"))
                                {
                                    try { plan = AuditAndCurate.Compute(doc, view, restrict, null); } catch { plan = null; }
                                }
                                if (plan != null)
                                {
                                    using (PerfLogger.Measure("Curate.PreRun.Apply", string.Empty))
                                    {
                                        try { AuditAndCurate.Apply(doc, plan); _curationAppliedFlag = true; } catch { }
                                    }
                                    try { AuditAndCurate.ReconcileWithModel(doc, plan, true); } catch { }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            Result = new BatchRunOptions
            {
                SetIds = selectedIds,
                SetNames = selectedNames,
                OutputDir = _settingsProvider.GetEffectiveOutputDir(_settings),
                Overwrite = _settings.DefaultOverwrite,
                SaveBefore = _settings.DefaultSaveBefore
            };
            this.DialogResult = true;
        }

        private void ShowShortError(string message)
        {
            try { MessageBox.Show(this, message, "Batch Export", MessageBoxButton.OK, MessageBoxImage.Information); }
            catch { }
        }

        private void ManageWorkflows_Click(object sender, RoutedEventArgs e)
        {
            var win = new WorkflowManagerWindow(_uidoc?.Document, _settings) { Owner = this };
            try { win.ShowDialog(); } catch { }
            _settings = _settingsProvider.Load();
            LoadWorkflowsIntoList();
        }
    }
}
