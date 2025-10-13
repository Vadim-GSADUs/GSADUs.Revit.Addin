using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    internal sealed class WorkflowRegistry : IWorkflowRegistry
    {
        private readonly List<IWorkflow> _workflows = new();
        public WorkflowRegistry(IEnumerable<IWorkflow> workflows)
        {
            if (workflows != null) _workflows.AddRange(workflows);
        }
        public IEnumerable<IWorkflow> All() => _workflows;
        public IWorkflow? Find(string id) => _workflows.FirstOrDefault(w => string.Equals(w.Id, id, System.StringComparison.OrdinalIgnoreCase));
    }

    internal sealed class BatchExportWorkflow : IWorkflow
    {
        private readonly IBatchRunCoordinator _coordinator;
        public BatchExportWorkflow(IBatchRunCoordinator coordinator)
        {
            _coordinator = coordinator;
        }
        public string Id => "batch-export";
        public string DisplayName => "Batch Export";
        public bool IsInternal => false;
        public Result Execute(UIApplication uiapp, UIDocument uidoc) => _coordinator.Run(uiapp, uidoc);
    }
}
