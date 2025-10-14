using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Logging;
using System.Collections.Generic;
using System.Diagnostics;
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

            var targetDoc = outDoc ?? sourceDoc;

            try
            {
                RunLog.BeginSubsection("SaveAsRvtAction", destinationPath);
                Trace.WriteLine($"BEGIN_TX SaveAsRvtAction set=\"{setName}\" target=\"{destinationPath}\" corr={RunLog.CorrId}");
                targetDoc.SaveAs(destinationPath, saveAsOptions);
                Trace.WriteLine($"END_TX SaveAsRvtAction status=success set=\"{setName}\" corr={RunLog.CorrId}");
            }
            catch (System.Exception ex)
            {
                Trace.WriteLine($"END_TX SaveAsRvtAction status=fail set=\"{setName}\" ex={ex.GetType().Name} msg=\"{ex.Message}\" corr={RunLog.CorrId}");
                throw;
            }
            finally
            {
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