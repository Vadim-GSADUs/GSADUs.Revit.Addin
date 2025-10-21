using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class CsvWorkflowTabViewModel : WorkflowTabBaseViewModel, IDataErrorInfo
    {
        private readonly DelegateCommand _saveCsvCommand;

        public CsvWorkflowTabViewModel()
        {
            Scopes.Clear(); // populated by presenter

            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IsBaseSaveEnabled)
                    || e.PropertyName == nameof(Name)
                    || e.PropertyName == nameof(WorkflowScope)
                    || e.PropertyName == nameof(Description)
                    || e.PropertyName == nameof(CsvPattern))
                {
                    HasUnsavedChanges = true;
                    OnChanged(nameof(HasUnsavedChanges));
                    _saveCsvCommand.RaiseCanExecuteChanged();
                }

                // Scope-based default pattern swap (only when user hasn't customized)
                if (e.PropertyName == nameof(WorkflowScope))
                {
                    var scope = WorkflowScope ?? string.Empty;
                    var isDefaultCurrentSet = string.Equals(CsvPattern, DefaultPatternCurrentSet, StringComparison.OrdinalIgnoreCase);
                    var isDefaultEntire = string.Equals(CsvPattern, DefaultPatternEntireProject, StringComparison.OrdinalIgnoreCase);
                    if (string.Equals(scope, "EntireProject", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(CsvPattern) || isDefaultCurrentSet)
                            CsvPattern = DefaultPatternEntireProject;
                    }
                    else if (string.Equals(scope, "CurrentSet", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(CsvPattern) || isDefaultEntire)
                            CsvPattern = DefaultPatternCurrentSet;
                    }
                }
            };

            NewCommand = new DelegateCommand(_ => Reset());
            _saveCsvCommand = new DelegateCommand(_ => SaveCommand?.Execute(null), _ => CanSaveCsv());

            // React to schedule selection changes to keep preview and Save state in sync
            AvailableSchedules.CollectionChanged += AvailableSchedules_CollectionChanged;
        }

        private void AvailableSchedules_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (var it in e.NewItems.OfType<ScheduleOption>())
                {
                    it.PropertyChanged += ScheduleItem_PropertyChanged;
                }
            }
            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (var it in e.OldItems.OfType<ScheduleOption>())
                {
                    it.PropertyChanged -= ScheduleItem_PropertyChanged;
                }
            }
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // Reattach to all items
                foreach (var it in AvailableSchedules)
                    it.PropertyChanged += ScheduleItem_PropertyChanged;
            }
            // Recompute preview and Save state on any list mutation
            RecomputePreview();
            _saveCsvCommand.RaiseCanExecuteChanged();
        }

        private void ScheduleItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScheduleOption.IsSelected))
            {
                RecomputePreview();
                _saveCsvCommand.RaiseCanExecuteChanged();
            }
        }

        private const string DefaultPatternCurrentSet = "{SetName} {ViewName}";
        private const string DefaultPatternEntireProject = "{FileName} {ViewName}";

        public ObservableCollection<SavedWorkflowListItem> SavedWorkflows { get; } = new();

        public ICommand NewCommand { get; }
        public ICommand? SaveCommand { get; set; }
        public ICommand SaveCsvCommand => _saveCsvCommand;

        private string? _selectedWorkflowId;
        public string? SelectedWorkflowId
        {
            get => _selectedWorkflowId;
            set { if (_selectedWorkflowId != value) { _selectedWorkflowId = value; OnChanged(nameof(SelectedWorkflowId)); } }
        }

        // Schedules list (host document only)
        public ObservableCollection<ScheduleOption> AvailableSchedules { get; } = new();

        public string[] SelectedScheduleIds => AvailableSchedules.Where(o => o.IsSelected).Select(o => o.Id).ToArray();

        // File name pattern and preview list
        private string _csvPattern = DefaultPatternCurrentSet;
        public string CsvPattern
        {
            get => _csvPattern;
            set
            {
                var v = (value ?? string.Empty).Trim();
                if (_csvPattern != v)
                {
                    _csvPattern = v;
                    OnChanged(nameof(CsvPattern));
                    RecomputePreview();
                    _saveCsvCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<string> FileNamePreview { get; } = new();

        private string _modelFileName = string.Empty;
        public string ModelFileName
        {
            get => _modelFileName;
            set { if (_modelFileName != value) { _modelFileName = value ?? string.Empty; OnChanged(nameof(ModelFileName)); RecomputePreview(); } }
        }

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set { if (_hasUnsavedChanges != value) { _hasUnsavedChanges = value; OnChanged(nameof(HasUnsavedChanges)); } }
        }

        public void SetDirty(bool dirty)
        {
            HasUnsavedChanges = dirty;
            OnChanged(nameof(HasUnsavedChanges));
            _saveCsvCommand.RaiseCanExecuteChanged();
        }

        private void RecomputePreview()
        {
            try
            {
                FileNamePreview.Clear();
                var pattern = CsvPattern;
                if (string.IsNullOrWhiteSpace(pattern)) return;

                // Build up to first 10 preview items using selected schedules
                var names = AvailableSchedules.Where(o => o.IsSelected).Select(o => o.Name).Take(10).ToList();
                if (names.Count == 0) return;

                foreach (var vname in names)
                {
                    var baseName = ApplyTokens(pattern, setName: "{SetName}", fileName: ModelFileName, viewName: vname);
                    baseName = Sanitize(baseName);
                    if (!baseName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) baseName += ".csv";
                    FileNamePreview.Add(baseName);
                }
                if (AvailableSchedules.Count(o => o.IsSelected) > names.Count)
                {
                    FileNamePreview.Add($"(+{AvailableSchedules.Count(o => o.IsSelected) - names.Count} more)");
                }
            }
            catch { }
        }

        private static string ApplyTokens(string pattern, string setName, string fileName, string viewName)
        {
            return (pattern ?? string.Empty)
                .Replace("{SetName}", setName)
                .Replace("{FileName}", fileName)
                .Replace("{ViewName}", viewName);
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace('/', '_').Replace('\\', '_');
        }

        private bool CanSaveCsv()
        {
            return IsBaseSaveEnabled
                && !string.IsNullOrWhiteSpace(Name)
                && !string.IsNullOrWhiteSpace(WorkflowScope)
                && SelectedScheduleIds.Length > 0
                && IsValidPattern(CsvPattern);
        }

        // Allowed tokens only
        private static readonly string[] AllowedTokens = new[] { "{SetName}", "{FileName}", "{ViewName}" };
        private static readonly Regex TokenRegex = new Regex("\\{[^}]+\\}", RegexOptions.Compiled);

        public bool IsValidPattern(string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            var tokens = TokenRegex.Matches(pattern).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).Distinct().ToList();
            // All tokens used must be allowed
            foreach (var t in tokens)
                if (!AllowedTokens.Contains(t)) return false;
            return true;
        }

        public string? SelectedWorkflowIdInternal => SelectedWorkflowId;

        public string Error => string.Empty;
        public string this[string columnName]
        {
            get
            {
                if (columnName == nameof(CsvPattern))
                {
                    return IsValidPattern(CsvPattern) ? string.Empty : "Only {SetName}, {FileName}, {ViewName} tokens are allowed.";
                }
                return string.Empty;
            }
        }

        public void Reset()
        {
            SelectedWorkflowId = null;
            Name = string.Empty;
            WorkflowScope = string.Empty;
            Description = string.Empty;
            CsvPattern = DefaultPatternCurrentSet;
            foreach (var o in AvailableSchedules) o.IsSelected = false;
            FileNamePreview.Clear();
            SetDirty(true);
            _saveCsvCommand.RaiseCanExecuteChanged();
        }
    }

    internal sealed class ScheduleOption : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _name = string.Empty;
        private bool _isSelected;

        public string Id { get => _id; set { if (_id != value) { _id = value ?? string.Empty; OnPropertyChanged(nameof(Id)); } } }
        public string Name { get => _name; set { if (_name != value) { _name = value ?? string.Empty; OnPropertyChanged(nameof(Name)); } } }

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
