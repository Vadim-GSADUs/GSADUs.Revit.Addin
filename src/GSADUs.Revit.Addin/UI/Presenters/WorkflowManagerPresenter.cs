using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace GSADUs.Revit.Addin.UI
{
    /// <summary>
    /// Phase 2 presenter stub. Mediates window lifecycle and selection routing.
    /// Initially thin; expanded in later PRs.
    /// </summary>
    internal sealed class WorkflowManagerPresenter
    {
        private readonly WorkflowCatalogService _catalog;
        private readonly IDialogService _dialogs;

        public WorkflowManagerPresenter(WorkflowCatalogService catalog, IDialogService dialogs)
        {
            _catalog = catalog;
            _dialogs = dialogs;
        }

        public AppSettings Settings => _catalog.Settings;

        public void OnLoaded(UIDocument? uidoc)
        {
            // Placeholder: future hydration coordination.
        }

        public void SaveSettings() => _catalog.Save();
    }
}
