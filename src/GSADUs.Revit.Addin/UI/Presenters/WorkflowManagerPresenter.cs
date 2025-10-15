using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace GSADUs.Revit.Addin.UI
{
    /// <summary>
    /// Phase 2 presenter. Mediates window lifecycle and selection routing.
    /// Currently thin to keep behavior unchanged; expanded in later PRs.
    /// </summary>
    internal sealed class WorkflowManagerPresenter
    {
        private readonly WorkflowCatalogService _catalog;
        private readonly IDialogService _dialogs;

        public WorkflowTabBaseViewModel RvtBase { get; } = new WorkflowTabBaseViewModel();
        public PdfWorkflowTabViewModel PdfWorkflow { get; } = new PdfWorkflowTabViewModel();
        public ImageWorkflowTabViewModel ImageWorkflow { get; } = new ImageWorkflowTabViewModel();
        public WorkflowTabBaseViewModel CsvBase { get; } = new WorkflowTabBaseViewModel();

        public WorkflowManagerPresenter(WorkflowCatalogService catalog, IDialogService dialogs)
        {
            _catalog = catalog;
            _dialogs = dialogs;
        }

        public AppSettings Settings => _catalog.Settings;

        public void OnWindowConstructed(WorkflowManagerWindow win)
        {
            // Placeholder: could set DataContexts in future.
        }

        public void OnLoaded(UIDocument? uidoc, WorkflowManagerWindow win)
        {
            // Placeholder: hook for future hydration orchestration.
        }

        public void OnSavedComboChanged(string tag, WorkflowDefinition? wf, WorkflowManagerWindow win)
        {
            // Placeholder: will coordinate tab routing later.
        }

        public void OnMarkDirty(string tag)
        {
            // Placeholder: could surface dirty-state telemetry.
            try { PerfLogger.Write("WorkflowManager.MarkDirty", tag, TimeSpan.Zero); } catch { }
        }

        public void SaveSettings() => _catalog.Save();
    }
}
