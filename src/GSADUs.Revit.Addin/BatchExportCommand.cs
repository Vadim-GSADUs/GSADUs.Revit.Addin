using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Logging;
using GSADUs.Revit.Addin.UI;
using System.Diagnostics;

namespace GSADUs.Revit.Addin
{
    [Transaction(TransactionMode.Manual)]
    public class BatchExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet set)
        {
            // Ensure DI and ExternalEvent-backed services are created while in a valid Revit API context.
            ServiceBootstrap.InitializeOrThrow(data.Application);
            RevitUiContext.Current = data.Application;

            // Eagerly resolve settings save infrastructure so ExternalEvent.Create runs under API context.
            try
            {
                var provider = ServiceBootstrap.Provider;
                _ = provider.GetService(typeof(GSADUs.Revit.Addin.UI.ProjectSettingsSaveExternalEvent));
                _ = provider.GetService(typeof(IProjectSettingsSaveService));
            }
            catch
            {
                // Swallow; logging below will still function with defaults if DI is unavailable.
            }

            var sw = Stopwatch.StartNew();

            // Retrieve the active document's file name for logging
            var rvtFileName = data.Application?.ActiveUIDocument?.Document?.PathName ?? "Unknown";

            RunLog.Begin("BatchExport", rvtFileName);
            Trace.WriteLine($"DOC {data.Application?.ActiveUIDocument?.Document?.PathName} corr={RunLog.CorrId}");

            try
            {
                // If an instance already open, bring it to front and exit.
                try { if (BatchExportWindow.TryActivateExisting()) return Result.Succeeded; } catch { }

                var provider = ServiceBootstrap.Provider;
                var coord = provider.GetService(typeof(IBatchRunCoordinator)) as IBatchRunCoordinator;
                if (coord == null)
                {
                    var dialogs = provider.GetService(typeof(IDialogService)) as IDialogService;
                    if (dialogs != null)
                        dialogs.Info("Batch Export", "Internal error: coordinator service was not registered.");
                    return Result.Failed;
                }
                return coord.Run(data.Application, data.Application.ActiveUIDocument);
            }
            finally
            {
                RunLog.End("BatchExport", sw.ElapsedMilliseconds);
                Trace.WriteLine($"LOG \"{RunLog.FilePath}\" corr={RunLog.CorrId}");
            }
        }
    }
}
