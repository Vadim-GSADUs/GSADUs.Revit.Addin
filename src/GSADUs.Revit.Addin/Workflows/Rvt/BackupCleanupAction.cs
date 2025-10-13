using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Logging; // Added namespace for RunLog
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace GSADUs.Revit.Addin.Workflows.Rvt
{
    internal sealed class BackupCleanupAction : IExportAction
    {
        public string Id => "backup-cleanup";
        public int Order => 900;
        public bool RequiresExternalClone => false;

        // Known Revit backup name variants:
        //  - filename.0001.rvt
        //  - filename-0001.rvt
        //  - filename.rvt.0001
        private static readonly Regex RxDotThenNumber = new Regex(@"\.\d{3,6}\.rvt$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxDashThenNumber = new Regex(@"-\d{3,6}\.rvt$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxAfterExtNumber = new Regex(@"\.rvt\.\d{3,6}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public bool IsEnabled(AppSettings app, BatchExportSettings request) => true;

        public void Execute(
            UIApplication uiapp,
            Document sourceDoc,
            Document? outDoc,
            string setName,
            IList<string> preserveUids,
            CleanupDiagnostics? cleanupDiag,
            DeletePlan? planForThisRun,
            bool isDryRun)
        {
            System.Diagnostics.Trace.WriteLine($"BEGIN BackupCleanupAction for set: {setName}");

            string? targetPath = outDoc?.PathName ?? sourceDoc?.PathName;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                System.Diagnostics.Trace.WriteLine("BackupCleanupAction skipped: targetPath is null or empty.");
                return;
            }

            string? dir = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                System.Diagnostics.Trace.WriteLine("BackupCleanupAction skipped: directory does not exist.");
                return;
            }

            try
            {
                System.Diagnostics.Trace.WriteLine($"Scanning directory for backup files: {dir}");
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = file;
                    if (RxDotThenNumber.IsMatch(name) || RxDashThenNumber.IsMatch(name) || RxAfterExtNumber.IsMatch(name))
                    {
                        try
                        {
                            System.Diagnostics.Trace.WriteLine($"Deleting backup file: {file}");
                            File.Delete(file);
                        }
                        catch (System.Exception ex)
                        {
                            System.Diagnostics.Trace.WriteLine($"Failed to delete backup file: {file}. Error: {ex.Message}");
                        }
                    }
                }
                System.Diagnostics.Trace.WriteLine("BackupCleanupAction completed successfully.");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"BackupCleanupAction failed: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            finally
            {
                System.Diagnostics.Trace.WriteLine($"END BackupCleanupAction for set: {setName}");
            }
        }
    }
}