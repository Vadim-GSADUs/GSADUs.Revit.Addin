using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace GSADUs.Revit.Addin.UI
{
    // Phase 2 scaffold only: not wired to XAML yet
    internal sealed class BatchExportWindowViewModel : INotifyPropertyChanged
    {
        private string _outputDir = string.Empty;
        private bool _dryRun;

        public string OutputDir { get => _outputDir; set { if (_outputDir != value) { _outputDir = value; OnPropertyChanged(nameof(OutputDir)); } } }
        public bool DryRun { get => _dryRun; set { if (_dryRun != value) { _dryRun = value; OnPropertyChanged(nameof(DryRun)); } } }

        public ObservableCollection<string> AllSetNames { get; } = new();
        public ObservableCollection<string> SelectedSetNames { get; } = new();

        public ObservableCollection<(string Id, string DisplayName, bool IsSelected)> Actions { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public List<string> GetSelectedActionIds() => Actions.Where(a => a.IsSelected).Select(a => a.Id).ToList();
    }
}
