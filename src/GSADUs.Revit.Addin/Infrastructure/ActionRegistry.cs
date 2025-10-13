using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    internal sealed class ActionDescriptor : IActionDescriptor
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public int Order { get; init; }
        public bool RequiresExternalClone { get; init; }
        public bool DefaultSelected { get; init; }
    }

    internal sealed class ActionRegistry : IActionRegistry
    {
        private readonly List<IActionDescriptor> _actions = new();
        public ActionRegistry()
        {
            // Populate with defaults that mirror existing pipeline
            _actions.Add(new ActionDescriptor { Id = "export-rvt", DisplayName = "Export RVT", Order = 100, RequiresExternalClone = true, DefaultSelected = true });
            _actions.Add(new ActionDescriptor { Id = "open-dryrun", DisplayName = "Open Clone (DryRun)", Order = 150, RequiresExternalClone = true, DefaultSelected = false });
            _actions.Add(new ActionDescriptor { Id = "cleanup", DisplayName = "Cleanup Exports", Order = 300, RequiresExternalClone = true, DefaultSelected = false });

            // In-place export actions (non-destructive)
            _actions.Add(new ActionDescriptor { Id = "export-pdf", DisplayName = "Export PDF (in-place)", Order = 500, RequiresExternalClone = false, DefaultSelected = false });
            _actions.Add(new ActionDescriptor { Id = "export-image", DisplayName = "Export Image (in-place)", Order = 600, RequiresExternalClone = false, DefaultSelected = false });
            _actions.Add(new ActionDescriptor { Id = "backup-cleanup", DisplayName = "Backup Cleanup", Order = 900, RequiresExternalClone = false, DefaultSelected = true });
            _actions.Add(new ActionDescriptor { Id = "resave-rvt", DisplayName = "Resave RVT", Order = 800, RequiresExternalClone = true, DefaultSelected = true });
        }
        public IEnumerable<IActionDescriptor> All() => _actions.OrderBy(a => a.Order);
        public IActionDescriptor? Find(string id) => _actions.FirstOrDefault(a => string.Equals(a.Id, id, System.StringComparison.OrdinalIgnoreCase));
    }
}
