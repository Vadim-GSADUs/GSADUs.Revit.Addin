using System.ComponentModel;
using System.Collections.ObjectModel;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class PdfWorkflowTabViewModel : WorkflowTabBaseViewModel, INotifyPropertyChanged
    {
        private string _pattern = "";
        private string? _selectedSetName;
        private string? _selectedPrintSet;
        private bool _isSaveEnabled;
        private bool _hasUnsavedChanges;

        public string Pattern
        {
            get => _pattern;
            set
            {
                if (_pattern != value)
                {
                    _pattern = value;
                    OnChanged(nameof(Pattern));
                    HasUnsavedChanges = true;
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                    RecomputeLocal();
                }
            }
        }
        public string? SelectedSetName
        {
            get => _selectedSetName;
            set
            {
                if (_selectedSetName != value)
                {
                    _selectedSetName = value;
                    OnChanged(nameof(SelectedSetName));
                    HasUnsavedChanges = true;
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                    RecomputeLocal();
                }
            }
        }
        public string? SelectedPrintSet
        {
            get => _selectedPrintSet;
            set
            {
                if (_selectedPrintSet != value)
                {
                    _selectedPrintSet = value;
                    OnChanged(nameof(SelectedPrintSet));
                    HasUnsavedChanges = true;
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                    RecomputeLocal();
                }
            }
        }
        public bool IsSaveEnabled
        {
            get => _isSaveEnabled;
            private set
            {
                if (_isSaveEnabled != value)
                {
                    _isSaveEnabled = value;
                    OnChanged(nameof(IsSaveEnabled));
                }
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                if (_hasUnsavedChanges != value)
                {
                    _hasUnsavedChanges = value;
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                }
            }
        }

        public void SetDirty(bool dirty)
        {
            HasUnsavedChanges = dirty;
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        public ObservableCollection<string> AvailableViewSets { get; set; } = new();
        public ObservableCollection<string> AvailableExportSetups { get; set; } = new();

        private void RecomputeLocal()
        {
            var ok = !string.IsNullOrWhiteSpace(SelectedSetName)
                     && !string.IsNullOrWhiteSpace(SelectedPrintSet)
                     && !string.IsNullOrWhiteSpace(Pattern)
                     && Pattern.Contains("{SetName}");
            IsSaveEnabled = ok && IsBaseSaveEnabled;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
