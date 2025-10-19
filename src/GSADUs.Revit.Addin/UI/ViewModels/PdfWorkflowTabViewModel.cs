using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class PdfWorkflowTabViewModel : WorkflowTabBaseViewModel, IDataErrorInfo
    {
        private readonly DelegateCommand _savePdfCommand;

        // Event consumers (e.g., presenter) can subscribe to be notified when Save is requested
        public event EventHandler? SaveRequested;

        public PdfWorkflowTabViewModel()
        {
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IsBaseSaveEnabled))
                {
                    RecomputeLocal();
                    _savePdfCommand.RaiseCanExecuteChanged();
                }
                if (e.PropertyName == nameof(Name) || e.PropertyName == nameof(WorkflowScope) || e.PropertyName == nameof(Description))
                {
                    HasUnsavedChanges = true;
                    OnChanged(nameof(HasUnsavedChanges));
                    RecomputeLocal();
                }
                // Ensure SavePdfCommand reevaluates when any required field changes
                if (e.PropertyName == nameof(Name)
                    || e.PropertyName == nameof(WorkflowScope)
                    || e.PropertyName == nameof(SelectedPrintSet)
                    || e.PropertyName == nameof(SelectedSetName)
                    || e.PropertyName == nameof(PdfPattern)
                    || e.PropertyName == nameof(Description))
                {
                    _savePdfCommand.RaiseCanExecuteChanged();
                }
            };
            NewCommand = new DelegateCommand(_ => Reset());

            // Ensure command exists and reflects CanSavePdf()
            _savePdfCommand = new DelegateCommand(_ => SavePdf(), _ => CanSavePdf());
        }

        public ICommand NewCommand { get; }
        public ICommand? ManagePdfSetupCommand { get; set; }
        public ICommand? SaveCommand { get; set; }
        public ICommand SavePdfCommand => _savePdfCommand;

        public ObservableCollection<SavedWorkflowListItem> SavedWorkflows { get; } = new();

        // Required for presenter and workflow selection
        private string? _selectedWorkflowId;
        public string? SelectedWorkflowId
        {
            get => _selectedWorkflowId;
            set { if (_selectedWorkflowId != value) { _selectedWorkflowId = value; OnChanged(nameof(SelectedWorkflowId)); } }
        }

        private string? _selectedSetName;
        public string? SelectedSetName
        {
            get => _selectedSetName;
            set
            {
                var trimmed = value?.Trim();
                if (_selectedSetName != trimmed)
                {
                    _selectedSetName = trimmed;
                    OnChanged(nameof(SelectedSetName));
                    _savePdfCommand.RaiseCanExecuteChanged();
                    HasUnsavedChanges = true;
                    OnChanged(nameof(HasUnsavedChanges));
                    RecomputeLocal();
                }
            }
        }
        private string? _selectedPrintSet;
        public string? SelectedPrintSet
        {
            get => _selectedPrintSet;
            set
            {
                var trimmed = value?.Trim();
                if (_selectedPrintSet != trimmed)
                {
                    _selectedPrintSet = trimmed;
                    OnChanged(nameof(SelectedPrintSet));
                    _savePdfCommand.RaiseCanExecuteChanged();
                    HasUnsavedChanges = true;
                    OnChanged(nameof(HasUnsavedChanges));
                    RecomputeLocal();
                }
            }
        }
        private string _pattern = "{SetName}.pdf";
        public string Pattern
        {
            get => _pattern;
            set
            {
                var v = (value ?? string.Empty).Trim();
                if (_pattern != v)
                {
                    _pattern = v;
                    OnChanged(nameof(Pattern));
                    OnChanged(nameof(PdfPattern));
                    // Update preview immediately and re-evaluate save
                    RecomputePreview();
                    OnChanged(nameof(Preview));
                    HasUnsavedChanges = true;
                    OnChanged(nameof(HasUnsavedChanges));
                    _savePdfCommand.RaiseCanExecuteChanged();
                    RecomputeLocal();
                }
            }
        }
        public string PdfPattern
        {
            get => Pattern;
            set => Pattern = value;
        }

        // Required for presenter and UI
        private bool _isSaveEnabled;
        public bool IsSaveEnabled
        {
            get => _isSaveEnabled;
            set
            {
                if (_isSaveEnabled != value)
                {
                    _isSaveEnabled = value;
                    OnChanged(nameof(IsSaveEnabled));
                }
            }
        }
        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                if (_hasUnsavedChanges != value)
                {
                    _hasUnsavedChanges = value;
                    OnChanged(nameof(HasUnsavedChanges));
                }
            }
        }
        private string _outputFolder = string.Empty;
        public string OutputFolder => _outputFolder; // read-only for binding

        private string _overwritePolicyText = string.Empty;
        public string OverwritePolicyText
        {
            get => _overwritePolicyText;
            set
            {
                if (_overwritePolicyText != value)
                {
                    _overwritePolicyText = value ?? string.Empty;
                    OnChanged(nameof(OverwritePolicyText));
                    OnChanged(nameof(OverwritePolicy)); // keep alias in sync
                }
            }
        }
        // New read-only alias property for binding
        public string OverwritePolicy => _overwritePolicyText;

        private string _preview = string.Empty;
        public string Preview
        {
            get => _preview;
            set { if (_preview != value) { _preview = value ?? string.Empty; OnChanged(nameof(Preview)); } }
        }

        public void ApplySettings(AppSettings settings)
        {
            // Initialize from settings without exposing setters
            var newOutput = settings?.DefaultOutputDir ?? string.Empty;
            if (!string.Equals(_outputFolder, newOutput, StringComparison.Ordinal))
            {
                _outputFolder = newOutput;
                OnChanged(nameof(OutputFolder));
            }

            var policy = (settings?.DefaultOverwrite ?? false) ? "True" : "False";
            if (!string.Equals(_overwritePolicyText, policy, StringComparison.Ordinal))
            {
                _overwritePolicyText = policy;
                OnChanged(nameof(OverwritePolicyText));
                OnChanged(nameof(OverwritePolicy));
            }
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

        public void Reset()
        {
            SelectedWorkflowId = null;
            Name = string.Empty;
            WorkflowScope = string.Empty;
            Description = string.Empty;
            Pattern = "{SetName}.pdf";
            SelectedSetName = null;
            SelectedPrintSet = null;
            SetDirty(true);
            RecomputeLocal();
            _savePdfCommand.RaiseCanExecuteChanged();
        }

        private void SavePdf()
        {
            // Maintain existing route via SaveCommand for backcompat
            SaveCommand?.Execute(null);
            // Also raise event for optional presenter subscription
            SaveRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool CanSavePdf()
        {
            return IsBaseSaveEnabled
                && !string.IsNullOrWhiteSpace(Name)
                && !string.IsNullOrWhiteSpace(SelectedSetName)
                && !string.IsNullOrWhiteSpace(SelectedPrintSet)
                && !string.IsNullOrWhiteSpace(PdfPattern)
                && PdfPattern.Contains("{SetName}");
        }

        public bool IsValidPattern(string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
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
