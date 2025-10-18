using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class ImageWorkflowTabViewModel : WorkflowTabBaseViewModel, INotifyPropertyChanged, IDataErrorInfo
    {
        private readonly DelegateCommand _saveImageCommand;

        public ImageWorkflowTabViewModel()
        {
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Name)
                    || e.PropertyName == nameof(ExportScope)
                    || e.PropertyName == nameof(SelectedPrintSet)
                    || e.PropertyName == nameof(SelectedSingleViewId)
                    || e.PropertyName == nameof(Resolution)
                    || e.PropertyName == nameof(CropMode)
                    || e.PropertyName == nameof(Format)
                    || e.PropertyName == nameof(Pattern)
                    || e.PropertyName == nameof(ImagePattern)
                    || e.PropertyName == nameof(IsBaseSaveEnabled))
                {
                    _saveImageCommand.RaiseCanExecuteChanged();
                }
                if (e.PropertyName == nameof(IsBaseSaveEnabled)) Recompute();
                if (e.PropertyName == nameof(Name) || e.PropertyName == nameof(WorkflowScope) || e.PropertyName == nameof(Description))
                {
                    HasUnsavedChanges = true;
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                    Recompute();
                    _saveImageCommand.RaiseCanExecuteChanged();
                }
            };
            NewCommand = new DelegateCommand(_ => Reset());

            _saveImageCommand = new DelegateCommand(
                _ => SaveCommand?.Execute(null),
                _ => CanSaveImage());
        }

        public ObservableCollection<SavedWorkflowListItem> SavedWorkflows { get; } = new();

        public ICommand? PickWhitelistCommand { get; set; }
        public ICommand NewCommand { get; }
        public ICommand? SaveCommand { get; set; }
        public ICommand SaveImageCommand => _saveImageCommand;

        public string? SelectedWorkflowId { get; set; }

        private string _whitelistSummary = string.Empty;
        public string WhitelistSummary
        {
            get => _whitelistSummary;
            set { if (_whitelistSummary != value) { _whitelistSummary = value ?? string.Empty; OnChanged(nameof(WhitelistSummary)); } }
        }

        public ObservableCollection<string> AvailablePrintSets { get; } = new();
        public ObservableCollection<SingleViewOption> AvailableSingleViews { get; } = new();

        // New list of images and selection
        public ObservableCollection<string> ImageFiles { get; } = new();

        private string? _selectedImage;
        public string? SelectedImage
        {
            get => _selectedImage;
            set
            {
                if (_selectedImage != value)
                {
                    _selectedImage = value;
                    OnChanged(nameof(SelectedImage));
                    _saveImageCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _imageEnabled = true;
        public bool ImageEnabled
        {
            get => _imageEnabled;
            set
            {
                if (_imageEnabled != value)
                {
                    _imageEnabled = value;
                    OnChanged(nameof(ImageEnabled));
                    _saveImageCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private string _pattern = "{SetName}";
        public string Pattern
        {
            get => _pattern;
            set { if (_pattern != value) { _pattern = value ?? string.Empty; OnChanged(nameof(Pattern)); OnChanged(nameof(ImagePattern)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); RecomputePreview(); _saveImageCommand.RaiseCanExecuteChanged(); } }
        }

        // Alias for binding
        public string ImagePattern
        {
            get => Pattern;
            set => Pattern = value;
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
                        SelectedPrintSet = null;
                    else
                        SelectedSingleViewId = null;
                    Recompute();
                    _saveImageCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private string? _selectedPrintSet;
        public string? SelectedPrintSet
        {
            get => _selectedPrintSet;
            set { if (_selectedPrintSet != value) { _selectedPrintSet = value; OnChanged(nameof(SelectedPrintSet)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); Recompute(); _saveImageCommand.RaiseCanExecuteChanged(); } }
        }

        private string? _selectedSingleViewId;
        public string? SelectedSingleViewId
        {
            get => _selectedSingleViewId;
            set { if (_selectedSingleViewId != value) { _selectedSingleViewId = value; OnChanged(nameof(SelectedSingleViewId)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); Recompute(); _saveImageCommand.RaiseCanExecuteChanged(); } }
        }

        private string _resolution = "Medium";
        public string Resolution
        {
            get => _resolution;
            set { if (_resolution != value) { _resolution = value ?? "Medium"; OnChanged(nameof(Resolution)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); RecomputePreview(); _saveImageCommand.RaiseCanExecuteChanged(); } }
        }

        private string _cropMode = "Static";
        public string CropMode
        {
            get => _cropMode;
            set { if (_cropMode != value) { _cropMode = value ?? "Static"; OnChanged(nameof(CropMode)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); RecomputePreview(); _saveImageCommand.RaiseCanExecuteChanged(); } }
        }

        private string _cropOffset = string.Empty;
        public string CropOffset
        {
            get => _cropOffset;
            set { if (_cropOffset != value) { _cropOffset = value ?? string.Empty; OnChanged(nameof(CropOffset)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); RecomputePreview(); } }
        }

        private string _format = "PNG";
        public string Format
        {
            get => _format;
            set { if (_format != value) { _format = value ?? "PNG"; OnChanged(nameof(Format)); HasUnsavedChanges = true; OnPropertyChanged(nameof(HasUnsavedChanges)); RecomputePreview(); _saveImageCommand.RaiseCanExecuteChanged(); } }
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

        // Computed property for the image tab preview text
        public string ImagePreviewText => Preview;

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
            _saveImageCommand.RaiseCanExecuteChanged();
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Recompute()
        {
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
            Preview = $"Preview: {core}{ext}";
        }

        public void Reset()
        {
            SelectedWorkflowId = null;
            Name = string.Empty;
            WorkflowScope = string.Empty;
            Description = string.Empty;
            Pattern = "{SetName}";
            Format = "PNG";
            Resolution = "Medium";
            CropMode = "Static";
            CropOffset = string.Empty;
            ExportScope = "PrintSet";
            SelectedPrintSet = null;
            SelectedSingleViewId = null;
            SelectedImage = null;
            SetDirty(true);
            Recompute();
            _saveImageCommand.RaiseCanExecuteChanged();
        }

        private bool CanSaveImage()
        {
            var nameOk = !string.IsNullOrWhiteSpace(Name);

            var scopeIsPrintSet = string.Equals(ExportScope, "PrintSet");
            var scopeIsSingleView = string.Equals(ExportScope, "SingleView");
            var scopeOk = scopeIsPrintSet || scopeIsSingleView;

            var rangeOk =
                (scopeIsPrintSet && !string.IsNullOrWhiteSpace(SelectedPrintSet)) ||
                (scopeIsSingleView && !string.IsNullOrWhiteSpace(SelectedSingleViewId));

            var resOk = !string.IsNullOrWhiteSpace(Resolution);
            var cropOk = !string.IsNullOrWhiteSpace(CropMode);
            var fmtOk  = !string.IsNullOrWhiteSpace(Format);
            var patOk  = IsValidPattern(ImagePattern);

            return IsBaseSaveEnabled && nameOk && scopeOk && rangeOk && resOk && cropOk && fmtOk && patOk;
        }

        public bool IsValidPattern(string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            return pattern.Contains("{SetName}");
        }

        // IDataErrorInfo for pattern validation feedback
        public string Error => string.Empty;
        public string this[string columnName]
        {
            get
            {
                if (columnName == nameof(ImagePattern) || columnName == nameof(Pattern))
                    return IsValidPattern(ImagePattern) ? string.Empty : "Pattern must include {SetName}";

                if (columnName == nameof(Name))
                    return string.IsNullOrWhiteSpace(Name) ? "Name is required" : string.Empty;

                if (columnName == nameof(ExportScope))
                    return (ExportScope == "PrintSet" || ExportScope == "SingleView") ? string.Empty : "Export scope must be PrintSet or SingleView";

                if (columnName == nameof(SelectedPrintSet) && string.Equals(ExportScope, "PrintSet"))
                    return string.IsNullOrWhiteSpace(SelectedPrintSet) ? "Select a Print Set" : string.Empty;

                if (columnName == nameof(SelectedSingleViewId) && string.Equals(ExportScope, "SingleView"))
                    return string.IsNullOrWhiteSpace(SelectedSingleViewId) ? "Select a Single View" : string.Empty;

                if (columnName == nameof(Resolution))
                    return string.IsNullOrWhiteSpace(Resolution) ? "Select a resolution" : string.Empty;

                if (columnName == nameof(CropMode))
                    return string.IsNullOrWhiteSpace(CropMode) ? "Select a crop mode" : string.Empty;

                if (columnName == nameof(Format))
                    return string.IsNullOrWhiteSpace(Format) ? "Select an image format" : string.Empty;

                return string.Empty;
            }
        }
    }

    internal sealed class SingleViewOption
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
