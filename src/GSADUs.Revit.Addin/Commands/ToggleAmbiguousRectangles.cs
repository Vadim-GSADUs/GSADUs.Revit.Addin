using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace GSADUs.Revit.Addin.Commands
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class ToggleAmbiguousRectangles : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var s = GSADUs.Revit.Addin.AppSettingsStore.Load();
            s.DrawAmbiguousRectangles = !s.DrawAmbiguousRectangles;
            GSADUs.Revit.Addin.AppSettingsStore.Save(s);

            TaskDialog.Show("Ambiguous Rectangles",
                $"DrawAmbiguousRectangles is now {(s.DrawAmbiguousRectangles ? "ON" : "OFF")}. Run Audit to draw rectangles.");
            return Result.Succeeded;
        }
    }
}
