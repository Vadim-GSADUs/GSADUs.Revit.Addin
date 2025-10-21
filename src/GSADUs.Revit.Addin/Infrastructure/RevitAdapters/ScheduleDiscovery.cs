using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    internal static class ScheduleDiscovery
    {
        // Host document only
        public static List<(string Id, string Name)> GetAll(Document doc)
        {
            var list = new List<(string, string)>();
            if (doc == null) return list;
            try
            {
                var schedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(vs => vs != null && !vs.IsTemplate)
                    .OrderBy(vs => vs.Name)
                    .ToList();
                foreach (var s in schedules)
                {
                    try
                    {
                        var eid = s.Id;
                        var idText = ToIdString(eid);
                        list.Add((idText, s.Name ?? string.Empty));
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        private static string ToIdString(ElementId id)
        {
            if (id == null) return string.Empty;
            try
            {
                var prop = id.GetType().GetProperty("IntegerValue");
                if (prop != null)
                {
                    var val = prop.GetValue(id);
                    if (val is int i) return i.ToString();
                    if (val is long l) return l.ToString();
                    if (val != null) return Convert.ToInt64(val).ToString();
                }
            }
            catch { }
            try
            {
                // Fallback to ToString(); Revit typically formats ElementId to its integer value
                var s = id.ToString();
                return s ?? string.Empty;
            }
            catch { return string.Empty; }
        }
    }
}
