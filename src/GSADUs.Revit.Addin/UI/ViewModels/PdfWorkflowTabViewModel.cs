using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class PdfWorkflowTabViewModel : WorkflowTabBaseViewModel, IDataErrorInfo
    {
        private readonly DelegateCommand _savePdfCommand;

        public PdfWorkflowTabViewModel()
        {
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IsBaseSaveEnabled)) RecomputeLocal();
                if (e.PropertyName == nameof(Name) || e.PropertyName == nameof(WorkflowScope) || e.PropertyName == nameof(Description))
                {
                    HasUnsavedChanges = true;
                    OnChanged(nameof(HasUnsavedChanges));
                    RecomputeLocal();
                }
                // Ensure SavePdfCommand reevaluates when relevant base fields might affect preview or validation
                if (e.PropertyName == nameof(Name) || e.PropertyName == nameof(WorkflowScope) || e.PropertyName == nameof(Description))
                {
                    _savePdfCommand.RaiseCanExecuteChanged();
                }
            };
            NewCommand = new DelegateCommand(_ => Reset());

            _savePdfCommand = new DelegateCommand(
                _ => SaveCommand?.Execute(null),
                _ => CanSavePdf());
        }

        public ICommand NewCommand { get; }
        public ICommand? ManagePdfSetupCommand { get; set; }
        public ICommand? SaveCommand { get; set; }
        public ICommand SavePdfCommand => _savePdfCommand;

        public ObservableCollection<SavedWorkflowListItem> SavedWorkflows { get; } = new();

        // New ListBox backing: items and selection
        public ObservableCollection<string> PdfFiles { get; } = new();

        private string? _selectedPdf;
        public string? SelectedPdf
        {
            get => _selectedPdf;
            set
            {
                if (_selectedPdf != value)
                {
                    _selectedPdf = value;
                    OnChanged(nameof(SelectedPdf));
                    _savePdfCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isPdfEnabled = true;
        public bool IsPdfEnabled
        {
            get => _isPdfEnabled;
            set
            {
                if (_isPdfEnabled != value)
                {
                    _isPdfEnabled = value;
                    OnChanged(nameof(IsPdfEnabled));
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    _savePdfCommand.RaiseCanExecuteChanged();
                    // Keep PdfEnabled alias in sync
                    OnChanged(nameof(PdfEnabled));
                }
            }
        }

        // Alias for binding compatibility with updated XAML
        public bool PdfEnabled
        {
            get => IsPdfEnabled;
            set => IsPdfEnabled = value;
        }

        private string? _selectedWorkflowId;
        public string? SelectedWorkflowId
        {
            get => _selectedWorkflowId;
            set { if (_selectedWorkflowId != value) { _selectedWorkflowId = value; OnChanged(nameof(SelectedWorkflowId)); } }
        }

        private string _pattern = "{SetName}.pdf";
        private string? _selectedSetName;
        private string? _selectedPrintSet;
        private bool _isSaveEnabled;
        private bool _hasUnsavedChanges;
        private string _outputFolder = string.Empty;
        private string _overwritePolicyText = string.Empty;
        private string _preview = string.Empty;

        public string Pattern
        {
            get => _pattern;
            set
            {
                if (_pattern != value)
                {
                    _pattern = value;
                    OnChanged(nameof(Pattern));
                    // keep alias property in sync
                    OnChanged(nameof(PdfPattern));
                    HasUnsavedChanges = true;
                    OnChanged(nameof(HasUnsavedChanges));
                    RecomputeLocal();
                    _savePdfCommand.RaiseCanExecuteChanged();
                }
            }
        }

        // Alias used by XAML for validation-friendly binding name
        public string PdfPattern
        {
            get => Pattern;
            set => Pattern = value;
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
                    OnChanged(nameof(HasUnsavedChanges));
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
                    OnChanged(nameof(HasUnsavedChanges));
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
                    OnChanged(nameof(HasUnsavedChanges));
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

        public string Preview
        {
            get => _preview;
            private set { if (_preview != value) { _preview = value ?? string.Empty; OnChanged(nameof(Preview)); } }
        }

        public void ApplySettings(AppSettings settings)
        {
            OutputFolder = settings?.DefaultOutputDir ?? string.Empty;
            OverwritePolicyText = (settings?.DefaultOverwrite ?? false) ? "True" : "False";
        }

        public void SetDirty(bool dirty)
        {
            HasUnsavedChanges = dirty;
            OnChanged(nameof(HasUnsavedChanges));
            RecomputeLocal();
            _savePdfCommand.RaiseCanExecuteChanged();
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
            RecomputePreview();
        }

        private void RecomputePreview()
        {
            var pat = (Pattern ?? "{SetName}.pdf").Trim();
            if (string.IsNullOrWhiteSpace(pat)) pat = "{SetName}.pdf";
            if (!pat.EndsWith(".pdf", System.StringComparison.OrdinalIgnoreCase)) pat += ".pdf";
            try { pat = System.IO.Path.GetFileName(pat); } catch { }
            Preview = $"Preview: {pat}";
        }

        private void Reset()
        {
            SelectedWorkflowId = null;
            Name = string.Empty;
            WorkflowScope = string.Empty;
            Description = string.Empty;
            Pattern = "{SetName}.pdf";
            SelectedSetName = null;
            SelectedPrintSet = null;
            SelectedPdf = null;
            SetDirty(true);
            RecomputeLocal();
            _savePdfCommand.RaiseCanExecuteChanged();
        }

        private bool CanSavePdf()
        {
            return PdfEnabled && SelectedPdf != null && IsValidPattern(PdfPattern);
        }

        public bool IsValidPattern(string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            // Require SetName token for PDF export naming
            return pattern.Contains("{SetName}");
        }

        // IDataErrorInfo implementation for inline validation feedback
        public string Error => string.Empty;
        public string this[string columnName]
        {
            get
            {
                if (columnName == nameof(PdfPattern))
                {
                    return IsValidPattern(PdfPattern) ? string.Empty : "Pattern must include {SetName}";
                }
                return string.Empty;
            }
        }
    }
}
