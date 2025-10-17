using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class CsvWorkflowTabViewModel : WorkflowTabBaseViewModel, IDataErrorInfo
    {
        private readonly DelegateCommand _saveCsvCommand;

        public CsvWorkflowTabViewModel()
        {
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IsBaseSaveEnabled)) { /* no-op for now */ }
                if (e.PropertyName == nameof(Name) || e.PropertyName == nameof(WorkflowScope) || e.PropertyName == nameof(Description))
                {
                    HasUnsavedChanges = true;
                    OnChanged(nameof(HasUnsavedChanges));
                    _saveCsvCommand.RaiseCanExecuteChanged();
                }
            };
            NewCommand = new DelegateCommand(_ => Reset());
            _saveCsvCommand = new DelegateCommand(_ => SaveCommand?.Execute(null), _ => CanSaveCsv());
        }

        public ObservableCollection<SavedWorkflowListItem> SavedWorkflows { get; } = new();

        public ICommand NewCommand { get; }
        public ICommand? SaveCommand { get; set; }
        public ICommand SaveCsvCommand => _saveCsvCommand;

        public ObservableCollection<string> CsvFiles { get; } = new();

        private string? _selectedCsv;
        public string? SelectedCsv
        {
            get => _selectedCsv;
            set { if (_selectedCsv != value) { _selectedCsv = value; OnChanged(nameof(SelectedCsv)); _saveCsvCommand.RaiseCanExecuteChanged(); } }
        }

        private string _csvPattern = "{SetName}.csv";
        public string CsvPattern
        {
            get => _csvPattern;
            set { if (_csvPattern != value) { _csvPattern = value ?? string.Empty; OnChanged(nameof(CsvPattern)); HasUnsavedChanges = true; OnChanged(nameof(HasUnsavedChanges)); _saveCsvCommand.RaiseCanExecuteChanged(); } }
        }

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set { if (_hasUnsavedChanges != value) { _hasUnsavedChanges = value; OnChanged(nameof(HasUnsavedChanges)); } }
        }

        public string? SelectedWorkflowId { get; set; }

        public string Error => string.Empty;
        public string this[string columnName]
        {
            get
            {
                if (columnName == nameof(CsvPattern))
                {
                    return IsValidPattern(CsvPattern) ? string.Empty : "Pattern must include {SetName}";
                }
                return string.Empty;
            }
        }

        private bool CanSaveCsv() => SelectedCsv != null && IsValidPattern(CsvPattern);

        public bool IsValidPattern(string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            return pattern.Contains("{SetName}");
        }

        private void Reset()
        {
            SelectedWorkflowId = null;
            Name = string.Empty;
            WorkflowScope = string.Empty;
            Description = string.Empty;
            CsvPattern = "{SetName}.csv";
            SelectedCsv = null;
            HasUnsavedChanges = true;
            _saveCsvCommand.RaiseCanExecuteChanged();
        }
    }
}
