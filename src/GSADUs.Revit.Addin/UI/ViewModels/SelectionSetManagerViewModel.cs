using System.Collections.ObjectModel;
using System.ComponentModel;

namespace GSADUs.Revit.Addin.UI
{
    // Phase 2 scaffold only: not wired to XAML yet
    internal sealed class SelectionSetManagerViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<Row> Rows { get; } = new();
        private string _summary = string.Empty;
        public string Summary { get => _summary; set { if (_summary != value) { _summary = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary))); } } }

        public event PropertyChangedEventHandler? PropertyChanged;

        internal sealed class Row : INotifyPropertyChanged
        {
            private string _setName = string.Empty;
            private string _editName = string.Empty;
            private bool _isEditing;

            public string SetName { get => _setName; set { if (_setName != value) { _setName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SetName))); } } }
            public string EditName { get => _editName; set { if (_editName != value) { _editName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditName))); } } }
            public bool IsEditing { get => _isEditing; set { if (_isEditing != value) { _isEditing = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing))); } } }

            public int Before { get; set; }
            public int Added { get; set; }
            public int Removed { get; set; }
            public int After { get; set; }
            public string Ambiguous { get; set; } = string.Empty;
            public string Details { get; set; } = string.Empty;

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
