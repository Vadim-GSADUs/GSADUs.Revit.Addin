using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Abstractions;
using GSADUs.Revit.Addin.Infrastructure;

namespace GSADUs.Revit.Addin.Commands
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class ToggleAmbiguousRectangles : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var provider = ServiceBootstrap.Provider.GetService(typeof(IProjectSettingsProvider)) as IProjectSettingsProvider
                            ?? new LegacyProjectSettingsProvider();
            var settings = provider.Load();
            settings.DrawAmbiguousRectangles = !settings.DrawAmbiguousRectangles;
            provider.Save(settings);

            TaskDialog.Show("Ambiguous Rectangles",
                $"DrawAmbiguousRectangles is now {(settings.DrawAmbiguousRectangles ? "ON" : "OFF")}. Run Audit to draw rectangles.");
            return Result.Succeeded;
        }
    }
}
