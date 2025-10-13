using Autodesk.Revit.UI;
using System.Collections.Generic;

namespace GSADUs.Revit.Addin
{
    public interface IWorkflow
    {
        string Id { get; }
        string DisplayName { get; }
        bool IsInternal { get; } // true = in-place/non-destructive
        Result Execute(UIApplication uiapp, UIDocument uidoc);
    }

    public interface IWorkflowRegistry
    {
        IEnumerable<IWorkflow> All();
        IWorkflow? Find(string id);
    }
}
