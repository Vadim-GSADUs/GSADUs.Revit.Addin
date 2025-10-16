using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class ImageWorkflowTabViewModel : WorkflowTabBaseViewModel, INotifyPropertyChanged
    {
        public ImageWorkflowTabViewModel()
        {
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IsBaseSaveEnabled)) Recompute();
                if (e.PropertyName == nameof(Name) || e.PropertyName == nameof(WorkflowScope) || e.PropertyName == nameof(Description))
                {
                    HasUnsavedChanges = true;
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                    Recompute();
                }
            };
            NewCommand = new DelegateCommand(_ => Reset());
        }

        public ObservableCollection<SavedWorkflowListItem> SavedWorkflows { get; } = new();

        public ICommand? PickWhitelistCommand { get; set; }
        public ICommand NewCommand { get; }
        public ICommand? SaveCommand { get; set; }

        public string? SelectedWorkflowId { get; set; }

        private string _whitelistSummary = string.Empty;
        public string WhitelistSummary
        {
            get => _whitelistSummary;
            set { if (_whitelistSummary != value) { _whitelistSummary = value ?? string.Empty; OnChanged(nameof(WhitelistSummary)); } }
        }

        public ObservableCollection<string> AvailablePrintSets { get; } = new();
        public ObservableCollection<SingleViewOption> AvailableSingleViews { get; } = new();

        private string _pattern = "{SetName}";
        public string Pattern
        {
            get => _pattern;
            set { if (_pattern != value) { _pattern = value ?? string.Empty; OnChanged(nameof(Pattern)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); Recompute(); } }
        }

        private string _exportScope = "PrintSet";
        public string ExportScope
        {
            get => _exportScope;
            set
            {
                if (_exportScope != value)
                {
                    _exportScope = value ?? "PrintSet";
                    OnChanged(nameof(ExportScope));
                    HasUnsavedChanges = true;
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                    // Clear the unselected dropdown
                    if (string.Equals(_exportScope, "SingleView", StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedPrintSet = null;
                    }
                    else
                    {
                        SelectedSingleViewId = null;
                    }
                    Recompute();
                }
            }
        }

        private string? _selectedPrintSet;
        public string? SelectedPrintSet
        {
            get => _selectedPrintSet;
            set { if (_selectedPrintSet != value) { _selectedPrintSet = value; OnChanged(nameof(SelectedPrintSet)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); Recompute(); } }
        }

        private string? _selectedSingleViewId;
        public string? SelectedSingleViewId
        {
            get => _selectedSingleViewId;
            set { if (_selectedSingleViewId != value) { _selectedSingleViewId = value; OnChanged(nameof(SelectedSingleViewId)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); Recompute(); } }
        }

        private string _resolution = "Medium";
        public string Resolution
        {
            get => _resolution;
            set { if (_resolution != value) { _resolution = value ?? "Medium"; OnChanged(nameof(Resolution)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); Recompute(); } }
        }

        private string _cropMode = "Static";
        public string CropMode
        {
            get => _cropMode;
            set { if (_cropMode != value) { _cropMode = value ?? "Static"; OnChanged(nameof(CropMode)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); Recompute(); } }
        }

        private string _cropOffset = string.Empty;
        public string CropOffset
        {
            get => _cropOffset;
            set { if (_cropOffset != value) { _cropOffset = value ?? string.Empty; OnChanged(nameof(CropOffset)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); Recompute(); } }
        }

        private string _format = "PNG";
        public string Format
        {
            get => _format;
            set { if (_format != value) { _format = value ?? "PNG"; OnChanged(nameof(Format)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); Recompute(); } }
        }

        private string _prefix = string.Empty;
        public string Prefix
        {
            get => _prefix;
            set { if (_prefix != value) { _prefix = value ?? string.Empty; OnChanged(nameof(Prefix)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); RecomputePreview(); } }
        }

        private string _suffix = string.Empty;
        public string Suffix
        {
            get => _suffix;
            set { if (_suffix != value) { _suffix = value ?? string.Empty; OnChanged(nameof(Suffix)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); RecomputePreview(); } }
        }

        private bool _isSaveEnabled;
        public bool IsSaveEnabled
        {
            get => _isSaveEnabled;
            private set { if (_isSaveEnabled != value) { _isSaveEnabled = value; OnChanged(nameof(IsSaveEnabled)); } }
        }

        private string _preview = string.Empty;
        public string Preview
        {
            get => _preview;
            private set { if (_preview != value) { _preview = value; OnChanged(nameof(Preview)); } }
        }

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set { if (_hasUnsavedChanges != value) { _hasUnsavedChanges = value; OnPropertyChanged(nameof(HasUnsavedChanges)); } }
        }

        public void SetDirty(bool dirty)
        {
            HasUnsavedChanges = dirty;
            OnPropertyChanged(nameof(HasUnsavedChanges));
            Recompute();
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Recompute()
        {
            var cropOk = true;
            if (string.Equals(CropMode, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(CropOffset))
                    cropOk = double.TryParse(CropOffset.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);
                else
                    cropOk = false;
            }
            var scopeOk = ExportScope == "SingleView" ? !string.IsNullOrWhiteSpace(SelectedSingleViewId)
                                                       : !string.IsNullOrWhiteSpace(SelectedPrintSet);
            var patOk = !string.IsNullOrWhiteSpace(Pattern) && Pattern.Contains("{SetName}");
            var fmtOk = !string.IsNullOrWhiteSpace(Format);
            var resOk = !string.IsNullOrWhiteSpace(Resolution);
            var nameOk = !string.IsNullOrWhiteSpace(Name);
            IsSaveEnabled = IsBaseSaveEnabled && nameOk && patOk && fmtOk && resOk && cropOk && scopeOk;
            RecomputePreview();
        }

        private static string MapFormatToExt(string? fmt)
        {
            return (fmt ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "BMP" => ".bmp",
                "TIFF" => ".tiff",
                _ => ".png"
            };
        }

        private void RecomputePreview()
        {
            var core = (Pattern ?? "{SetName}").Trim();
            if (string.IsNullOrWhiteSpace(core)) core = "{SetName}";
            try { core = System.IO.Path.GetFileNameWithoutExtension(core); } catch { }
            var ext = MapFormatToExt(Format);
            Preview = $"Preview: {Prefix}{core}{Suffix}{ext}";
        }

        private void Reset()
        {
            SelectedWorkflowId = null;
            Name = string.Empty;
            WorkflowScope = string.Empty;
            Description = string.Empty;
            Pattern = "{SetName}";
            Prefix = string.Empty;
            Suffix = string.Empty;
            Format = "PNG";
            Resolution = "Medium";
            CropMode = "Static";
            CropOffset = string.Empty;
            ExportScope = "PrintSet";
            SelectedPrintSet = null;
            SelectedSingleViewId = null;
            SetDirty(true);
            Recompute();
        }
    }

    internal sealed class SingleViewOption
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
