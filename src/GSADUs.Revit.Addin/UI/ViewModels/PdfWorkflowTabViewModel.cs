using System;
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
                // Ensure SavePdfCommand reevaluates when any required field changes
                if (e.PropertyName == nameof(Name) || e.PropertyName == nameof(SelectedPrintSet) || e.PropertyName == nameof(SelectedSetName) || e.PropertyName == nameof(PdfPattern) || e.PropertyName == nameof(Description))
                {
                    _savePdfCommand.RaiseCanExecuteChanged();
                }
            };
            NewCommand = new DelegateCommand(_ => Reset());

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
                if (_selectedSetName != value)
                {
                    _selectedSetName = value;
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
                if (_selectedPrintSet != value)
                {
                    _selectedPrintSet = value;
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
                if (_pattern != value)
                {
                    _pattern = value;
                    OnChanged(nameof(Pattern));
                    OnChanged(nameof(PdfPattern));
                    _savePdfCommand.RaiseCanExecuteChanged();
                    HasUnsavedChanges = true;
                    OnChanged(nameof(HasUnsavedChanges));
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
        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                if (_outputFolder != value)
                {
                    _outputFolder = value ?? string.Empty;
                    OnChanged(nameof(OutputFolder));
                }
            }
        }
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
                }
            }
        }
        private string _preview = string.Empty;
        public string Preview
        {
            get => _preview;
            set { if (_preview != value) { _preview = value ?? string.Empty; OnChanged(nameof(Preview)); } }
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
            SaveCommand?.Execute(null);
        }

        private bool CanSavePdf()
        {
            return !string.IsNullOrWhiteSpace(Name)
                && !string.IsNullOrWhiteSpace(SelectedPrintSet)
                && !string.IsNullOrWhiteSpace(SelectedSetName)
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
