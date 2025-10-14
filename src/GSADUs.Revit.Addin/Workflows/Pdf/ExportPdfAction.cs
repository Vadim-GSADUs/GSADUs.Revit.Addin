using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin.Workflows.Pdf
{
    // PDF export implemented using minimal PdfWorkflowRunner
    internal sealed class ExportPdfAction : IExportAction
    {
        public string Id => "export-pdf";
        public int Order => 500;
        public bool RequiresExternalClone => false;
        public bool IsEnabled(AppSettings app, BatchExportSettings request) => true;
        public void Execute(UIApplication uiapp, Document sourceDoc, Document? outDoc, string setName, IList<string> preserveUids, bool isDryRun)
        {
            var appSettings = AppSettingsStore.Load();
            var selectedWorkflowIds = new System.Collections.Generic.HashSet<string>(appSettings.SelectedWorkflowIds ?? new System.Collections.Generic.List<string>());
            var pdfWorkflows = (appSettings.Workflows ?? new System.Collections.Generic.List<WorkflowDefinition>())
                .Where(w => w.Output == OutputType.Pdf && selectedWorkflowIds.Contains(w.Id))
                .ToList();
            if (pdfWorkflows.Count == 0) return;

            var runner = new PdfWorkflowRunner();
            foreach (var wf in pdfWorkflows)
            {
                try { _ = runner.Run(sourceDoc, wf, setName); } catch { }
            }
        }
    }
}
