using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class PdfWorkflowTabViewModel : WorkflowTabBaseViewModel, INotifyPropertyChanged
    {
        public PdfWorkflowTabViewModel()
        {
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IsBaseSaveEnabled)) RecomputeLocal();
            };
            NewCommand = new DelegateCommand(_ => Reset());
        }

        public ICommand NewCommand { get; }

        private string? _selectedWorkflowId;
        public string? SelectedWorkflowId
        {
            get => _selectedWorkflowId;
            set { if (_selectedWorkflowId != value) { _selectedWorkflowId = value; OnChanged(nameof(SelectedWorkflowId)); } }
        }

        private string _pattern = "";
        private string? _selectedSetName;
        private string? _selectedPrintSet;
        private bool _isSaveEnabled;
        private bool _hasUnsavedChanges;
        private string _outputFolder = string.Empty;
        private string _overwritePolicyText = string.Empty;

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

        public string OutputFolder
        {
            get => _outputFolder;
            private set
            {
                if (_outputFolder != value)
                {
                    _outputFolder = value ?? string.Empty;
                    OnChanged(nameof(OutputFolder));
                }
            }
        }

        public string OverwritePolicyText
        {
            get => _overwritePolicyText;
            private set
            {
                if (_overwritePolicyText != value)
                {
                    _overwritePolicyText = value ?? string.Empty;
                    OnChanged(nameof(OverwritePolicyText));
                }
            }
        }

        public void ApplySettings(AppSettings settings)
        {
            OutputFolder = settings?.DefaultOutputDir ?? string.Empty;
            OverwritePolicyText = (settings?.DefaultOverwrite ?? false) ? "True" : "False";
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

        private void Reset()
        {
            SelectedWorkflowId = null;
            Name = string.Empty;
            WorkflowScope = string.Empty;
            Description = string.Empty;
            Pattern = string.Empty;
            SelectedSetName = null;
            SelectedPrintSet = null;
            SetDirty(true);
            RecomputeLocal();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
