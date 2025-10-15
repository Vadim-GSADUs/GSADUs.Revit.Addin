using System.ComponentModel;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class PdfWorkflowTabViewModel : WorkflowTabBaseViewModel
    {
        private string _pattern = "";
        private string? _selectedSetName;
        private string? _selectedPrintSet;
        private bool _isSaveEnabled;

        public string Pattern { get => _pattern; set { if (_pattern != value) { _pattern = value; OnChanged(nameof(Pattern)); RecomputeLocal(); } } }
        public string? SelectedSetName { get => _selectedSetName; set { if (_selectedSetName != value) { _selectedSetName = value; OnChanged(nameof(SelectedSetName)); RecomputeLocal(); } } }
        public string? SelectedPrintSet { get => _selectedPrintSet; set { if (_selectedPrintSet != value) { _selectedPrintSet = value; OnChanged(nameof(SelectedPrintSet)); RecomputeLocal(); } } }
        public bool IsSaveEnabled { get => _isSaveEnabled; private set { if (_isSaveEnabled != value) { _isSaveEnabled = value; OnChanged(nameof(IsSaveEnabled)); } } }

        private void RecomputeLocal()
        {
            var ok = !string.IsNullOrWhiteSpace(SelectedSetName)
                     && !string.IsNullOrWhiteSpace(SelectedPrintSet)
                     && !string.IsNullOrWhiteSpace(Pattern)
                     && Pattern.Contains("{SetName}")
                     && Pattern.EndsWith(".pdf", System.StringComparison.OrdinalIgnoreCase);
            IsSaveEnabled = ok && IsBaseSaveEnabled;
        }
    }
}
