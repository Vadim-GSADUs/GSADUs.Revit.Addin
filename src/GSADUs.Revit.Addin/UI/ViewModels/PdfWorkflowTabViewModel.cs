using System.ComponentModel;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class PdfWorkflowTabViewModel : INotifyPropertyChanged
    {
        private string _pattern = "";
        private string? _selectedSetName;
        private string? _selectedPrintSet;
        private bool _isSaveEnabled;

        public string Pattern { get => _pattern; set { if (_pattern != value) { _pattern = value; OnChanged(nameof(Pattern)); Recompute(); } } }
        public string? SelectedSetName { get => _selectedSetName; set { if (_selectedSetName != value) { _selectedSetName = value; OnChanged(nameof(SelectedSetName)); Recompute(); } } }
        public string? SelectedPrintSet { get => _selectedPrintSet; set { if (_selectedPrintSet != value) { _selectedPrintSet = value; OnChanged(nameof(SelectedPrintSet)); Recompute(); } } }
        public bool IsSaveEnabled { get => _isSaveEnabled; private set { if (_isSaveEnabled != value) { _isSaveEnabled = value; OnChanged(nameof(IsSaveEnabled)); } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private void Recompute()
        {
            var hasSet = !string.IsNullOrWhiteSpace(SelectedSetName);
            var hasPrintSet = !string.IsNullOrWhiteSpace(SelectedPrintSet);
            var hasPattern = !string.IsNullOrWhiteSpace(Pattern) && Pattern.Contains("{SetName}") && Pattern.EndsWith(".pdf", System.StringComparison.OrdinalIgnoreCase);
            IsSaveEnabled = hasSet && hasPrintSet && hasPattern;
        }
    }
}
