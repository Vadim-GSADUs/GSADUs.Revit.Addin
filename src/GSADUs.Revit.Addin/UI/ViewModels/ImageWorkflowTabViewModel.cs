using System.Collections.ObjectModel;
using System.ComponentModel;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class ImageWorkflowTabViewModel : WorkflowTabBaseViewModel
    {
        private string _pattern = ""; // extensionless
        private string? _selectedSetName;
        private string? _selectedPrintSet;
        private string _scope = "CurrentSet"; // or "AllViews"
        private string _resolution = "High"; // Low/Medium/High/Ultra
        private bool _isSaveEnabled;

        public ObservableCollection<string> AvailableViewSets { get; } = new();
        public ObservableCollection<string> AvailablePrintSets { get; } = new();

        public string Pattern { get => _pattern; set { if (_pattern != value) { _pattern = value; OnChanged(nameof(Pattern)); Recompute(); } } }
        public string? SelectedSetName { get => _selectedSetName; set { if (_selectedSetName != value) { _selectedSetName = value; OnChanged(nameof(SelectedSetName)); Recompute(); } } }
        public string? SelectedPrintSet { get => _selectedPrintSet; set { if (_selectedPrintSet != value) { _selectedPrintSet = value; OnChanged(nameof(SelectedPrintSet)); Recompute(); } } }
        public string Scope { get => _scope; set { if (_scope != value) { _scope = value; OnChanged(nameof(Scope)); Recompute(); } } }
        public string Resolution { get => _resolution; set { if (_resolution != value) { _resolution = value; OnChanged(nameof(Resolution)); Recompute(); } } }
        public bool IsSaveEnabled { get => _isSaveEnabled; private set { if (_isSaveEnabled != value) { _isSaveEnabled = value; OnChanged(nameof(IsSaveEnabled)); } } }

        private void Recompute()
        {
            var hasSet = !string.IsNullOrWhiteSpace(SelectedSetName);
            var hasPrintSet = !string.IsNullOrWhiteSpace(SelectedPrintSet);
            var hasPattern = !string.IsNullOrWhiteSpace(Pattern) && Pattern.Contains("{SetName}"); // extensionless by design
            IsSaveEnabled = hasSet && hasPrintSet && hasPattern && IsBaseSaveEnabled;
        }
    }
}
