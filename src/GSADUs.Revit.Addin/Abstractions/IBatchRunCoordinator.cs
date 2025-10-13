using Autodesk.Revit.UI;

namespace GSADUs.Revit.Addin
{
    public interface IBatchRunCoordinator
    {
        Result Run(UIApplication uiapp, UIDocument uidoc);
    }
}
