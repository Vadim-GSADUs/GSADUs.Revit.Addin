using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    internal sealed class WorkflowPlanRegistry : IWorkflowPlanRegistry
    {
        private readonly AppSettings _settings;
        public WorkflowPlanRegistry() : this(AppSettingsStore.Load()) { }
        public WorkflowPlanRegistry(AppSettings settings) { _settings = settings; }

        public IEnumerable<WorkflowDefinition> All()
        {
            var s = _settings ?? AppSettingsStore.Load();
            return (s.Workflows ?? new List<WorkflowDefinition>()).OrderBy(w => w.Output).ThenBy(w => w.Order).ThenBy(w => w.Name);
        }

        public IEnumerable<WorkflowDefinition> Selected(AppSettings settings)
        {
            var selected = new HashSet<string>(settings.SelectedWorkflowIds ?? new List<string>(), System.StringComparer.OrdinalIgnoreCase);
            return (settings.Workflows ?? new List<WorkflowDefinition>()).Where(w => selected.Contains(w.Id));
        }

        public WorkflowDefinition? Find(string id)
        {
            return (_settings.Workflows ?? new List<WorkflowDefinition>()).FirstOrDefault(w => string.Equals(w.Id, id, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
