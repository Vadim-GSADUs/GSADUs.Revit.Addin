using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Logging; // Added namespace for RunLog
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin.Workflows.Rvt
{
    internal sealed class OpenForDryRunAction : IExportAction
    {
        public string Id => "open-dryrun";

        public int Order => 150;

        public bool RequiresExternalClone => true;

        public bool IsEnabled(AppSettings app, BatchExportSettings request)
        {
            return request.ActionIds.Any(id => id == "open-dryrun");
        }

        public void Execute(UIApplication uiapp, Document sourceDoc, Document? outDoc, string setName, IList<string> preserveUids, CleanupDiagnostics? cleanupDiag, DeletePlan? planForThisRun, bool isDryRun)
        {
            if (!isDryRun)
                return;

            if (outDoc == null)
                return;

            // TODO: When the runner supplies the cloned path, ensure it is opened/activated here or pre-opened by the runner.
            // For now, assume outDoc is already open in DryRun mode and no further action is required.
        }
    }
}