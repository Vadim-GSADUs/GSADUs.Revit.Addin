using Autodesk.Revit.DB;

namespace GSADUs.Revit.Addin
{
    public interface ILogSyncService
    {
        // Ensure the CSV log contains rows for all current Selection Sets and marks missing ones appropriately.
        void EnsureSync(Document doc, IBatchLog log, IBatchLogFactory factory);
    }
}
