using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Logging; // Added namespace for RunLog
using System.Collections.Generic;

namespace GSADUs.Revit.Addin.Workflows.Rvt
{
    internal sealed class CleanupExportsAction : IExportAction
    {
        public string Id => "cleanup";
        public int Order => 300;
        public bool RequiresExternalClone => true;
        public bool IsEnabled(AppSettings app, BatchExportSettings request) => app.DefaultCleanup;

        public void Execute(UIApplication uiapp, Document sourceDoc, Document? outDoc, string setName, IList<string> preserveUids, CleanupDiagnostics? cleanupDiag, DeletePlan? planForThisRun, bool isDryRun)
        {
            System.Diagnostics.Trace.WriteLine($"BEGIN CleanupExportsAction for set: {setName}");

            if (outDoc == null)
            {
                System.Diagnostics.Trace.WriteLine("CleanupExportsAction skipped: outDoc is null (external-only cleanup).");
                return;
            }

            try
            {
                System.Diagnostics.Trace.WriteLine("Running ExportCleanup...");
                _ = ExportCleanup.Run(outDoc, preserveUids, new CleanupOptions(), cleanupDiag, planForThisRun, suppressWarnings: !isDryRun);
                System.Diagnostics.Trace.WriteLine("ExportCleanup completed successfully.");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"CleanupExportsAction failed: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            finally
            {
                System.Diagnostics.Trace.WriteLine($"END CleanupExportsAction for set: {setName}");
            }
        }
    }
}