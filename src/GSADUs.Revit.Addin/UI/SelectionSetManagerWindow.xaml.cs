using Autodesk.Revit.DB;
using GSADUs.Revit.Addin.Abstractions;
using GSADUs.Revit.Addin.Infrastructure;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GSADUs.Revit.Addin.UI
{
    public partial class SelectionSetManagerWindow : Window
    {
        // --- Singleton management ---
        private static SelectionSetManagerWindow? _activeInstance;
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

        public bool SaveRequested { get; private set; }
        private readonly Document? _doc;
        private CuratePlan? _plan;
        private IDictionary<string, int>? _prevMembers;

        private readonly IDialogService _dialogs;
        private readonly IBatchLogFactory _logFactory;

        // Map of current selection set names -> UniqueId for deriving ids (rebuilt each audit)
        private readonly Dictionary<string, string> _nameToId = new(StringComparer.OrdinalIgnoreCase);

        // Published analyzer results (IDs are SelectionFilterElement.UniqueId)
        public IReadOnlyList<string> IgnoredSetIds { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> AmbiguousSetIds { get; private set; } = Array.Empty<string>();

        public sealed class Row : INotifyPropertyChanged
        {
            private string _setName = string.Empty;
            private string _editName = string.Empty;
            private bool _isEditing;

            public string SetName { get => _setName; set { if (_setName != value) { _setName = value; OnPropertyChanged(nameof(SetName)); } } }
            public string EditName { get => _editName; set { if (_editName != value) { _editName = value; OnPropertyChanged(nameof(EditName)); } } }
            public bool IsEditing { get => _isEditing; set { if (_isEditing != value) { _isEditing = value; OnPropertyChanged(nameof(IsEditing)); } } }

            public int Before { get; set; }
            public int Added { get; set; }
            public int Removed { get; set; }
            public int After { get; set; }
            public string Ambiguous { get; set; } = "";
            public string Details { get; set; } = string.Empty;

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private readonly IProjectSettingsProvider _settingsProvider;
        private readonly AppSettings _settings;

        public SelectionSetManagerWindow(Document? doc, AppSettings? settings = null)
        {
            System.Windows.Application.LoadComponent(this, new Uri("/GSADUs.Revit.Addin;component/UI/SelectionSetManagerWindow.xaml", UriKind.Relative));
            RegisterInstance();
            _doc = doc;
            _dialogs = ServiceBootstrap.Provider.GetService(typeof(IDialogService)) as IDialogService ?? new DialogService();
            _logFactory = ServiceBootstrap.Provider.GetService(typeof(IBatchLogFactory)) as IBatchLogFactory ?? new CsvBatchLogger();
            _settingsProvider = ServiceBootstrap.Provider.GetService(typeof(IProjectSettingsProvider)) as IProjectSettingsProvider
                                ?? new EsProjectSettingsProvider(() => _doc ?? RevitUiContext.Current?.ActiveUIDocument?.Document);
            _settings = settings ?? _settingsProvider.Load();

            // Minimal VM hookup (not used by XAML yet; no behavior change)
            try { this.DataContext = new SelectionSetManagerViewModel(); } catch { }

            RefreshSummary();

            // Try to restore a cached plan for this document to avoid re-running audit
            try
            {
                var cached = CuratePlanCache.Get(_doc);
                if (cached != null)
                {
                    _plan = cached;
                    _prevMembers = LoadPrevMembers();
                    PopulateFromPlan(_plan, _prevMembers);
                }
            }
            catch { }
        }

        private void RunAudit_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null)
            {
                _dialogs.Info("Audit", "No active document.");
                return;
            }

            try
            {
                using var _ = PerfLogger.Measure("SelectionManager.Audit.Compute", _doc.Title ?? "");
                var view = _doc.ActiveView;
                _plan = AuditAndCurate.Compute(_doc, view, new AuditAndCurate.AuditOptions());
                // Cache the plan for reuse while the Batch Export window is open
                try { CuratePlanCache.Store(_doc, _plan); } catch { }
                _prevMembers = LoadPrevMembers();
                PopulateFromPlan(_plan, _prevMembers);
            }
            catch (Exception ex)
            {
                _dialogs.Info("Audit", ex.Message);
            }
        }

        private void PopulateFromPlan(CuratePlan plan, IDictionary<string, int>? prevMembers)
        {
            _nameToId.Clear();
            if (_doc != null)
            {
                try
                {
                    foreach (var sf in new FilteredElementCollector(_doc)
                        .OfClass(typeof(SelectionFilterElement))
                        .Cast<SelectionFilterElement>())
                    {
                        var nm = sf.Name ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(nm) && !string.IsNullOrWhiteSpace(sf.UniqueId))
                            _nameToId[nm] = sf.UniqueId;
                    }
                }
                catch { }
            }

            var rows = new List<Row>();
            foreach (var d in plan.Deltas.OrderBy(x => x.SetName, StringComparer.OrdinalIgnoreCase))
            {
                var before = d.BeforeCount;
                var after = d.AfterCount;
                var removed = d.RemovedCount;
                var added = d.AddedCount;
                var details = string.IsNullOrWhiteSpace(d.Details) ? "(none)" : d.Details;

                if (prevMembers != null && prevMembers.TryGetValue(d.SetName, out var loggedMembers))
                {
                    if (loggedMembers > after)
                    {
                        before = Math.Max(before, loggedMembers);
                        var extraRemoved = loggedMembers - after;
                        removed = Math.Max(removed, extraRemoved);
                        if (details == "(none)") details = string.Empty;
                        if (extraRemoved > 0)
                        {
                            if (details.Length > 0) details += "\n";
                            details += $"-{extraRemoved} (from log: previously recorded members)";
                        }
                    }
                }

                rows.Add(new Row
                {
                    SetName = d.SetName,
                    EditName = d.SetName,
                    Before = before,
                    Added = added,
                    Removed = removed,
                    After = after,
                    Ambiguous = plan.AmbiguousSets.Contains(d.SetName, StringComparer.OrdinalIgnoreCase) ? "Yes" : "No",
                    Details = string.IsNullOrWhiteSpace(details) ? "(none)" : details
                });
            }

            if (this.FindName("RowsList") is ListView lv)
                lv.ItemsSource = rows;

            int changed = 0;
            foreach (var d in plan.Deltas)
            {
                bool hasComputed = d.HasChanges;
                bool hasLoggedMismatch = false;
                if (prevMembers != null && prevMembers.TryGetValue(d.SetName, out var prev))
                {
                    hasLoggedMismatch = (prev != d.AfterCount);
                }
                if (hasComputed || hasLoggedMismatch) changed++;
            }

            // Derive analyzer result ID lists directly from plan (no recomputation)
            try
            {
                IgnoredSetIds = plan.IgnoredSets
                    .Select(n => _nameToId.TryGetValue(n, out var id) ? id : null)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { IgnoredSetIds = Array.Empty<string>(); }
            try
            {
                AmbiguousSetIds = plan.AmbiguousSets
                    .Select(n => _nameToId.TryGetValue(n, out var id) ? id : null)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { AmbiguousSetIds = Array.Empty<string>(); }

            var counts =
                $"Valid sets: {plan.ValidSets.Count}\n" +
                $"Ignored sets: {plan.IgnoredSets.Count}\n" +
                $"Ambiguous sets: {plan.AmbiguousSets.Count}\n" +
                $"Changed sets (preview): {changed}";

            if (this.FindName("SummaryBlock") is TextBlock tb)
                tb.Text = counts;
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null)
            {
                _dialogs.Info("Selection Sets", "No active document.");
                return;
            }
            if (_plan == null)
            {
                _dialogs.Info("Selection Sets", "Run Audit first.");
                return;
            }

            // Refresh ID lists (defensive) – still needed for downstream log sync in BatchExportWindow
            try
            {
                IgnoredSetIds = _plan.IgnoredSets
                    .Select(n => _nameToId.TryGetValue(n, out var id) ? id : null)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { IgnoredSetIds = Array.Empty<string>(); }
            try
            {
                AmbiguousSetIds = _plan.AmbiguousSets
                    .Select(n => _nameToId.TryGetValue(n, out var id) ? id : null)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { AmbiguousSetIds = Array.Empty<string>(); }

            // Defer all writes: just stage the current plan in cache
            try { CuratePlanCache.Store(_doc, _plan); } catch { }
            SaveRequested = true;

            // Update summary to signal staging (no transaction executed yet)
            if (_plan != null)
            {
                PopulateFromPlan(_plan, _prevMembers);
                if (this.FindName("SummaryBlock") is TextBlock tb)
                {
                    tb.Text += "\n(Staged at " + DateTime.Now.ToString("HH:mm:ss") + "; will apply on dialog close)";
                }
            }
        }

        private void RenameBtn_Click(object sender, RoutedEventArgs e)
        {
            BeginInlineRename();
        }

        private void RowsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            BeginInlineRename();
        }

        private void BeginInlineRename()
        {
            if (this.FindName("RowsList") is not ListView lv) return;
            if (lv.SelectedItems?.Count != 1) return;
            if (lv.SelectedItem is not Row row) return;

            row.EditName = row.SetName;
            row.IsEditing = true;
            lv.UpdateLayout();

            var lvi = lv.ItemContainerGenerator.ContainerFromItem(row) as ListViewItem;
            if (lvi != null)
            {
                var editor = FindVisualChildByName<TextBox>(lvi, "SetNameEditor");
                if (editor != null)
                {
                    editor.Focus();
                    editor.SelectAll();
                }
            }
        }

        private static T? FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T fe && fe.Name == name) return fe;
                var result = FindVisualChildByName<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private void SetNameEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (this.FindName("RowsList") is not ListView lv) return;
            if ((sender as FrameworkElement)?.DataContext is not Row row) return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitRename(row);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelRename(row);
            }
        }

        private void SetNameEditor_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not Row row) return;
            CommitRename(row);
        }

        private void CommitRename(Row row)
        {
            if (_doc == null) { CancelRename(row); return; }
            var currentName = row.SetName;
            var newName = (row.EditName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(newName)) { _dialogs.Info("Rename Selection Set", "Name cannot be empty."); RefocusEditor(row); return; }
            if (string.Equals(newName, currentName, StringComparison.Ordinal)) { row.IsEditing = false; return; }

            var existing = new FilteredElementCollector(_doc)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .Select(f => f.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (existing.Contains(newName)) { _dialogs.Info("Rename Selection Set", "A selection set with that name already exists."); RefocusEditor(row); return; }

            try
            {
                using var scope = PerfLogger.Measure("SelectionManager.Rename", currentName + " -> " + newName);
                using (var tx = new Transaction(_doc, "Rename Selection Set"))
                {
                    tx.Start();
                    var sel = new FilteredElementCollector(_doc)
                        .OfClass(typeof(SelectionFilterElement))
                        .Cast<SelectionFilterElement>()
                        .FirstOrDefault(f => string.Equals(f.Name, currentName, StringComparison.OrdinalIgnoreCase));
                    if (sel == null) { tx.RollBack(); _dialogs.Info("Rename Selection Set", "Selected set no longer exists."); row.IsEditing = false; return; }
                    sel.Name = newName; tx.Commit();
                }
            }
            catch (Exception ex) { _dialogs.Info("Rename Selection Set", ex.Message); RefocusEditor(row); return; }

            try { CuratePlanCache.Invalidate(_doc); } catch { }
            row.SetName = newName; row.EditName = newName; row.IsEditing = false;

            if (_plan != null)
            {
                try
                {
                    if (_plan.ValidSets is List<string> vsList) { for (int i = 0; i < vsList.Count; i++) { if (string.Equals(vsList[i], currentName, StringComparison.OrdinalIgnoreCase)) { vsList[i] = newName; break; } } }
                    else { var vs = _plan.ValidSets.ToList(); for (int i = 0; i < vs.Count; i++) { if (string.Equals(vs[i], currentName, StringComparison.OrdinalIgnoreCase)) { vs[i] = newName; break; } } _plan.ValidSets = vs; }

                    if (_plan.AmbiguousSets is List<string> ambList) { for (int i = 0; i < ambList.Count; i++) { if (string.Equals(ambList[i], currentName, StringComparison.OrdinalIgnoreCase)) { ambList[i] = newName; break; } } }
                    else { var amb = _plan.AmbiguousSets.ToList(); for (int i = 0; i < amb.Count; i++) { if (string.Equals(amb[i], currentName, StringComparison.OrdinalIgnoreCase)) { amb[i] = newName; break; } } _plan.AmbiguousSets = amb; }

                    if (_plan.Deltas is List<SetDelta> delList) { foreach (var d in delList) { if (string.Equals(d.SetName, currentName, StringComparison.OrdinalIgnoreCase)) { d.SetName = newName; break; } } }
                    else { var del = _plan.Deltas.ToList(); foreach (var d in del) { if (string.Equals(d.SetName, currentName, StringComparison.OrdinalIgnoreCase)) { d.SetName = newName; break; } } _plan.Deltas = del; }

                    if (_prevMembers != null && _prevMembers.ContainsKey(currentName) && !_prevMembers.ContainsKey(newName)) { var val = _prevMembers[currentName]; _prevMembers.Remove(currentName); _prevMembers[newName] = val; }
                    try { CuratePlanCache.Store(_doc, _plan); } catch { }
                }
                catch { try { RefreshSummary(); } catch { } }
            }
        }

        private void RefocusEditor(Row row)
        {
            if (this.FindName("RowsList") is not ListView lv) return;
            row.IsEditing = true;
            lv.UpdateLayout();
            var lvi = lv.ItemContainerGenerator.ContainerFromItem(row) as ListViewItem;
            if (lvi != null)
            {
                var editor = FindVisualChildByName<TextBox>(lvi, "SetNameEditor");
                editor?.Focus();
                editor?.SelectAll();
            }
        }

        private void CancelRename(Row row)
        {
            row.EditName = row.SetName;
            row.IsEditing = false;
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { _dialogs.Info("Selection Sets", "No active document."); return; }
            if (this.FindName("RowsList") is not ListView lv) return;
            var row = lv.SelectedItem as Row;
            if (row == null) { _dialogs.Info("Selection Sets", "Select a single set to delete."); return; }

            var confirm = _dialogs.ConfirmYesNo("Confirm Delete", $"Delete selection set '{row.SetName}'?", "Only the saved Selection Set will be deleted; model elements remain.", defaultYes: false);
            if (!confirm) return;

            var deletedName = row.SetName;
            try
            {
                using var scope = PerfLogger.Measure("SelectionManager.Delete", deletedName);
                using (var tx = new Transaction(_doc, "Delete Selection Set"))
                {
                    tx.Start();
                    var sfilter = new FilteredElementCollector(_doc)
                        .OfClass(typeof(SelectionFilterElement))
                        .Cast<SelectionFilterElement>()
                        .FirstOrDefault(f => string.Equals(f.Name, deletedName, StringComparison.OrdinalIgnoreCase));
                    if (sfilter != null) _doc.Delete(sfilter.Id);
                    tx.Commit();
                }
            }
            catch (Exception ex) { _dialogs.Info("Delete Selection Set", ex.Message); }

            try { CuratePlanCache.Invalidate(_doc); } catch { }

            if (_plan != null)
            {
                try
                {
                    if (_plan.ValidSets is List<string> vsList) vsList.RemoveAll(n => string.Equals(n, deletedName, StringComparison.OrdinalIgnoreCase));
                    else { var vs = _plan.ValidSets.Where(n => !string.Equals(n, deletedName, StringComparison.OrdinalIgnoreCase)).ToList(); _plan.ValidSets = vs; }
                    if (_plan.AmbiguousSets is List<string> ambList) ambList.RemoveAll(n => string.Equals(n, deletedName, StringComparison.OrdinalIgnoreCase));
                    else { var amb = _plan.AmbiguousSets.Where(n => !string.Equals(n, deletedName, StringComparison.OrdinalIgnoreCase)).ToList(); _plan.AmbiguousSets = amb; }
                    if (_plan.Deltas is List<SetDelta> delList) { for (int i = delList.Count - 1; i >= 0; i--) if (string.Equals(delList[i].SetName, deletedName, StringComparison.OrdinalIgnoreCase)) delList.RemoveAt(i); }
                    else { var del = _plan.Deltas.Where(d => !string.Equals(d.SetName, deletedName, StringComparison.OrdinalIgnoreCase)).ToList(); _plan.Deltas = del; }
                    if (_prevMembers != null && _prevMembers.ContainsKey(deletedName)) _prevMembers.Remove(deletedName);
                    PopulateFromPlan(_plan, _prevMembers);
                    try { CuratePlanCache.Store(_doc, _plan); } catch { }
                }
                catch { try { RefreshSummary(); } catch { } }
            }
            else { RefreshSummary(); }
        }

        private void RowsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var singleSelected = (this.FindName("RowsList") as ListView)?.SelectedItems?.Count == 1;
            if (this.FindName("DeleteBtn") is Button btn)
                btn.IsEnabled = singleSelected == true;
            if (this.FindName("RenameBtn") is Button rbtn)
                rbtn.IsEnabled = singleSelected == true;
        }

        private string GetLogPath()
        {
            var logDir = _settingsProvider.GetEffectiveLogDir(_settings);
            var modelName = System.IO.Path.GetFileNameWithoutExtension(_doc?.PathName) ?? "Model";
            return System.IO.Path.Combine(logDir, San($"{modelName} Batch Export Log.csv"));
        }

        private Dictionary<string, int> LoadPrevMembers()
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var log = _logFactory.Load(GetLogPath());
                foreach (var row in log.GetRows())
                {
                    var key = row.GetValueOrDefault("SetName") ?? row.GetValueOrDefault("Key") ?? string.Empty; // prefer current column
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    // Current schema uses MemberCount; legacy used Members
                    var countText = row.GetValueOrDefault("MemberCount") ?? row.GetValueOrDefault("Members") ?? string.Empty;
                    if (int.TryParse(countText, out var n)) dict[key] = n;
                }
            }
            catch { }
            return dict;
        }

        private void RefreshSummary()
        {
            if (this.FindName("RowsList") is ListView lv)
                lv.ItemsSource = null;
            if (this.FindName("SummaryBlock") is TextBlock tb)
                tb.Text = "Run an audit to view details.";
            if (this.FindName("DeleteBtn") is Button btn)
                btn.IsEnabled = false;
            if (this.FindName("RenameBtn") is Button rbtn)
                rbtn.IsEnabled = false;
        }

        private static string San(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
