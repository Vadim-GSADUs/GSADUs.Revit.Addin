using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Windows;

namespace GSADUs.Revit.Addin.UI
{
    internal static class BatchExportWindowHost
    {
        private static readonly Dictionary<string, BatchExportWindow> _windows = new();

        public static BatchExportWindow? ShowOrActivate(UIDocument uidoc, Window? owner = null)
        {
            if (uidoc == null || uidoc.Document == null)
                return null;

            var doc = uidoc.Document;
            var key = GetKey(doc);
            if (_windows.TryGetValue(key, out var existing) && existing != null && existing.IsLoaded)
            {
                try
                {
                    if (!existing.IsVisible) existing.Show();
                    existing.Activate();
                    existing.Topmost = true;
                    existing.Topmost = false;
                }
                catch { }
                return existing;
            }

            var win = new BatchExportWindow(System.Array.Empty<string>(), uidoc);
            if (owner != null)
            {
                try { win.Owner = owner; } catch { }
            }

            _windows[key] = win;
            win.Closed += (_, _) =>
            {
                try { _windows.Remove(key); } catch { }
            };

            win.Show();
            try
            {
                win.Activate();
                win.Topmost = true;
                win.Topmost = false;
            }
            catch { }

            return win;
        }

        private static string GetKey(Document doc)
        {
            try
            {
                var path = doc.PathName;
                if (!string.IsNullOrWhiteSpace(path))
                    return $"BE::{path}";

                var title = doc.Title;
                if (!string.IsNullOrWhiteSpace(title))
                    return $"BE::{title}";

                return $"BE::<unknown>::{doc.GetHashCode()}";
            }
            catch
            {
                return "BE::<no-doc>";
            }
        }
    }
}
