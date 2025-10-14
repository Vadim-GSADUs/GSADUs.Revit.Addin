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
            // Removed RVT-related actions: export-rvt, open-dryrun, cleanup, backup-cleanup, resave-rvt
            // Only PDF and Image actions remain
            _actions.Add(new ActionDescriptor { Id = "export-pdf", DisplayName = "Export PDF (in-place)", Order = 100, RequiresExternalClone = false, DefaultSelected = false });
            _actions.Add(new ActionDescriptor { Id = "export-image", DisplayName = "Export Image (in-place)", Order = 200, RequiresExternalClone = false, DefaultSelected = false });
        }
        public IEnumerable<IActionDescriptor> All() => _actions.OrderBy(a => a.Order);
        public IActionDescriptor? Find(string id) => _actions.FirstOrDefault(a => string.Equals(a.Id, id, System.StringComparison.OrdinalIgnoreCase));
    }
}
