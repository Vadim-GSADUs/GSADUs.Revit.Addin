using GSADUs.Revit.Addin.Abstractions;
using GSADUs.Revit.Addin.Infrastructure;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    internal sealed class WorkflowPlanRegistry : IWorkflowPlanRegistry
    {
        private readonly IProjectSettingsProvider _settingsProvider;
        private readonly AppSettings _settings;

        public WorkflowPlanRegistry()
            : this(ServiceBootstrap.Provider.GetService(typeof(IProjectSettingsProvider)) as IProjectSettingsProvider
                   ?? new EsProjectSettingsProvider())
        {
        }

        public WorkflowPlanRegistry(IProjectSettingsProvider settingsProvider)
        {
            _settingsProvider = settingsProvider;
            _settings = _settingsProvider.Load();
        }

        public IEnumerable<WorkflowDefinition> All()
        {
            var workflows = _settings.Workflows ?? new List<WorkflowDefinition>();
            return workflows.OrderBy(w => w.Output).ThenBy(w => w.Order).ThenBy(w => w.Name);
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
