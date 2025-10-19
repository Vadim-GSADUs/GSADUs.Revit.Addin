using System.ComponentModel;
using System.Collections.ObjectModel;

namespace GSADUs.Revit.Addin.UI
{
    // Base VM for common fields across tabs: Name, Scope, Description
    internal class WorkflowTabBaseViewModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _workflowScope = string.Empty;
        private string _description = string.Empty;
        private bool _isBaseSaveEnabled;

        public ObservableCollection<string> Scopes { get; } = new();

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value ?? string.Empty; OnChanged(nameof(Name)); Recompute(); } }
        }

        // Avoid conflict with Image tab's own export scope property named "Scope"
        public string WorkflowScope
        {
            get => _workflowScope;
            set { if (_workflowScope != value) { _workflowScope = value ?? string.Empty; OnChanged(nameof(WorkflowScope)); Recompute(); } }
        }

        public string Description
        {
            get => _description;
            set { if (_description != value) { _description = value ?? string.Empty; OnChanged(nameof(Description)); } }
        }

        public bool IsBaseSaveEnabled
        {
            get => _isBaseSaveEnabled;
            private set { if (_isBaseSaveEnabled != value) { _isBaseSaveEnabled = value; OnChanged(nameof(IsBaseSaveEnabled)); } }
        }

        protected virtual void OnChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Recompute()
        {
            IsBaseSaveEnabled = !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(WorkflowScope);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
