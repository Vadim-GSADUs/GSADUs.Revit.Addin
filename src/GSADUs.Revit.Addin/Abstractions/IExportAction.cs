using GSADUs.Revit.Addin.Workflows.Rvt;

namespace GSADUs.Revit.Addin
{
    // Minimal synchronous action interface for Phase 2
    public interface IExportAction
    {
        string Id { get; }
        int Order { get; }
        bool RequiresExternalClone { get; }

        // Whether this action should run given current app and run settings
        bool IsEnabled(AppSettings app, BatchExportSettings request);

        // Execute against the given context. outDoc may be null if action is in-place (not used yet).
        void Execute(Autodesk.Revit.UI.UIApplication uiapp,
                     Autodesk.Revit.DB.Document sourceDoc,
                     Autodesk.Revit.DB.Document? outDoc,
                     string setName,
                     System.Collections.Generic.IList<string> preserveUids,
                     CleanupDiagnostics? cleanupDiag,
                     DeletePlan? planForThisRun,
                     bool isDryRun);
    }
}
