using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Logging; // Added namespace for RunLog

namespace GSADUs.Revit.Addin.Workflows.Rvt
{
    internal sealed class ResaveRvtAction : IExportAction
    {
        public string Id => "resave-rvt";
        public int Order => 800;
        public bool RequiresExternalClone => true;

        public bool IsEnabled(AppSettings app, BatchExportSettings request)
        {
            // Always enabled for RVT workflows
            return true;
        }

        public void Execute(UIApplication uiapp, Document sourceDoc, Document? outDoc, string setName, System.Collections.Generic.IList<string> preserveUids, CleanupDiagnostics? cleanupDiag, DeletePlan? planForThisRun, bool isDryRun)
        {
            var settings = AppSettingsStore.Load();
            bool compact = settings?.PurgeCompact == true;

            System.Diagnostics.Trace.WriteLine($"BEGIN ResaveRvtAction for set: {setName}");

            try
            {
                if (outDoc == null)
                {
                    System.Diagnostics.Trace.WriteLine("ResaveRvtAction skipped: outDoc is null.");
                    return;
                }

                if (!string.IsNullOrEmpty(outDoc.PathName))
                {
                    if (compact)
                    {
                        System.Diagnostics.Trace.WriteLine("Performing compact save...");
                        outDoc.SaveAs(outDoc.PathName, new SaveAsOptions { OverwriteExistingFile = true, Compact = true });
                    }
                    else
                    {
                        System.Diagnostics.Trace.WriteLine("Performing regular save...");
                        outDoc.Save();
                    }
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine("Performing save with new SaveAs options...");
                    outDoc.SaveAs(outDoc.PathName, new SaveAsOptions { OverwriteExistingFile = true, Compact = compact });
                }

                System.Diagnostics.Trace.WriteLine($"ResaveRvtAction completed: compact={compact} path={outDoc.PathName}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ResaveRvtAction failed: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            finally
            {
                System.Diagnostics.Trace.WriteLine($"END ResaveRvtAction for set: {setName}");
            }
        }
    }
}