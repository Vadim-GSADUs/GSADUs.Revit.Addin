using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Linq;

namespace GSADUs.Revit.Addin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ClearAmbiguityRectangles : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uiapp = data.Application;
            var doc = uiapp.ActiveUIDocument.Document;

            var toDelete = new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(e =>
                {
                    var p = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    return p != null && p.AsString() == "Ambiguity IBB";
                })
                .Select(e => e.Id)
                .ToList();

            if (toDelete.Count == 0)
            {
                TaskDialog.Show("Ambiguity Rectangles", "No tagged rectangles found.");
                return Result.Succeeded;
            }

            using (var tx = new Transaction(doc, "Clear Ambiguity Rectangles"))
            {
                tx.Start();
                doc.Delete(toDelete);
                tx.Commit();
            }

            TaskDialog.Show("Ambiguity Rectangles", $"Deleted {toDelete.Count} rectangles.");
            return Result.Succeeded;
        }
    }
}
