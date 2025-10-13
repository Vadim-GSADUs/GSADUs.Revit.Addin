using System.Collections.Generic;

namespace GSADUs.Revit.Addin
{
    // Lightweight descriptor used by the UI and coordinator to compose a run.
    public interface IActionDescriptor
    {
        string Id { get; }
        string DisplayName { get; }
        int Order { get; } // lower first
        bool RequiresExternalClone { get; }
        bool DefaultSelected { get; }
    }

    public interface IActionRegistry
    {
        IEnumerable<IActionDescriptor> All();
        IActionDescriptor? Find(string id);
    }
}
