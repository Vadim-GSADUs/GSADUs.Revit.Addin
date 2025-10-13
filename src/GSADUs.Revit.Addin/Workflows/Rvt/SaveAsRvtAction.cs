using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Logging; // Added namespace for RunLog
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GSADUs.Revit.Addin.Workflows.Rvt
{
    internal sealed class SaveAsRvtAction : IExportAction
    {
        // Updated action identifier to match ActionRegistry descriptor
        public string Id => "export-rvt";

        public int Order => 100;

        public bool RequiresExternalClone => true;

        public bool IsEnabled(AppSettings app, BatchExportSettings request)
        {
            // Use LINQ's Any method to check for the action ID
            return request.ActionIds.Any(id => id == "export-rvt");
        }

        public void Execute(UIApplication uiapp, Document sourceDoc, Document? outDoc, string setName, IList<string> preserveUids, CleanupDiagnostics? cleanupDiag, DeletePlan? planForThisRun, bool isDryRun)
        {
            if (sourceDoc == null || sourceDoc.IsFamilyDocument)
                return;

            if (string.IsNullOrEmpty(sourceDoc.PathName))
                return;

            var settings = AppSettingsStore.Load();
            var baseDir = AppSettingsStore.GetEffectiveOutputDir(settings);
            Directory.CreateDirectory(baseDir);

            var fileName = $"{San(setName)}.rvt";
            var destinationPath = Path.Combine(baseDir, fileName);

            SaveAsOptions saveAsOptions = new SaveAsOptions
            {
                Compact = true,
                OverwriteExistingFile = true
            };

            try
            {
                RunLog.BeginSubsection("SaveAsRvtAction", destinationPath);
                System.Diagnostics.Trace.WriteLine($"BEGIN SaveAsRvtAction for set: {setName}");
                sourceDoc.SaveAs(destinationPath, saveAsOptions);
                System.Diagnostics.Trace.WriteLine($"RVT export saved: {destinationPath}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"RVT export failed: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            finally
            {
                System.Diagnostics.Trace.WriteLine($"END SaveAsRvtAction for set: {setName}");
                RunLog.EndSubsection("SaveAsRvtAction");
            }
        }

        private static string San(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}