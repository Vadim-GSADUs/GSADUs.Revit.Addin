using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class WorkflowManagerViewModel : INotifyPropertyChanged
    {
        private readonly WorkflowCatalogService _catalog;
        private readonly WorkflowManagerPresenter _presenter;

        public WorkflowManagerViewModel(WorkflowCatalogService catalog, WorkflowManagerPresenter presenter)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
            OpenWorkflowCommand = new DelegateCommand(p => OpenWorkflow(p as WorkflowDefinition));

            DuplicateSelectedCommand = new DelegateCommand(_ => DuplicateSelected(), _ => SelectedWorkflow != null);
            RenameSelectedCommand = new DelegateCommand(_ => RenameSelected(), _ => SelectedWorkflow != null);
            DeleteSelectedCommand = new DelegateCommand(_ => DeleteSelected(), _ => SelectedWorkflow != null);
        }

        public ObservableCollection<WorkflowDefinition> Workflows => _catalog.Workflows;

        private WorkflowDefinition? _selectedWorkflow;
        public WorkflowDefinition? SelectedWorkflow
        {
            get => _selectedWorkflow;
            set
            {
                if (!ReferenceEquals(_selectedWorkflow, value))
                {
                    _selectedWorkflow = value;
                    OnPropertyChanged(nameof(SelectedWorkflow));
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { if (_selectedTabIndex != value) { _selectedTabIndex = value; OnPropertyChanged(nameof(SelectedTabIndex)); } }
        }

        public ICommand OpenWorkflowCommand { get; }
        public ICommand DuplicateSelectedCommand { get; }
        public ICommand RenameSelectedCommand { get; }
        public ICommand DeleteSelectedCommand { get; }

        public PdfWorkflowTabViewModel Pdf => _presenter.PdfWorkflow;
        public ImageWorkflowTabViewModel Image => _presenter.ImageWorkflow;

        private void OpenWorkflow(WorkflowDefinition? wf)
        {
            if (wf == null) wf = SelectedWorkflow;
            if (wf == null) return;

            switch (wf.Output)
            {
                case OutputType.Pdf:
                    SelectedTabIndex = 1;
                    _presenter.PdfWorkflow.SelectedWorkflowId = wf.Id;
                    break;
                case OutputType.Image:
                    SelectedTabIndex = 2;
                    _presenter.ImageWorkflow.SelectedWorkflowId = wf.Id;
                    break;
                default:
                    SelectedTabIndex = 0;
                    break;
            }
        }

        private void DuplicateSelected()
        {
            if (SelectedWorkflow == null) return;
            var clone = _catalog.Duplicate(SelectedWorkflow.Id);
            if (clone != null) SelectedWorkflow = clone;
        }

        private void RenameSelected()
        {
            if (SelectedWorkflow == null) return;
            var current = SelectedWorkflow.Name ?? string.Empty;
            var newName = string.IsNullOrWhiteSpace(current) ? "Workflow" : current + " (Renamed)";
            _catalog.Rename(SelectedWorkflow.Id, newName);
            // Refresh selection to updated object
            SelectedWorkflow = _catalog.Find(SelectedWorkflow.Id);
        }

        private void DeleteSelected()
        {
            if (SelectedWorkflow == null) return;
            var id = SelectedWorkflow.Id;
            // Delegate deletion to presenter so it can cascade updates across tabs
            _presenter.DeleteWorkflow(id);
            SelectedWorkflow = null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
